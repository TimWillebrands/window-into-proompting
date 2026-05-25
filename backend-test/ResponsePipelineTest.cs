using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Streaming;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="ResponsePipeline"/> — the per-turn orchestration extracted
/// from <c>PersonaGrain</c>. Covers the eight scenarios from issue #63:
///
///   • Pre-gate obvious-skip → RecordSkippedTurnAsync, no slot, no LLM
///   • Self-fan-out → early return, no side effects
///   • Decision returns Respond=false → MarkGenerationStopped + PersonaDeclined event
///   • Decision + Speaking happy path → AppendMessageAsync with expected appraisal
///   • Speaking retry on transient failure → ResetGeneratedText + GenerationRetry event
///   • Race-cancel during Speaking → emote via MarkGenerationCancelledAsEmoteAsync
///   • External cancel (race-cancelled NOT set) → MarkGenerationFailedAsync("cancelled")
///   • Repair hint consumed once on decision pass, cleared regardless of decision
///
/// Strategy: pure unit tests. Real <see cref="InFlightStore"/>; Mock chat group, grain
/// factory, router, persona root, memory repo. Pattern follows <see cref="RaceTriggerTest"/>.
/// </summary>
public class ResponsePipelineTest
{
    private const string PersonaName = "Alice";
    private static readonly Guid PersonaId = Guid.NewGuid();

    private static Persona MakePersona(double chattiness = 0.5, double impulsivity = 0.5) =>
        new(PersonaId, PersonaName, $"You are {PersonaName}.", bio: null, chattiness, impulsivity);

    private static ChatMessage Msg(Guid senderId, string content, int messageId = 1, string senderType = "user") => new()
    {
        MessageId = messageId,
        SenderId = senderId,
        SenderType = senderType,
        Content = content,
    };

    /// <summary>
    /// Fixture bundling the pipeline + the mocks the test typically inspects.
    /// Mocks are loose so tests assert what they care about and don't drown in
    /// "no setup found" failures for unused paths.
    /// </summary>
    private sealed record Fixture(
        ResponsePipeline Pipeline,
        InFlightStore Store,
        Mock<IChatGroupGrain> ChatGroup,
        Mock<IGrainFactory> GrainFactory,
        Mock<ILlmRouterGrain> Router,
        Mock<IPersonaRootGrain> PersonaRoot,
        Mock<IMemoryRepository> MemoryRepo);

