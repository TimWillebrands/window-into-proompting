using System.Runtime.CompilerServices;
using System.Text.Json;
using BackendTest.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans.TestKit;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace BackendTest;

/// <summary>
/// Tests for the <see cref="PersonaGrain"/> half of the fanout + feedback loop.
///
/// Approach: PersonaGrain is a plain Grain (not JournaledGrain), so it activates cleanly
/// inside TestKit without special DI setup. IChatGroupGrain is probed — every callback
/// (GetNextMessageIdAsync, AppendMessageAsync, MarkGenerationStopped/Failed) is captured
/// so tests can assert on the terminal state without needing a real ChatGroupGrain.
///
/// Coverage:
///   • Happy path: direct mention → auto-respond → AppendMessageAsync called
///   • Happy path: generation content streamed → AppendMessageAsync carries correct text
///   • Decline path: LLM says don't respond → MarkGenerationStoppedAsync called
///   • All-fail path: endpoint always throws → retries exhausted → MarkGenerationFailedAsync
///   • Mid-stream failure: endpoint dies after first chunk → retries → eventual failure
///   • Cancellation: token cancelled mid-generation → MarkGenerationFailedAsync("cancelled")
///   • Retry success: endpoint fails once then succeeds → AppendMessageAsync called (not failed)
///
/// NOTE: End-to-end loop tests (ChatGroupGrain fanout → PersonaGrain → ChatGroupGrain feedback)
/// are in ChatGroupFanoutTest.cs and require JournaledGrain DI — see that file for the blocker.
/// </summary>
public class PersonaGrainFeedbackTest : TestKitBase
{
    private readonly Guid _personaId = Guid.NewGuid();
    private readonly Guid _chatGroupId = Guid.NewGuid();
    private const string PersonaName = "Alice";

