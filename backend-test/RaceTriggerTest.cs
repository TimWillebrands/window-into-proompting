using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="RaceTrigger"/> — the stop-signal race extracted from
/// <c>PersonaGrain</c>. Covers the five race outcomes from the issue:
///
///   • cancel-decision   (Decision phase → always cancel)
///   • past-pnr          (Speaking phase, tokens ≥ PnrTokens → repair hint, no cancel)
///   • cancel-generation (Speaking pre-PNR, cancelScore &gt; 0.5 → cancel + race-cancelled flag)
///   • continue          (Speaking pre-PNR, cancelScore ≤ 0.5 → repair hint, no cancel)
///   • let-it-ride       (salience throws → behaves like SalienceScore.LetItRide → no cancel)
///
/// Strategy: real <see cref="InFlightStore"/>, mock <see cref="IChatGroupGrain"/> probe
/// for participants + papertrail capture, mock <see cref="IGrainFactory"/> + mock
/// <see cref="ILlmRouterGrain"/> for the salience-service path (only exercised by the
/// pre-PNR outcomes).
/// </summary>
public class RaceTriggerTest
{
    private const string PersonaName = "Vlad";
    private static readonly Guid PersonaId = Guid.NewGuid();

    // PNR threshold lives on RaceTrigger as a private const (80 tokens, ~4 chars/token).
    // Generate enough chars to push past it deterministically (90 tokens worth = 360 chars).
    private const string PastPnrText =
        "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
        "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
        "0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789" +
        "012345678901234567890123456789012345678901234567890123456789";

    private static Persona MakePersona(double impulsivity = 0.5) => new()
    {
        Id = PersonaId,
        Name = PersonaName,
        SystemPrompt = "You are Vlad.",
        Bio = "Reluctant philosopher",
        Chattiness = 0.5,
        Impulsivity = impulsivity,
    };

    private static ChatMessage NewTrigger(Guid senderId, string content = "hey wait") => new()
    {
        MessageId = 99,
        SenderId = senderId,
        SenderType = "user",
        Content = content,
    };

    private static readonly Guid PartyId = Guid.NewGuid();