    private static Fixture BuildFixture()
    {
        var store = new InFlightStore();
        var partyId = Guid.NewGuid();

        var chatGroup = new Mock<IChatGroupGrain>();
        chatGroup.Setup(g => g.GetParticipantIdsAsync())
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { PersonaId });
        chatGroup.Setup(g => g.GetDriverOverridesAsync())
            .ReturnsAsync((IReadOnlyDictionary<Guid, DriverKind>)new Dictionary<Guid, DriverKind>());
        chatGroup.Setup(g => g.GetPartyIdAsync()).ReturnsAsync(partyId);
        chatGroup.Setup(g => g.GetScenarioAsync()).ReturnsAsync((string?)null);
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage>());
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(0);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.RecordSkippedTurnAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.GetNextMessageIdAsync(It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<int?>()))
            .ReturnsAsync(42);
        chatGroup.Setup(g => g.AppendMessageAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationStoppedAsync(
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.MarkGenerationCancelledAsEmoteAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        var router = new Mock<ILlmRouterGrain>();

        var personaRoot = new Mock<IPersonaRootGrain>();
        personaRoot.Setup(g => g.GetAll()).ReturnsAsync(new Persona[]
        {
            new() { Id = PersonaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null }
        });

        var partyGrain = new Mock<IPartyGrain>();
        partyGrain.Setup(g => g.GetCastAsync()).ReturnsAsync((IReadOnlyList<CastMember>)new List<CastMember>
        {
            CastMember.Create(
                new PartyParticipant { Id = PersonaId, Name = PersonaName, IsUser = false },
                new Persona { Id = PersonaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null })
        });

        var factory = new Mock<IGrainFactory>();
        factory.Setup(f => f.GetGrain<ILlmRouterGrain>(0L, null)).Returns(router.Object);
        factory.Setup(f => f.GetGrain<IPersonaRootGrain>(Guid.Empty, null)).Returns(personaRoot.Object);
        factory.Setup(f => f.GetGrain<IPartyGrain>(partyId, null)).Returns(partyGrain.Object);

        var memoryRepo = new Mock<IMemoryRepository>();
        memoryRepo.Setup(m => m.RecallRecentSnippetsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        var pipeline = new ResponsePipeline(
            factory.Object,
            memoryRepo.Object,
            NullLoggerFactory.Instance,
            NullLogger<ResponsePipeline>.Instance);

        return new Fixture(pipeline, store, chatGroup, factory, router, personaRoot, memoryRepo);
    }

    /// <summary>Router whose endpoint streams a single chunk of canned JSON/text.</summary>
    private static void WireRouter(Mock<ILlmRouterGrain> router, params string[] scriptedChunks)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
        endpoint.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(scriptedChunks, ct));
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
    }

    /// <summary>
    /// Wire the router so each successive call returns an endpoint streaming the next
    /// canned response. Use when the pipeline makes multiple LLM calls in a single
    /// HandleAsync (e.g. decision + speaking).
    /// </summary>
    private static void WireRouterSequence(Mock<ILlmRouterGrain> router, params string[] perCallContent)
    {
        var endpoints = perCallContent.Select(content =>
        {
            var ep = new Mock<ILlmEndpointGrain>();
            ep.Setup(e => e.GetAttributionAsync())
                .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
            ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
                .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(new[] { content }, ct));
            return ep.Object;
        }).ToList();

        var idx = 0;
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var next = Interlocked.Increment(ref idx) - 1;
                if (next >= endpoints.Count)
                    throw new Xunit.Sdk.XunitException(
                        $"Unexpected extra RouteAsync call (#{next + 1}) — scripted only {endpoints.Count}.");
                return endpoints[next];
            });
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> StreamChunks(
        IEnumerable<string> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, chunk);
        }
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> MidStreamFailure(
        string partialChunk,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, partialChunk);
        await Task.Yield();
        throw new InvalidOperationException("scripted mid-stream failure");
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> HangUntilCancelled(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }

    // ── 1. Pre-gate obvious-skip ──────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_PreGateObviousSkip_RecordsSkippedTurnAndSkipsLlm()
    {
        // Arrange: deep into a heated AI-vs-AI cascade — rounds=5, recentSelf=2.
        // No mention, no question → IsObviousSkip math drops below -0.4 deterministically.
        var fx = BuildFixture();
        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "carry on without me");

        // History with two trailing self-assistant messages so recentSelfCount=2.
        // Last message must not mention persona or end with '?' so urge stays low.
        var assistantHistory = new List<ChatMessage>
        {
            new() { MessageId = 10, SenderId = otherSenderId, SenderType = "user", Content = "hi all" },
            new() { MessageId = 11, SenderId = PersonaId,     SenderType = "assistant", Content = "yo" },
            new() { MessageId = 12, SenderId = PersonaId,     SenderType = "assistant", Content = "and another thing" },
        };
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(int.MaxValue))
            .ReturnsAsync(assistantHistory);
        fx.ChatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(5);

        // Act
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        // Assert: persona persisted the skip into the papertrail but reserved no slot
        // and made no LLM call.
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(
            PersonaId, PersonaName, triggering.MessageId,
            It.IsAny<double>(), It.IsAny<string>()), Times.Once);
        fx.ChatGroup.Verify(g => g.GetNextMessageIdAsync(
            It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
        fx.Router.Verify(r => r.RouteAsync(
            It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 2. Self-fan-out ───────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SelfFanout_NoSideEffects()
    {
        // Arrange: triggering message comes from the persona itself. Pipeline must
        // bail before touching the chat group — even reading history would be wasted
        // work, and any state mutation here risks Vlad ruminating on Vlad's last line.
        var fx = BuildFixture();
        var triggering = Msg(PersonaId, "I am the only voice in this room");

        // Act
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        // Assert: not even the read-only history fetch happens — defense-in-depth means
        // "do nothing observable", not "do nothing harmful".
        fx.ChatGroup.Verify(g => g.GetMessagesUntilAsync(It.IsAny<int>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<double>(), It.IsAny<string>()), Times.Never);
        fx.ChatGroup.Verify(g => g.GetNextMessageIdAsync(
            It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Never);
        fx.Router.Verify(r => r.RouteAsync(
            It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 3. Decision returns Respond=false ─────────────────────────────────────

    [Fact]
    public async Task HandleAsync_DecisionDeclines_MarksStoppedWithDeclinedAppraisal()
    {
        // Arrange: decision LLM returns respond=false with a gut reaction.
        // History: a single user msg that does NOT mention persona — urge stays low so
        // the LLM decision call fires (not the auto-respond shortcut).
        var fx = BuildFixture();
        var declineJson = """{"gutReaction":"not interested","memoryToReference":null,"wouldSay":"","respond":false}""";
        WireRouter(fx.Router, declineJson);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        string? capturedAppraisal = null;
        int? stoppedTriggeredBy = null;
        fx.ChatGroup.Setup(g => g.MarkGenerationStoppedAsync(
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Callback<int, string?, int?>((_, appraisal, triggeredBy) =>
            {
                capturedAppraisal = appraisal;
                stoppedTriggeredBy = triggeredBy;
            })
            .Returns(Task.CompletedTask);

        var declinedEvents = new List<MessageStreamEvent>();
        fx.ChatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Callback<int, MessageStreamEvent>((_, evt) => declinedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        // Act
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        // Assert: declined → no message body written, papertrail carries the appraisal
        // JSON in the shape PartyController.TryParseAppraisal expects.
        fx.ChatGroup.Verify(g => g.MarkGenerationStoppedAsync(
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        fx.ChatGroup.Verify(g => g.AppendMessageAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);

        Assert.Equal(triggering.MessageId, stoppedTriggeredBy);
        Assert.NotNull(capturedAppraisal);
        Assert.Contains("\"reason\"", capturedAppraisal);
        Assert.Contains("\"stop\":true", capturedAppraisal);
        Assert.Contains("not interested", capturedAppraisal);

        // PersonaDeclinedResponse event fired with done=true.
        Assert.Contains(declinedEvents, e => e.Event == MessageStreamEvent.PersonaDeclinedResponse && e.Done);
    }

    // ── 4. Decision + Speaking happy path ─────────────────────────────────────

    [Fact]
    public async Task HandleAsync_HappyPath_AppendsMessageWithAppraisal()
    {
        // Arrange: decision LLM says respond=true with a clear instruction; speaking
        // LLM streams the literal reply. AppendMessageAsync should be called with the
        // speaking content + the appraisal JSON shape (personaId/instruction/reason/stop).
        var fx = BuildFixture();
        var decisionJson = """{"gutReaction":"I have thoughts","memoryToReference":null,"wouldSay":"Bring it.","respond":true}""";
        var speakingText = "Bring it. Let's roll.";
        WireRouterSequence(fx.Router, decisionJson, speakingText);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        string? capturedContent = null;
        string? capturedAppraisal = null;
        int? capturedTriggeredBy = null;
        fx.ChatGroup.Setup(g => g.AppendMessageAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, string?, string?, ChatMessageMetadata?, int?, CancellationToken>(
                (_, content, _, appraisal, _, triggeredBy, _) =>
                {
                    capturedContent = content;
                    capturedAppraisal = appraisal;
                    capturedTriggeredBy = triggeredBy;
                })
            .Returns(Task.CompletedTask);

        // Act
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        // Assert: terminal write happened with expected appraisal shape and the speaking text.
        fx.ChatGroup.Verify(g => g.AppendMessageAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        fx.ChatGroup.Verify(g => g.MarkGenerationStoppedAsync(
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        fx.ChatGroup.Verify(g => g.MarkGenerationFailedAsync(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);

        Assert.Equal(speakingText, capturedContent);
        Assert.Equal(triggering.MessageId, capturedTriggeredBy);
        Assert.NotNull(capturedAppraisal);
        Assert.Contains("\"reason\":\"I have thoughts\"", capturedAppraisal);
        Assert.Contains("\"instruction\":\"Bring it.\"", capturedAppraisal);
        Assert.Contains("\"stop\":false", capturedAppraisal);
    }

    // ── 5. Speaking-phase retry on transient failure ──────────────────────────

    [Fact]
    public async Task HandleAsync_SpeakingFailsOnce_RetriesAndAccumulatesFromFreshStream()
    {
        // Arrange: decision succeeds; speaking endpoint streams "partial" then throws on
        // first attempt and emits "recovered content" cleanly on second. The retry must
        // restream from a fresh buffer (AppendMessageAsync content is exactly "recovered
        // content", not "partialrecovered content") and fire a GenerationRetry event.
        var fx = BuildFixture();
        var decisionJson = """{"gutReaction":"sure","memoryToReference":null,"wouldSay":"on it.","respond":true}""";

        // Decision endpoint → single canned JSON; speaking endpoint → fail-then-succeed.
        var decisionEp = new Mock<ILlmEndpointGrain>();
        decisionEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "decider" });
        decisionEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(new[] { decisionJson }, ct));

        var speakingCall = 0;
        var speakingEp = new Mock<ILlmEndpointGrain>();
        speakingEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "speaker" });
        speakingEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) =>
            {
                var n = Interlocked.Increment(ref speakingCall);
                return n == 1
                    ? MidStreamFailure("partial", ct)
                    : StreamChunks(new[] { "recovered content" }, ct);
            });

        var routeCall = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) == 1 ? decisionEp.Object : speakingEp.Object);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "kick things off");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        string? capturedContent = null;
        fx.ChatGroup.Setup(g => g.AppendMessageAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
                It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, string?, string?, ChatMessageMetadata?, int?, CancellationToken>(
                (_, content, _, _, _, _, _) => capturedContent = content)
            .Returns(Task.CompletedTask);

        var streamEvents = new List<MessageStreamEvent>();
        fx.ChatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Callback<int, MessageStreamEvent>((_, evt) => streamEvents.Add(evt))
            .Returns(Task.CompletedTask);

        // Act — generous timeout for the 2s retry delay.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: cts.Token);

        // Assert: retry happened, final content is from the fresh stream (not concatenated).
        Assert.Equal("recovered content", capturedContent);
        Assert.Contains(streamEvents, e => e.Event == MessageStreamEvent.GenerationRetry);
        Assert.Equal(2, speakingCall);
        fx.ChatGroup.Verify(g => g.MarkGenerationFailedAsync(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    // ── 6. Race-cancel during Speaking → emote ────────────────────────────────

    [Fact]
    public async Task HandleAsync_RaceCancelDuringSpeaking_GeneratesEmote()
    {
        // Arrange: decision succeeds; speaking endpoint blocks indefinitely. While the
        // pipeline is mid-stream, an external task (simulating RaceTrigger.EvaluateAsync
        // firing from a sibling NotifyMessageAsync) marks the in-flight as race-cancelled
        // and trips the CTS. The pipeline's OperationCanceledException catch must route
        // to the emote path: PersonaEmoteService is invoked, MarkGenerationCancelledAsEmoteAsync
        // is called, and the emote appraisal carries raceCancelled=true.
        var fx = BuildFixture();
        var decisionJson = """{"gutReaction":"jumping in","memoryToReference":null,"wouldSay":"hold on, I","respond":true}""";
        var emoteText = "*pulls back, listening instead*";
        var chatGroupId = Guid.NewGuid();
        var startedSpeaking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var decisionEp = new Mock<ILlmEndpointGrain>();
        decisionEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "decider" });
        decisionEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(new[] { decisionJson }, ct));

        var speakingEp = new Mock<ILlmEndpointGrain>();
        speakingEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "speaker" });
        speakingEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SignalThenHang(startedSpeaking, ct));

        var emoteEp = new Mock<ILlmEndpointGrain>();
        emoteEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "emoter" });
        emoteEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(new[] { emoteText }, ct));

        var routeCall = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) switch
            {
                1 => decisionEp.Object,
                2 => speakingEp.Object,
                _ => emoteEp.Object,
            });

        var triggering = Msg(Guid.NewGuid(), "kick things off");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        string? capturedEmote = null;
        string? capturedAppraisal = null;
        fx.ChatGroup.Setup(g => g.MarkGenerationCancelledAsEmoteAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()))
            .Callback<int, string, string?, int?>((_, emote, appraisal, _) =>
            {
                capturedEmote = emote;
                capturedAppraisal = appraisal;
            })
            .Returns(Task.CompletedTask);

        // Act: start the pipeline; once speaking has begun, simulate the race trigger
        // tripping inFlight + CTS from outside.
        var pipelineTask = fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId,
            triggering,
            fx.ChatGroup.Object,
            fx.Store,
            CancellationToken.None);

        await startedSpeaking.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var inFlightSnapshot = fx.Store.SnapshotForChatGroup(chatGroupId);
        Assert.Single(inFlightSnapshot);
        var (_, inFlightGen) = inFlightSnapshot[0];
        inFlightGen.MarkRaceCancelled("forget it — totally different topic", "Mira");
        inFlightGen.Cts.Cancel();

        await pipelineTask.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        fx.ChatGroup.Verify(g => g.MarkGenerationCancelledAsEmoteAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()),
            Times.Once);
        fx.ChatGroup.Verify(g => g.AppendMessageAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
        fx.ChatGroup.Verify(g => g.MarkGenerationFailedAsync(
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);

        Assert.Equal(emoteText, capturedEmote);
        Assert.NotNull(capturedAppraisal);
        Assert.Contains("\"raceCancelled\":true", capturedAppraisal);
    }

    // ── 7. External cancel (race-cancelled NOT set) ───────────────────────────

    [Fact]
    public async Task HandleAsync_ExternalCancelDuringSpeaking_MarksFailedWithCancelled()
    {
        // Arrange: decision succeeds; speaking hangs; the *caller's* CTS is tripped
        // (simulating PartyGrain.CancelGenerationAsync via PersonaGrain.CancelGenerationAsync).
        // RaceCancelled flag is NOT set → catch routes to MarkGenerationFailedAsync("cancelled"),
        // not the emote path.
        var fx = BuildFixture();
        var decisionJson = """{"gutReaction":"about to","memoryToReference":null,"wouldSay":"writing...","respond":true}""";

        var decisionEp = new Mock<ILlmEndpointGrain>();
        decisionEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "decider" });
        decisionEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(new[] { decisionJson }, ct));

        var speakingEp = new Mock<ILlmEndpointGrain>();
        speakingEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "speaker" });
        speakingEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => HangUntilCancelled(ct));

        var routeCall = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) == 1 ? decisionEp.Object : speakingEp.Object);

        var triggering = Msg(Guid.NewGuid(), "kick things off");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        string? capturedError = null;
        fx.ChatGroup.Setup(g => g.MarkGenerationFailedAsync(It.IsAny<int>(), It.IsAny<string>()))
            .Callback<int, string>((_, error) => capturedError = error)
            .Returns(Task.CompletedTask);

        // Act: cancel 100ms into the hanging speaking phase.
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: cts.Token);

        // Assert: failure surfaced as "cancelled", emote path not taken.
        Assert.Equal("cancelled", capturedError);
        fx.ChatGroup.Verify(g => g.MarkGenerationCancelledAsEmoteAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        fx.ChatGroup.Verify(g => g.AppendMessageAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<string?>(), It.IsAny<ChatMessageMetadata?>(),
            It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── 8. Repair hint consumed once on decision pass ─────────────────────────

    [Fact]
    public async Task HandleAsync_RepairHintPresent_ConsumedAndClearedAfterDecisionPass()
    {
        // Arrange: pre-seed a repair hint into the store for this chat group.
        // Run a decline-path pipeline (cheap, exercises decision phase end-to-end).
        // After: the hint must no longer be in the store regardless of decision outcome.
        var fx = BuildFixture();
        var chatGroupId = Guid.NewGuid();
        var hint = new RepairHint(MissedMessageId: 42, MissedSenderName: "Mira", MissedContent: "you missed this");
        fx.Store.SetRepairHint(chatGroupId, hint);

        var declineJson = """{"gutReaction":"meh","memoryToReference":null,"wouldSay":"","respond":false}""";
        WireRouter(fx.Router, declineJson);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "anyone home?");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        // Sanity-check the hint is there before HandleAsync — using ConsumeRepairHint
        // would clear it, so use a peek-by-setting-it-back pattern.
        // (InFlightStore has no peek; assert by re-seeding immediately if the pre-condition matters.)
        // Instead: trust SetRepairHint and assert post-condition only.

        // Act
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId,
            triggering,
            fx.ChatGroup.Object,
            fx.Store,
            CancellationToken.None);

        // Assert: pipeline ran decline path, AND the hint has been consumed (store no longer holds it).
        fx.ChatGroup.Verify(g => g.MarkGenerationStoppedAsync(
            It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        Assert.Null(fx.Store.ConsumeRepairHint(chatGroupId));
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> SignalThenHang(
        TaskCompletionSource<bool> signal,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "hold on, I");
        signal.TrySetResult(true);
        await Task.Delay(Timeout.Infinite, ct);
        yield break;
    }
}
