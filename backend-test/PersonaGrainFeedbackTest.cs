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

public class PersonaGrainFeedbackTest : TestKitBase
{
    private readonly Guid _personaId = Guid.NewGuid();
    private readonly Guid _chatGroupId = Guid.NewGuid();
    private const string PersonaName = "Alice";

    [Fact]
    public async Task CancelGenerationAsync_CancelsInFlightSpeaking()
    {
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
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) == 1 ? decisionEp.Object : speakingEp.Object);

        var personaRoot = Silo.AddProbe<IPersonaRootGrain>(Guid.Empty);
        personaRoot.Setup(g => g.GetAll()).ReturnsAsync(new Persona[]
        {
            new() { Id = _personaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null }
        });

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
        chatGroup.Setup(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()))
            .ReturnsAsync(() => ++msgIdSeq);
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });
        var partyId = Guid.NewGuid();
        chatGroup.Setup(g => g.GetPartyIdAsync()).ReturnsAsync(partyId);

        var partyGrain = Silo.AddProbe<IPartyGrain>(partyId);
        partyGrain.Setup(g => g.GetCastAsync()).ReturnsAsync((IReadOnlyList<CastMember>)new List<CastMember>
        {
            CastMember.Create(
                new PartyParticipant { Id = _personaId, Name = PersonaName, Driver = DriverKind.LLM },
                new Persona { Id = _personaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null })
        });
        chatGroup.Setup(g => g.GetCastAsync()).ReturnsAsync(() => RoomCast.Create(
            partyGrain.Object.GetCastAsync().Result,
            new HashSet<Guid> { _personaId },
            ChatGroupKinds.Group));
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(2);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Returns(Task.CompletedTask);
        var outcomes = new List<TurnOutcome>();
        chatGroup.Setup(g => g.EndTurnAsync(It.IsAny<int>(), It.IsAny<TurnOutcome>()))
            .Callback<int, TurnOutcome>((_, o) => outcomes.Add(o))
            .Returns(Task.CompletedTask);

        Silo.AddService<ILoggerFactory>(NullLoggerFactory.Instance);
        Silo.AddService(new RaceTrigger(Silo.GrainFactory, NullLoggerFactory.Instance, new BackendTest.Infrastructure.MenuJudge(() => null)));

        var memoryRepo = new Mock<IMemoryRepository>();
        memoryRepo.Setup(m => m.RecallAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Array.Empty<RecalledMemory>());
        memoryRepo.Setup(m => m.RecallStancesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StanceLine>());
        Silo.AddService(new ResponsePipeline(
            Silo.GrainFactory,
            memoryRepo.Object,
            NullLoggerFactory.Instance,
            NullLogger<ResponsePipeline>.Instance,
            new BackendTest.Infrastructure.MenuJudge(() => null)));

        var grain = await Silo.CreateGrainAsync<PersonaGrain>(_personaId);
        await grain.SetPersona(PersonaName, $"You are {PersonaName}.", null);

        var cancelTask = Task.Run(async () =>
        {
            await startedSpeaking.Task.ConfigureAwait(false);
            await grain.CancelGenerationAsync().ConfigureAwait(false);
        });

        await grain.NotifyMessageAsync(_chatGroupId, triggering, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(15));
        await cancelTask.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal("cancelled", Assert.IsType<TurnOutcome.Failed>(Assert.Single(outcomes)).Error);
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