    /// <summary>
    /// Build a mocked <see cref="IChatGroupGrain"/> that:
    ///   • returns the test PartyId from GetPartyIdAsync (so the sender-name resolution path
    ///     routes through PartyGrain.GetCastAsync)
    ///   • records every RecordRaceEvaluationAsync call into the provided outcomes list
    /// </summary>
    private static Mock<IChatGroupGrain> MakeChatGroup(
        List<(string outcome, double? salience, double? cancelScore)> outcomes)
    {
        var mock = new Mock<IChatGroupGrain>();
        mock.Setup(g => g.GetPartyIdAsync()).ReturnsAsync(PartyId);
        mock.Setup(g => g.RecordRaceEvaluationAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<string>(), It.IsAny<double?>(), It.IsAny<double?>()))
            .Returns<Guid, string, int, string, double?, double?>(
                (_, _, _, outcome, sal, cs) =>
                {
                    outcomes.Add((outcome, sal, cs));
                    return Task.CompletedTask;
                });
        return mock;
    }

    /// <summary>
    /// Build a fake grain factory whose <c>GetGrain&lt;ILlmRouterGrain&gt;(0, null)</c> returns
    /// the supplied router mock and whose <c>GetGrain&lt;IPartyGrain&gt;(PartyId, null)</c>
    /// returns a party grain mock with the test cast (used for sender-name resolution).
    /// </summary>
    private static Mock<IGrainFactory> MakeGrainFactory(Mock<ILlmRouterGrain> router, Guid otherParticipantId, string otherParticipantName)
    {
        var partyGrain = new Mock<IPartyGrain>();
        partyGrain.Setup(g => g.GetCastAsync()).ReturnsAsync((IReadOnlyList<CastMember>)new List<CastMember>
        {
            CastMember.Create(
                new PartyParticipant { Id = PersonaId, Name = PersonaName, Driver = DriverKind.LLM },
                new Persona { Id = PersonaId, Name = PersonaName, SystemPrompt = "You are Vlad.", Bio = "Reluctant philosopher" }),
            CastMember.Create(
                new PartyParticipant { Id = otherParticipantId, Name = otherParticipantName, Driver = DriverKind.User },
                persona: null),
        });
        var factory = new Mock<IGrainFactory>();
        factory.Setup(f => f.GetGrain<ILlmRouterGrain>(0L, null)).Returns(router.Object);
        factory.Setup(f => f.GetGrain<IPartyGrain>(PartyId, null)).Returns(partyGrain.Object);
        return factory;
    }

    /// <summary>Router that responds to RouteAsync with an endpoint streaming the given JSON salience response.</summary>
    private static Mock<ILlmRouterGrain> MakeRouterWithSalience(string salienceJson)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SingleChunk(salienceJson, ct));
        endpoint.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "lfm2-test" });

        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
        return router;
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> SingleChunk(
        string data,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, data);
    }

    private static RaceTrigger MakeTrigger(Mock<IGrainFactory> factory) =>
        new(factory.Object, NullLoggerFactory.Instance);

    // ── outcomes ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_DecisionPhase_AlwaysCancels()
    {
        // Arrange: in-flight gen still in Decision phase. No salience call expected —
        // the race shortcircuits to cancel.
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var router = MakeRouterWithSalience("""{"salience":0.0,"kind":"irrelevant"}""");
        var factory = MakeGrainFactory(router, senderId, "Mira");
        var trigger = MakeTrigger(factory);

        // Act
        await trigger.EvaluateAsync(
            MakePersona(), chatGroupId, NewTrigger(senderId), chatGroup.Object, store,
            CancellationToken.None);

        // Assert: gen cancelled, race-cancelled flag set, outcome recorded
        var snap = gen.Snapshot();
        Assert.True(snap.RaceCancelled);
        Assert.True(cts.IsCancellationRequested);
        Assert.Single(outcomes);
        Assert.Equal("cancel-decision", outcomes[0].Item1);
        // Salience never consulted on decision-phase cancel.
        router.Verify(
            r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_SpeakingPastPnr_SetsRepairHintAndSkipsCancel()
    {
        // Arrange: in-flight gen in Speaking phase with enough chars to push past PNR (80 tokens, ~320 chars).
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("gut", "would say");
        gen.AppendChunk(PastPnrText); // 360+ chars → 90+ tokens → past PnrTokens=80

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var router = MakeRouterWithSalience("""{"salience":1.0,"kind":"contradict"}""");
        var factory = MakeGrainFactory(router, senderId, "Mira");
        var trigger = MakeTrigger(factory);

        // Act
        await trigger.EvaluateAsync(
            MakePersona(), chatGroupId, NewTrigger(senderId, "forget that"), chatGroup.Object, store,
            CancellationToken.None);

        // Assert: no cancellation, repair hint stashed for next decision pass, salience never burned
        Assert.False(gen.Snapshot().RaceCancelled);
        Assert.False(cts.IsCancellationRequested);
        var hint = store.ConsumeRepairHint(chatGroupId);
        Assert.True(hint.HasValue);
        Assert.Equal(99, hint!.Value.MissedMessageId);
        Assert.Single(outcomes);
        Assert.Equal("past-pnr", outcomes[0].Item1);
        router.Verify(
            r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_SpeakingPrePnr_HighSalience_CancelsGeneration()
    {
        // Arrange: Speaking phase, no tokens yet (commitmentProgress=0), impulsivity=0.
        // cancelScore = salience * (1 - 0) * (1 - 0) = salience. High salience → cancel.
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("planning to defend it", "I think it's actually good");

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var router = MakeRouterWithSalience("""{"salience":0.95,"kind":"contradict"}""");
        var factory = MakeGrainFactory(router, senderId, "Mira");
        var trigger = MakeTrigger(factory);

        // Act
        await trigger.EvaluateAsync(
            MakePersona(impulsivity: 0.0), chatGroupId, NewTrigger(senderId, "no it's broken"),
            chatGroup.Object, store, CancellationToken.None);

        // Assert: race elected to cancel; race-cancelled flag set so the catch in PersonaGrain
        // routes to the emote path instead of marking failed.
        var snap = gen.Snapshot();
        Assert.True(snap.RaceCancelled);
        Assert.True(cts.IsCancellationRequested);
        Assert.Single(outcomes);
        Assert.Equal("cancel-generation", outcomes[0].Item1);
        Assert.NotNull(outcomes[0].Item2);
        Assert.True(outcomes[0].Item3 > 0.5);
    }

    [Fact]
    public async Task EvaluateAsync_SpeakingPrePnr_LowSalience_ContinuesAndSetsRepairHint()
    {
        // Arrange: low salience → cancelScore stays under threshold → continue branch.
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("gut", "would say");

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var router = MakeRouterWithSalience("""{"salience":0.1,"kind":"tangent"}""");
        var factory = MakeGrainFactory(router, senderId, "Mira");
        var trigger = MakeTrigger(factory);

        // Act
        await trigger.EvaluateAsync(
            MakePersona(impulsivity: 0.5), chatGroupId, NewTrigger(senderId, "btw lunch"),
            chatGroup.Object, store, CancellationToken.None);

        // Assert: no cancel, repair hint set for next decision pass, outcome "continue"
        Assert.False(gen.Snapshot().RaceCancelled);
        Assert.False(cts.IsCancellationRequested);
        var hint = store.ConsumeRepairHint(chatGroupId);
        Assert.True(hint.HasValue);
        Assert.Equal(99, hint!.Value.MissedMessageId);
        Assert.Single(outcomes);
        Assert.Equal("continue", outcomes[0].Item1);
    }

    [Fact]
    public async Task EvaluateAsync_SpeakingPrePnr_SalienceThrows_LetsItRide()
    {
        // Arrange: salience routing throws → service returns SalienceScore.LetItRide internally,
        // cancelScore math collapses to 0, never crosses the threshold. The in-flight gen
        // survives untouched — this is the conservative fallback that preserves current
        // behavior when the cheap salience model is unavailable.
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("gut", "would say");

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);

        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no CharacterThoughts provider"));
        var factory = MakeGrainFactory(router, senderId, "Mira");
        var trigger = MakeTrigger(factory);

        // Act
        await trigger.EvaluateAsync(
            MakePersona(impulsivity: 0.0), chatGroupId, NewTrigger(senderId),
            chatGroup.Object, store, CancellationToken.None);

        // Assert: in-flight survives. RaceCancelled stays false, CTS not tripped.
        // Behavior matches SalienceScore.LetItRide (cancelScore=0 → continue branch).
        Assert.False(gen.Snapshot().RaceCancelled);
        Assert.False(cts.IsCancellationRequested);
        // Outcome papertrail records "continue" with salience=0 — the LetItRide value.
        Assert.Single(outcomes);
        Assert.Equal("continue", outcomes[0].Item1);
        Assert.Equal(0.0, outcomes[0].Item2);
    }
}
