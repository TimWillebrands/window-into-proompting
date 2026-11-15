using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.SystemOne;

namespace BackendTest;

public class RaceTriggerTest
{
    private const string PersonaName = "Vlad";
    private static readonly Guid PersonaId = Guid.NewGuid();

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

    private static Mock<IChatGroupGrain> MakeChatGroup(
        List<(string outcome, double? salience, double? cancelScore)> outcomes)
    {
        var mock = new Mock<IChatGroupGrain>();
        mock.Setup(g => g.GetPartyIdAsync()).ReturnsAsync(PartyId);
        mock.Setup(g => g.GetCastAsync()).ReturnsAsync(new RoomCast());
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

    private static Mock<IGrainFactory> MakeGrainFactory(Guid otherParticipantId, string otherParticipantName)
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
        factory.Setup(f => f.GetGrain<IPartyGrain>(PartyId, null)).Returns(partyGrain.Object);
        return factory;
    }

    private static Mock<ISystemOneJudge> MakeJudge(double salience, string kind)
    {
        var answers = new SystemOneAnswers();
        answers.Nouls["interrupt"] = salience;
        answers.Choices["kind"] = new Dictionary<string, double> { [kind] = 1.0 };
        var judge = new Mock<ISystemOneJudge>();
        judge.Setup(j => j.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, SystemOneQuestion>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answers);
        return judge;
    }

    private static RaceTrigger MakeTrigger(Mock<IGrainFactory> factory, Mock<ISystemOneJudge> judge) =>
        new(factory.Object, NullLoggerFactory.Instance, judge.Object);

    [Fact]
    public async Task EvaluateAsync_DecisionPhase_AlwaysCancels()
    {
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var judge = MakeJudge(0.0, "irrelevant");
        var factory = MakeGrainFactory(senderId, "Mira");
        var trigger = MakeTrigger(factory, judge);

        await trigger.EvaluateAsync(
            MakePersona(), chatGroupId, NewTrigger(senderId), chatGroup.Object, store,
            CancellationToken.None);

        var snap = gen.Snapshot();
        Assert.True(snap.RaceCancelled);
        Assert.True(cts.IsCancellationRequested);
        Assert.Single(outcomes);
        Assert.Equal("cancel-decision", outcomes[0].Item1);
        judge.Verify(
            j => j.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, SystemOneQuestion>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_SpeakingPastPnr_SetsRepairHintAndSkipsCancel()
    {
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("gut", "would say");
        gen.AppendChunk(PastPnrText);

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var judge = MakeJudge(1.0, "contradict");
        var factory = MakeGrainFactory(senderId, "Mira");
        var trigger = MakeTrigger(factory, judge);

        await trigger.EvaluateAsync(
            MakePersona(), chatGroupId, NewTrigger(senderId, "forget that"), chatGroup.Object, store,
            CancellationToken.None);

        Assert.False(gen.Snapshot().RaceCancelled);
        Assert.False(cts.IsCancellationRequested);
        var hint = store.ConsumeRepairHint(chatGroupId);
        Assert.True(hint.HasValue);
        Assert.Equal(99, hint!.Value.MissedMessageId);
        Assert.Single(outcomes);
        Assert.Equal("past-pnr", outcomes[0].Item1);
        judge.Verify(
            j => j.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, SystemOneQuestion>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateAsync_SpeakingPrePnr_HighSalience_CancelsGeneration()
    {
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("planning to defend it", "I think it's actually good");

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var judge = MakeJudge(0.95, "contradict");
        var factory = MakeGrainFactory(senderId, "Mira");
        var trigger = MakeTrigger(factory, judge);

        await trigger.EvaluateAsync(
            MakePersona(impulsivity: 0.0), chatGroupId, NewTrigger(senderId, "no it's broken"),
            chatGroup.Object, store, CancellationToken.None);

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
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("gut", "would say");

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);
        var judge = MakeJudge(0.1, "tangent");
        var factory = MakeGrainFactory(senderId, "Mira");
        var trigger = MakeTrigger(factory, judge);

        await trigger.EvaluateAsync(
            MakePersona(impulsivity: 0.5), chatGroupId, NewTrigger(senderId, "btw lunch"),
            chatGroup.Object, store, CancellationToken.None);

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
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var cts = new CancellationTokenSource();
        var gen = store.Register(chatGroupId, messageId: 7, cts);
        gen.MarkGenerationStarted("gut", "would say");

        var senderId = Guid.NewGuid();
        var outcomes = new List<(string, double?, double?)>();
        var chatGroup = MakeChatGroup(outcomes);

        var judge = new Mock<ISystemOneJudge>();
        judge.Setup(j => j.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, SystemOneQuestion>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("judge down"));
        var factory = MakeGrainFactory(senderId, "Mira");
        var trigger = MakeTrigger(factory, judge);

        await trigger.EvaluateAsync(
            MakePersona(impulsivity: 0.0), chatGroupId, NewTrigger(senderId),
            chatGroup.Object, store, CancellationToken.None);

        Assert.False(gen.Snapshot().RaceCancelled);
        Assert.False(cts.IsCancellationRequested);
        Assert.Single(outcomes);
        Assert.Equal("continue", outcomes[0].Item1);
        Assert.Equal(0.0, outcomes[0].Item2);
    }
}
