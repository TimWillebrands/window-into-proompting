using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.TestKit;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Streaming;

namespace BackendTest;

/// <summary>
/// Orleans-specific invariants for <see cref="PersonaGrain"/>. After the
/// <see cref="ResponsePipeline"/> extraction (issue #63), PersonaGrain is a thin
/// shell over persisted state + <see cref="InFlightStore"/> + injected RaceTrigger
/// and Pipeline. Per-turn orchestration behavior lives in <c>ResponsePipelineTest</c>;
/// race-trigger behavior lives in <c>RaceTriggerTest</c>. What's left to verify here
/// are properties that only exist at the grain boundary.
///
/// Coverage:
///   • <c>CancelGenerationAsync</c> via the <see cref="IPersonaGrain"/> interface
///     cancels in-flight work — the cancel routes through the per-grain
///     <see cref="InFlightStore"/> and trips the linked CTS that <c>NotifyMessageAsync</c>
///     is currently awaiting in the speaking phase.
/// </summary>
public class PersonaGrainFeedbackTest : TestKitBase
{
    private readonly Guid _personaId = Guid.NewGuid();
    private readonly Guid _chatGroupId = Guid.NewGuid();
    private const string PersonaName = "Alice";

    [Fact]
    public async Task CancelGenerationAsync_CancelsInFlightSpeaking()
    {
        // Arrange: drive PersonaGrain through to the speaking phase with a hanging endpoint.
        // While speaking hangs, call CancelGenerationAsync via the grain interface — the
        // grain must route through InFlightStore.CancelAllAsync to trip the speaking CTS,
        // surfacing as MarkGenerationFailedAsync("cancelled") through the pipeline's external-
        // cancel branch.
        var decisionJson = """{"gutReaction":"on it","memoryToReference":null,"wouldSay":"writing...","respond":true}""";

        var decisionEp = new Mock<ILlmEndpointGrain>();
        decisionEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "decider" });
        decisionEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SingleChunk(decisionJson, ct));

        var startedSpeaking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var speakingEp = new Mock<ILlmEndpointGrain>();
        speakingEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "speaker" });
        speakingEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SignalThenHang(startedSpeaking, ct));

        var routeCall = 0;
        var router = Silo.AddProbe<ILlmRouterGrain>(0L);
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) == 1 ? decisionEp.Object : speakingEp.Object);

        var personaRoot = Silo.AddProbe<IPersonaRootGrain>(Guid.Empty);
        personaRoot.Setup(g => g.GetAll()).ReturnsAsync(new Persona[]
        {
            new() { Id = _personaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null }
        });

        // Triggering content: no name mention, no trailing '?'. Combined with a non-zero
        // round count this keeps urge under the 0.9 auto-respond cutoff so the decision
        // LLM actually fires (we need that to reach speaking, where the cancel races).
        var triggering = new ChatMessage
        {
            MessageId = 1,
            SenderId = Guid.NewGuid(),
            SenderType = "user",
            Content = "anyone home",
            ChatGroupId = _chatGroupId,
        };

        var msgIdSeq = 0;
        var chatGroup = Silo.AddProbe<IChatGroupGrain>(_chatGroupId);
        chatGroup.Setup(g => g.GetNextMessageIdAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ReturnsAsync(() => ++msgIdSeq);
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });
        chatGroup.Setup(g => g.GetParticipantsAsync()).ReturnsAsync(new List<PartyParticipant>
        {
            new() { Id = _personaId, Name = PersonaName, IsUser = false }
        });
        chatGroup.Setup(g => g.GetPartyIdAsync()).ReturnsAsync(Guid.NewGuid());
        chatGroup.Setup(g => g.GetScenarioAsync()).ReturnsAsync((string?)null);
        // Two prior assistant rounds → no cold-open bump. Stays under the obvious-skip
        // threshold too (no recent self-messages).
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(2);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        Silo.AddService<ILoggerFactory>(NullLoggerFactory.Instance);
        Silo.AddService(new RaceTrigger(Silo.GrainFactory, NullLoggerFactory.Instance));

        var memoryRepo = new Mock<IMemoryRepository>();
        memoryRepo.Setup(m => m.RecallRecentSnippetsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        Silo.AddService(new ResponsePipeline(
            Silo.GrainFactory,
            memoryRepo.Object,
            NullLoggerFactory.Instance,
            NullLogger<ResponsePipeline>.Instance));

        var grain = await Silo.CreateGrainAsync<PersonaGrain>(_personaId);
        await grain.SetPersona(PersonaName, $"You are {PersonaName}.", null);

        // Act: kick off NotifyMessageAsync in the background; once the speaking phase is
        // streaming, call CancelGenerationAsync through the grain interface. The grain
        // routes through its InFlightStore, which cancels the linked CTS the pipeline is
        // awaiting on.
        // TestKit dispatches grain calls on the test's synchronization context. To race
        // CancelGenerationAsync against an in-flight NotifyMessageAsync we need to leave
        // the test's awaiter free, so the grain proxy's continuation can fire while we
        // sit on startedSpeaking. Task.Run hops to the threadpool for that.
        _ = Task.Run(async () =>
        {
            await startedSpeaking.Task.ConfigureAwait(false);
            await grain.CancelGenerationAsync().ConfigureAwait(false);
        });

        await grain.NotifyMessageAsync(_chatGroupId, triggering, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(15));

        // Assert: pipeline's external-cancel branch ran — no terminal write, no emote,
        // MarkGenerationFailedAsync called with "cancelled".
        chatGroup.Verify(g => g.MarkGenerationFailedAsync(
            It.IsAny<int>(), It.Is<string>(s => s == "cancelled")), Times.Once);
        chatGroup.Verify(g => g.AppendMessageAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        chatGroup.Verify(g => g.MarkGenerationCancelledAsEmoteAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> SingleChunk(
        string data,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, data);
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> SignalThenHang(
        TaskCompletionSource<bool> signal,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "writing...");
        signal.TrySetResult(true);
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }
}