    // ── Setup helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a ready-to-use PersonaGrain with all dependencies probed.
    /// The triggering message must contain the persona's name to trigger auto-respond
    /// (skips the decision LLM call).
    /// </summary>
    private async Task<(IPersonaGrain grain, Mock<IChatGroupGrain> chatGroup)>
        SetupPersona(
            FakeEndpointGrain endpoint,
            List<PartyParticipant>? extraParticipants = null)
    {
        // Chat group probe: captures what the persona does after its decision
        var msgIdSeq = 0;
        var chatGroup = Silo.AddProbe<IChatGroupGrain>(_chatGroupId);
        chatGroup
            .Setup(g => g.GetNextMessageIdAsync(It.IsAny<Guid?>(), It.IsAny<string>()))
            .ReturnsAsync(() => ++msgIdSeq);
        // Return history containing a message that mentions the persona by name.
        // PersonaDecisionService.CalculateResponseUrge checks history.LastOrDefault().Content
        // for the persona's name — a mention triggers urge ≥ 0.9 → auto-respond shortcut,
        // skipping the decision LLM call so tests only exercise the generation endpoint.
        chatGroup
            .Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>
            {
                new()
                {
                    MessageId = 1,
                    SenderType = "user",
                    SenderId = Guid.NewGuid(),
                    Content = $"Hey {PersonaName}, what do you think?",
                    ChatGroupId = _chatGroupId
                }
            });
        chatGroup
            .Setup(g => g.GetParticipantsAsync())
            .ReturnsAsync(new List<PartyParticipant>(extraParticipants ?? [])
            {
                new() { Id = _personaId, Name = PersonaName, IsUser = false }
            });
        chatGroup
            .Setup(g => g.CountTrailingAssistantMessagesAsync())
            .ReturnsAsync(0);
        chatGroup
            .Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Returns(Task.CompletedTask);
        chatGroup
            .Setup(g => g.AppendMessageAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chatGroup
            .Setup(g => g.MarkGenerationStoppedAsync(It.IsAny<int>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        chatGroup
            .Setup(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // PersonaRootGrain probe: needed when persona fetches persona details for prompt building
        var personaRoot = Silo.AddProbe<IPersonaRootGrain>(Guid.Empty);
        personaRoot
            .Setup(g => g.GetAll())
            .ReturnsAsync(new Persona[]
            {
                new() { Id = _personaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null }
            });

        // PersonaGrain injects ILoggerFactory to create PersonaDecisionService's logger.
        // TestKit's auto-wired logger factory can produce loggers with null internals;
        // NullLoggerFactory is safe.
        Silo.AddService<ILoggerFactory>(NullLoggerFactory.Instance);

        // Router probe: routes all job complexities to the same fake endpoint
        endpoint.RegisterOn(Silo);
        var router = Silo.AddProbe<ILlmRouterGrain>(0L);
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Reference);

        var grain = await Silo.CreateGrainAsync<PersonaGrain>(_personaId);
        await grain.SetPersona(PersonaName, $"You are {PersonaName}.", null);

        return (grain, chatGroup);
    }

    /// <summary>Triggering message that mentions the persona by name → auto-respond shortcut fires.</summary>
    private ChatMessage MentionMessage(string extra = "") =>
        new()
        {
            MessageId = 1,
            SenderId = Guid.NewGuid(),
            SenderType = "user",
            Content = $"Hey {PersonaName}, {extra}",
            ChatGroupId = _chatGroupId,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

    /// <summary>Triggering message with no persona mention — forces the LLM decision call.</summary>
    private ChatMessage NeutralMessage() =>
        new()
        {
            MessageId = 1,
            SenderId = Guid.NewGuid(),
            SenderType = "user",
            Content = "What time is it?",
            ChatGroupId = _chatGroupId,
            SendAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

    private static FakeEndpointGrain MakeEndpoint(IEnumerable<LlmGenerationEvent>? chunks = null)
        => new(Guid.NewGuid(), scriptedChunks: chunks);

    private static async IAsyncEnumerable<LlmGenerationEvent> SingleJsonChunk(
        string json,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, json);
    }

    // ── Happy path — direct mention → auto-respond ────────────────────────────

    [Fact]
    public async Task NotifyMessage_DirectMention_CallsAppendMessageAsync()
    {
        // Arrange
        var endpoint = MakeEndpoint([
            new(LlmGenerationEvent.ContentChunk, "Hello from Alice!")
        ]);
        var (grain, chatGroup) = await SetupPersona(endpoint);

        // Act
        await grain.NotifyMessageAsync(_chatGroupId, MentionMessage("what do you think?"), CancellationToken.None);

        // Assert
        // TODO: tighten to exact-payload assertion — Contains lets prefixed/suffixed/duplicated strings pass silently.
        // TODO: add companion test for manual cancel path (CancelGenerationAsync while NotifyMessageAsync in-flight) mirroring NotifyMessage_CancelledDuringGeneration_CallsMarkGenerationFailed_WithCancelled.
        chatGroup.Verify(
            g => g.AppendMessageAsync(
                It.IsAny<int>(),
                It.Is<string>(s => s.Contains("Hello from Alice!")),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ChatMessageMetadata?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        chatGroup.Verify(g => g.MarkGenerationStoppedAsync(It.IsAny<int>(), It.IsAny<string?>()), Times.Never);
        chatGroup.Verify(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NotifyMessage_DirectMention_ContentAccumulatedAcrossChunks()
    {
        // Arrange: two chunks that together form the full response
        var endpoint = MakeEndpoint([
            new(LlmGenerationEvent.ContentChunk, "part one "),
            new(LlmGenerationEvent.ContentChunk, "part two"),
        ]);
        var (grain, chatGroup) = await SetupPersona(endpoint);

        string? capturedContent = null;
        chatGroup
            .Setup(g => g.AppendMessageAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, string?, string?, ChatMessageMetadata?, CancellationToken>(
                (_, content, _, _, _, _) => capturedContent = content)
            .Returns(Task.CompletedTask);

        // Act
        await grain.NotifyMessageAsync(_chatGroupId, MentionMessage(), CancellationToken.None);

        // Assert
        Assert.Equal("part one part two", capturedContent);
    }

    // ── Decline path — LLM says don't respond ────────────────────────────────

    [Fact]
    public async Task NotifyMessage_LlmSaysDecline_CallsMarkGenerationStopped()
    {
        // Arrange: neutral message (no name mention) forces LLM decision call.
        // The endpoint returns a JSON response that says don't respond.
        var declineJson = JsonSerializer.Serialize(
            new { respond = false, instruction = "stay quiet", reason = "not relevant" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var endpointId = Guid.NewGuid();
        var decisionMock = new Mock<ILlmEndpointGrain>();
        decisionMock
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SingleJsonChunk(declineJson, ct));

        // A neutral message won't auto-respond, so we need a decision-endpoint
        var msgIdSeq = 0;
        var chatGroup = Silo.AddProbe<IChatGroupGrain>(_chatGroupId);
        chatGroup.Setup(g => g.GetNextMessageIdAsync(It.IsAny<Guid?>(), It.IsAny<string>())).ReturnsAsync(() => ++msgIdSeq);
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>())).ReturnsAsync(new List<ChatMessage>());
        chatGroup.Setup(g => g.GetParticipantsAsync()).ReturnsAsync(new List<PartyParticipant> { new() { Id = _personaId, Name = PersonaName } });
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(0);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationStoppedAsync(It.IsAny<int>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var personaRoot = Silo.AddProbe<IPersonaRootGrain>(Guid.Empty);
        personaRoot.Setup(g => g.GetAll()).ReturnsAsync(new Persona[] { new() { Id = _personaId, Name = PersonaName, SystemPrompt = "..." } });

        Silo.AddService<ILoggerFactory>(NullLoggerFactory.Instance);

        var router = Silo.AddProbe<ILlmRouterGrain>(0L);
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(decisionMock.Object);

        var grain = await Silo.CreateGrainAsync<PersonaGrain>(_personaId);
        await grain.SetPersona(PersonaName, "...", null);

        // Act
        await grain.NotifyMessageAsync(_chatGroupId, NeutralMessage(), CancellationToken.None);

        // Assert: persona decided NOT to respond
        chatGroup.Verify(g => g.MarkGenerationStoppedAsync(It.IsAny<int>(), It.IsAny<string?>()), Times.Once);
        chatGroup.Verify(g => g.AppendMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Unhappy path — all endpoints permanently choked ──────────────────────

    /// <summary>
    /// When every endpoint throws on every attempt, PersonaGrain exhausts its 2-retry loop
    /// and calls MarkGenerationFailedAsync. No AppendMessage should occur.
    ///
    /// Note: PersonaGrain delays 2s + 4s between retries. This test takes ~6s.
    /// </summary>
    [Fact]
    public async Task NotifyMessage_AllEndpointsFail_CallsMarkGenerationFailed()
    {
        // Arrange: endpoint is permanently choked
        var endpoint = MakeEndpoint();
        endpoint.Choke();
        var (grain, chatGroup) = await SetupPersona(endpoint);

        // Act — allow generous timeout for the 2+4s retry delays
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await grain.NotifyMessageAsync(_chatGroupId, MentionMessage(), cts.Token);

        // Assert
        chatGroup.Verify(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Once);
        chatGroup.Verify(g => g.AppendMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Unhappy path — cancellation mid-generation ───────────────────────────

    [Fact]
    public async Task NotifyMessage_CancelledDuringGeneration_CallsMarkGenerationFailed_WithCancelled()
    {
        // Arrange: endpoint hangs indefinitely — cancellation must surface cleanly
        using var cts = new CancellationTokenSource();
        var endpointId = Guid.NewGuid();
        var hangingMock = new Mock<ILlmEndpointGrain>();
        hangingMock
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => HangUntilCancelled(ct));

        var msgIdSeq = 0;
        var chatGroup = Silo.AddProbe<IChatGroupGrain>(_chatGroupId);
        chatGroup.Setup(g => g.GetNextMessageIdAsync(It.IsAny<Guid?>(), It.IsAny<string>())).ReturnsAsync(() => ++msgIdSeq);
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>())).ReturnsAsync(new List<ChatMessage>());
        chatGroup.Setup(g => g.GetParticipantsAsync()).ReturnsAsync(new List<PartyParticipant> { new() { Id = _personaId, Name = PersonaName } });
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(0);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.AppendMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationStoppedAsync(It.IsAny<int>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var personaRoot = Silo.AddProbe<IPersonaRootGrain>(Guid.Empty);
        personaRoot.Setup(g => g.GetAll()).ReturnsAsync(new Persona[] { new() { Id = _personaId, Name = PersonaName, SystemPrompt = "..." } });

        Silo.AddService<ILoggerFactory>(NullLoggerFactory.Instance);

        var router = Silo.AddProbe<ILlmRouterGrain>(0L);
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(hangingMock.Object);

        var grain = await Silo.CreateGrainAsync<PersonaGrain>(_personaId);
        await grain.SetPersona(PersonaName, "...", null);

        // Act: cancel 100ms into the hanging generation
        cts.CancelAfter(100);
        await grain.NotifyMessageAsync(_chatGroupId, MentionMessage(), cts.Token);

        // Assert: cancelled is surfaced as failure with "cancelled" error
        chatGroup.Verify(
            g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.Is<string>(s => s.Contains("cancel"))),
            Times.Once);
        chatGroup.Verify(
            g => g.AppendMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Unhappy path — endpoint fails once, succeeds on retry ────────────────

    [Fact]
    public async Task NotifyMessage_EndpointFailsOnce_RetriesAndSucceeds()
    {
        // Arrange: endpoint fails on first call, succeeds on subsequent calls.
        // MentionMessage() triggers auto-respond (urge ≥ 0.9 from name match), so the
        // router is only called for generation — not for the decision LLM call.
        // That means FailFirstNAttempts(1) affects the first generation attempt, not
        // an unrelated decision call.
        var endpoint = MakeEndpoint([
            new(LlmGenerationEvent.ContentChunk, "retry worked!")
        ]);
        endpoint.FailFirstNAttempts(1); // fail attempt 1, succeed on attempt 2

        var (grain, chatGroup) = await SetupPersona(endpoint);

        // Act — 1 failure = 2s delay before retry
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await grain.NotifyMessageAsync(_chatGroupId, MentionMessage(), cts.Token);

        // Assert: eventually succeeded — content from retry appears
        chatGroup.Verify(
            g => g.AppendMessageAsync(
                It.IsAny<int>(),
                It.Is<string>(s => s.Contains("retry worked!")),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ChatMessageMetadata?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        chatGroup.Verify(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(2, endpoint.TotalAttempts);    // first fail + one success (retry happened)
        Assert.Equal(1, endpoint.TotalJobsReceived); // only the successful attempt counts
    }

    // ── Unhappy path — mid-stream failure after some chunks ──────────────────

    [Fact]
    public async Task NotifyMessage_EndpointFailsMidStream_RetriesAndAccumulatesFromFreshStream()
    {
        // Arrange: endpoint yields 1 chunk then fails; on retry it yields full content
        var endpoint = MakeEndpoint([
            new(LlmGenerationEvent.ContentChunk, "full response on retry"),
        ]);
        endpoint.FailAfterChunks(0); // throw after 0 chunks on first call

        // Override: first call → fail after 0 chunks; subsequent calls → succeed
        // FailAfterChunks + FailFirstN don't compose, but FailAfterChunks only applies to
        // calls where failFirstN hasn't already thrown. We reset it after triggering once.
        // Simpler: use FailFirstNAttempts which throws before streaming; the mid-stream test
        // uses a custom mock instead.
        var midStreamId = Guid.NewGuid();
        var callCount = 0;
        var midStreamMock = new Mock<ILlmEndpointGrain>();
        midStreamMock
            .Setup(e => e.GetModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LlmModel>
            {
                new() { Name = "test", EndpointProviderGrainId = midStreamId, ProviderType = "ollama", SupportedComplexities = JobComplexity.General }
            });
        midStreamMock.Setup(e => e.PressureAsync()).ReturnsAsync(0);
        midStreamMock
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) =>
            {
                var n = Interlocked.Increment(ref callCount);
                return n == 1
                    ? MidStreamError("partial", ct)
                    : FullStream("recovered content", ct);
            });

        var msgIdSeq = 0;
        var chatGroup = Silo.AddProbe<IChatGroupGrain>(_chatGroupId);
        var mentionHistory = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderType = "user", SenderId = Guid.NewGuid(), Content = $"Hey {PersonaName}, what do you think?", ChatGroupId = _chatGroupId }
        };
        chatGroup.Setup(g => g.GetNextMessageIdAsync(It.IsAny<Guid?>(), It.IsAny<string>())).ReturnsAsync(() => ++msgIdSeq);
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>())).ReturnsAsync(mentionHistory);
        chatGroup.Setup(g => g.GetParticipantsAsync()).ReturnsAsync(new List<PartyParticipant> { new() { Id = _personaId, Name = PersonaName } });
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(0);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.AppendMessageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationStoppedAsync(It.IsAny<int>(), It.IsAny<string?>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var personaRoot = Silo.AddProbe<IPersonaRootGrain>(Guid.Empty);
        personaRoot.Setup(g => g.GetAll()).ReturnsAsync(new Persona[] { new() { Id = _personaId, Name = PersonaName, SystemPrompt = "..." } });

        Silo.AddService<ILoggerFactory>(NullLoggerFactory.Instance);

        var router = Silo.AddProbe<ILlmRouterGrain>(0L);
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(midStreamMock.Object);

        var grain = await Silo.CreateGrainAsync<PersonaGrain>(_personaId);
        await grain.SetPersona(PersonaName, "...", null);

        // Act — MentionMessage() triggers auto-respond so the router is only called for
        // generation (not the decision LLM call), ensuring midStreamMock sees only
        // generation requests where our fail-then-succeed script applies correctly.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await grain.NotifyMessageAsync(_chatGroupId, MentionMessage(), cts.Token);

        // Assert: the retry stream starts from a fresh buffer — exact "recovered content",
        // not "partialrecovered content". Equality (not substring) catches buffer reuse regressions.
        chatGroup.Verify(
            g => g.AppendMessageAsync(
                It.IsAny<int>(),
                "recovered content",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<ChatMessageMetadata?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        chatGroup.Verify(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(2, callCount); // one failure + one success
    }

    // ── Streaming helpers ─────────────────────────────────────────────────────

    private static async IAsyncEnumerable<LlmGenerationEvent> HangUntilCancelled(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break; // unreachable but required for IAsyncEnumerable
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> MidStreamError(
        string partialChunk,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, partialChunk);
        await Task.Yield();
        throw new InvalidOperationException("mid-stream connection reset");
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> FullStream(
        string content,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, content);
    }
}
