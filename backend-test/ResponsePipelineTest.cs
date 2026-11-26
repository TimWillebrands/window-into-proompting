using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Streaming;
using PartyTown.Services.SystemOne;

namespace BackendTest;

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

    private static readonly Guid HumanId = Guid.NewGuid();
    private const string HumanName = "Sam";

    private sealed record Fixture(
        ResponsePipeline Pipeline,
        InFlightStore Store,
        Mock<IChatGroupGrain> ChatGroup,
        Mock<IGrainFactory> GrainFactory,
        Mock<ILlmRouterGrain> Router,
        Mock<IPersonaRootGrain> PersonaRoot,
        Mock<IMemoryRepository> MemoryRepo,
        List<TurnOutcome> Outcomes,
        List<MessageStreamEvent> Events,
        Mock<PartyTown.World.Reflex.IPersonaReflexGrain>? Reflex);

    private static Fixture BuildFixture(
        PartyTown.World.Reflex.ReflexVerdict? verdict = null,
        ISystemOneJudge? judge = null,
        string kind = ChatGroupKinds.Group,
        bool windingDown = false,
        IReadOnlyList<RecalledMemory>? memories = null,
        bool asleep = false,
        Guid? also = null)
    {
        var store = new InFlightStore();
        var partyId = Guid.NewGuid();

        var chatGroup = new Mock<IChatGroupGrain>();
        chatGroup.Setup(g => g.GetParticipantIdsAsync())
            .ReturnsAsync((IReadOnlySet<Guid>)new HashSet<Guid> { PersonaId });
        chatGroup.Setup(g => g.GetPartyIdAsync()).ReturnsAsync(partyId);
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage>());
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(0);
        var events = new List<MessageStreamEvent>();
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Callback<int, MessageStreamEvent>((_, evt) => { lock (events) events.Add(evt); })
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.RecordSkippedTurnAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
                It.IsAny<double>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()))
            .ReturnsAsync(42);
        var outcomes = new List<TurnOutcome>();
        chatGroup.Setup(g => g.EndTurnAsync(It.IsAny<int>(), It.IsAny<TurnOutcome>()))
            .Callback<int, TurnOutcome>((_, o) => outcomes.Add(o))
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
                new PartyParticipant { Id = PersonaId, Name = PersonaName, Driver = DriverKind.LLM },
                new Persona { Id = PersonaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null }),
            CastMember.Create(new PartyParticipant { Id = HumanId, Name = HumanName, Driver = DriverKind.User }, null),
            CastMember.Create(
                new PartyParticipant { Id = also ?? Guid.Empty, Name = "Bob", Driver = DriverKind.LLM },
                new Persona { Id = also ?? Guid.Empty, Name = "Bob", SystemPrompt = "You are Bob.", Bio = null }),
        });

        var members = new HashSet<Guid> { PersonaId, HumanId };
        if (also is { } other) members.Add(other);
        chatGroup.Setup(g => g.GetCastAsync()).ReturnsAsync(() => RoomCast.Create(
            partyGrain.Object.GetCastAsync().Result,
            members,
            kind));

        var factory = new Mock<IGrainFactory>();
        factory.Setup(f => f.GetGrain<ILlmRouterGrain>(0L, null)).Returns(router.Object);
        factory.Setup(f => f.GetGrain<IPersonaRootGrain>(Guid.Empty, null)).Returns(personaRoot.Object);
        factory.Setup(f => f.GetGrain<IPartyGrain>(partyId, null)).Returns(partyGrain.Object);

        var thermostat = new Mock<PartyTown.World.Self.IConversationThermostatGrain>();
        thermostat.Setup(t => t.GetCueAsync()).ReturnsAsync(windingDown ? PartyTown.World.Self.Thermostat.Cue(0, 0, 0.9) : null);
        thermostat.Setup(t => t.PeekAsync()).ReturnsAsync(windingDown
            ? new PartyTown.World.Self.ThermostatRead(0, 0, 0.9, 0, true)
            : PartyTown.World.Self.ThermostatRead.None);
        thermostat.Setup(t => t.IsWindingDownAsync()).ReturnsAsync(windingDown);
        factory.Setup(f => f.GetGrain<PartyTown.World.Self.IConversationThermostatGrain>(It.IsAny<Guid>(), null)).Returns(thermostat.Object);

        var places = new Mock<PartyTown.World.Places.IPlacesGrain>();
        places.Setup(p => p.IsAsleepAsync(It.IsAny<Guid>())).ReturnsAsync(asleep);
        factory.Setup(f => f.GetGrain<PartyTown.World.Places.IPlacesGrain>(partyId, null)).Returns(places.Object);

        var memoryRepo = new Mock<IMemoryRepository>();
        memoryRepo.Setup(m => m.RecallAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ReturnsAsync(memories ?? Array.Empty<RecalledMemory>());
        memoryRepo.Setup(m => m.RecallStancesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StanceLine>());

        PartyTown.World.Reflex.ReflexGate? reflex = null;
        Mock<PartyTown.World.Reflex.IPersonaReflexGrain>? reflexGrain = null;
        if (verdict is not null)
        {
            reflexGrain = new Mock<PartyTown.World.Reflex.IPersonaReflexGrain>();
            reflexGrain.Setup(g => g.PerceiveAsync(It.IsAny<PartyTown.World.Reflex.Percept>())).ReturnsAsync(verdict);
            reflexGrain.Setup(g => g.GetMindAsync()).ReturnsAsync(string.Empty);
            factory.Setup(f => f.GetGrain<PartyTown.World.Reflex.IPersonaReflexGrain>(It.IsAny<string>(), null)).Returns(reflexGrain.Object);
            reflex = new PartyTown.World.Reflex.ReflexGate(factory.Object, NullLogger<PartyTown.World.Reflex.ReflexGate>.Instance);
        }

        var pipeline = new ResponsePipeline(
            factory.Object,
            memoryRepo.Object,
            NullLoggerFactory.Instance,
            NullLogger<ResponsePipeline>.Instance,
            judge ?? new BackendTest.Infrastructure.MenuJudge(() => null),
            reflex: reflex,
            backoff: SpeakingBackoff.None);

        return new Fixture(pipeline, store, chatGroup, factory, router, personaRoot, memoryRepo, outcomes, events, reflexGrain);
    }

    private static void WireRouter(Mock<ILlmRouterGrain> router, params string[] scriptedChunks)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
        endpoint.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(scriptedChunks, ct));
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
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

    private static List<LlmGenerationJob> WireCapturing(Mock<ILlmRouterGrain> router, params string[] perCallContent)
    {
        var jobs = new List<LlmGenerationJob>();
        var call = 0;
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var content = perCallContent[Math.Min(Interlocked.Increment(ref call) - 1, perCallContent.Length - 1)];
                var ep = new Mock<ILlmEndpointGrain>();
                ep.Setup(e => e.GetAttributionAsync()).ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
                ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
                    .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
                    {
                        lock (jobs) jobs.Add(job);
                        return StreamChunks(new[] { content }, ct);
                    });
                return ep.Object;
            });
        return jobs;
    }

    private static string Guidance(LlmGenerationJob speaking) => speaking.Messages.Last(m => m.Role == "system").Content ?? string.Empty;

    private static readonly string[] OldCues =
    {
        UtteranceRhythm.Cue(UtteranceRhythm.Length.Short),
        UtteranceRhythm.Cue(UtteranceRhythm.Length.Medium),
        UtteranceRhythm.Cue(UtteranceRhythm.Length.Long),
    };

    private static Task Handle(Fixture fx, ChatMessage trigger, Guid? room = null, CancellationToken ct = default)
        => fx.Pipeline.HandleAsync(MakePersona(), room ?? Guid.NewGuid(), trigger, fx.ChatGroup.Object, fx.Store, ct);

    private static void Hears(Fixture fx, params ChatMessage[] history)
        => fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(history.ToList());

    private static string Declared(Fixture fx) => Assert.IsType<TurnOutcome.Declined>(Assert.Single(fx.Outcomes)).Appraisal ?? string.Empty;

    private static RecalledMemory Memory(string snippet, double salience = 0.5, RecallArm arm = RecallArm.Recent, int recallCount = 0)
        => new(Guid.NewGuid(), snippet, 0.6, recallCount, null, DateTimeOffset.UtcNow.AddDays(-3), salience, arm);

    private sealed class RoomJudge(double? gravity = null, string? memory = null, bool fails = false, Task? holds = null) : ISystemOneJudge
    {
        public List<(string State, IReadOnlyDictionary<string, SystemOneQuestion> Questions)> Asked { get; } = new();

        public async Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
        {
            lock (Asked) Asked.Add((state, questions));
            if (holds is not null) await holds.WaitAsync(ct);
            if (fails) throw new HttpRequestException("judge down");
            var answers = new SystemOneAnswers();
            if (gravity is { } g) answers.Nouls[Gravity.Key] = g;
            if (memory is not null && questions.TryGetValue(Gravity.RecallKey, out var q) && q is ChoiceQuestion recall)
                answers.Choices[Gravity.RecallKey] = recall.Options.Keys.ToDictionary(k => k, k => k == memory ? 1.0 : 0.0);
            return answers;
        }
    }

    [Fact]
    public async Task HandleAsync_PreGateObviousSkip_RecordsSkippedTurnAndSkipsLlm()
    {
        var fx = BuildFixture();
        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "carry on without me", messageId: 13);
        Hears(fx,
            new ChatMessage { MessageId = 10, SenderId = otherSenderId, SenderType = "user", Content = "hi all" },
            new ChatMessage { MessageId = 11, SenderId = PersonaId, SenderType = "assistant", Content = "yo" },
            new ChatMessage { MessageId = 12, SenderId = PersonaId, SenderType = "assistant", Content = "and another thing" },
            triggering);
        fx.ChatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(5);

        await Handle(fx, triggering);

        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(
            PersonaId, PersonaName, triggering.MessageId,
            It.IsAny<double>(), It.IsAny<string>()), Times.Once);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.Router.Verify(r => r.RouteAsync(
            It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_SelfFanout_NoSideEffects()
    {
        var fx = BuildFixture();
        var triggering = Msg(PersonaId, "I am the only voice in this room");

        await Handle(fx, triggering);

        fx.ChatGroup.Verify(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<double>(), It.IsAny<string>()), Times.Never);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.Router.Verify(r => r.RouteAsync(
            It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_UrgeIsReadOffTheirLine_NotAnOpenSlotAfterIt()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("listen"));
        var triggering = Msg(HumanId, $"{PersonaName}, coffee later?", messageId: 1);
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(int.MaxValue, It.IsAny<Guid?>())).ReturnsAsync(new List<ChatMessage>
        {
            triggering,
            new() { MessageId = 2, SenderId = Guid.NewGuid(), SenderType = "assistant", Content = null },
        });

        await Handle(fx, triggering);

        fx.Reflex!.Verify(r => r.PerceiveAsync(It.Is<PartyTown.World.Reflex.Percept>(p => p.Named && p.FromHuman)), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TheVoicePasses_TheTurnIsDeclinedPlainly_AndTheMarkNeverReachesTheRoom()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, "(Pa", "ss).");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var appraisal = Declared(fx);
        Assert.Contains($"\"reason\":\"{Passing.Reason}\"", appraisal);
        Assert.Contains("\"passed\":true", appraisal);
        Assert.Contains("\"instruction\":null", appraisal);
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.PersonaDeclinedResponse && e.Done && e.Data.Contains(Passing.Reason));
        Assert.DoesNotContain(fx.Events, e => e.Event == MessageStreamEvent.ContentChunk);
    }

    [Fact]
    public async Task HandleAsync_ALineStartingLikeAPass_IsStreamedOnceItIsnt()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, "(pa", "uses) ok fine");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        Assert.Equal("(pauses) ok fine", string.Concat(fx.Events.Where(e => e.Event == MessageStreamEvent.ContentChunk).Select(e => e.Data)));
    }

    [Fact]
    public async Task HandleAsync_ReflexTakesIt_NoSlotNoLlm_ReasonInTheThoughtLog()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("putting it off"));
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.Router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(PersonaId, PersonaName, triggering.MessageId, It.IsAny<double>(), "reflex: putting it off"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_AHumansLineInAQuietGroup_GoesThroughReflexLikeAnyOther()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("listen"));
        var triggering = Msg(HumanId, "hiya");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        fx.Reflex!.Verify(r => r.PerceiveAsync(It.Is<PartyTown.World.Reflex.Percept>(p => p.FromHuman && !p.Named)), Times.Once);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(PersonaId, PersonaName, triggering.MessageId, It.IsAny<double>(), "reflex: listen"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ReflexHearsTheRoomAndTheThermostat()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("listen"), windingDown: true);
        var mira = Guid.NewGuid();
        var bo = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var triggering = new ChatMessage { MessageId = 4, SenderId = bo, SenderType = "assistant", Content = "ha, right", SendAt = now };
        Hears(fx,
            new ChatMessage { MessageId = 1, SenderId = HumanId, SenderType = "user", Content = "morning", SendAt = now - 60_000 },
            new ChatMessage { MessageId = 2, SenderId = mira, SenderType = "assistant", Content = "morning!", SendAt = now - 40_000 },
            new ChatMessage { MessageId = 3, SenderId = bo, SenderType = "assistant", Content = "hey", SendAt = now - 20_000 },
            triggering);

        await Handle(fx, triggering);

        fx.Reflex!.Verify(r => r.PerceiveAsync(It.Is<PartyTown.World.Reflex.Percept>(p =>
            p.Traded == 3 && p.Recent == 4 && p.Own == 0 && p.WindingDown && p.Thermostat != null && p.Thermostat.Contains("winding down"))), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ALineThatDidntNameThem_MayBeLetGo()
    {
        var fx = BuildFixture();
        var jobs = WireCapturing(fx.Router, "sure");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Contains(Passing.Cue, Guidance(jobs.Single()));
    }

    [Fact]
    public async Task HandleAsync_APersonaNamesThem_NoPassOffered()
    {
        var fx = BuildFixture();
        var jobs = WireCapturing(fx.Router, "sure");
        var triggering = Msg(Guid.NewGuid(), $"{PersonaName} what do you reckon", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.DoesNotContain(Passing.Cue, Guidance(jobs.Single()));
    }

    [Fact]
    public async Task HandleAsync_ReflexTorn_ANamedLineMayBeLetGo()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(0, deliberate: true, "now"));
        var jobs = WireCapturing(fx.Router, "(pass)");
        var triggering = Msg(Guid.NewGuid(), $"{PersonaName} what do you reckon", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Contains(Passing.Cue, Guidance(jobs.Single()));
        Assert.Contains("\"passed\":true", Declared(fx));
    }

    [Fact]
    public async Task HandleAsync_WindingDown_ANamedLineMayBeLetGo()
    {
        var fx = BuildFixture(windingDown: true);
        var jobs = WireCapturing(fx.Router, "sure");
        var triggering = Msg(Guid.NewGuid(), $"{PersonaName}, see you saturday then?", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Contains(Passing.Cue, Guidance(jobs.Single()));
    }

    [Fact]
    public async Task HandleAsync_AHumanNamesThem_NoPass_EvenTornAndWindingDown()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(0, deliberate: true, "now"), windingDown: true);
        var jobs = WireCapturing(fx.Router, "sure");
        var triggering = Msg(HumanId, $"{PersonaName} what do you reckon");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.DoesNotContain(Passing.Cue, Guidance(jobs.Single()));
    }

    [Fact]
    public async Task HandleAsync_AHumansDm_NoPass_EvenWindingDown()
    {
        var fx = BuildFixture(kind: ChatGroupKinds.Direct, windingDown: true);
        var jobs = WireCapturing(fx.Router, "sure");
        var triggering = Msg(HumanId, "so, saturday?");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.DoesNotContain(Passing.Cue, Guidance(jobs.Single()));
    }

    [Fact]
    public async Task HandleAsync_NewerLineWhileReading_OnlyTheNewerOneReplies()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(5_000, deliberate: false, "now"));
        var room = Guid.NewGuid();
        var first = Msg(Guid.NewGuid(), "anyone up for chess", messageId: 1, senderType: "assistant");
        Hears(fx, first);

        var reading = Handle(fx, first, room);
        await fx.Store.ReadAsync(room, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await reading;

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(PersonaId, PersonaName, first.MessageId, It.IsAny<double>(), "reflex: read on to a newer line"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TurnTellsTheRoomHowKeenTheyWere()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, "(pass)");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, triggering.MessageId, It.Is<double?>(u => u > 0)), Times.Once);
    }

    [Fact]
    public async Task AnswerAsync_GoesStraightToTheVoice_WithNoPassOnOffer()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("listen"));
        var jobs = WireCapturing(fx.Router, "Not taking the bait.");
        var line = Msg(HumanId, "bet nobody here can name a bread");
        Hears(fx,
            line,
            new ChatMessage { MessageId = 2, SenderId = PersonaId, SenderType = "assistant", Content = "yo" },
            new ChatMessage { MessageId = 3, SenderId = PersonaId, SenderType = "assistant", Content = "and another thing" });
        fx.ChatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(5);

        await fx.Pipeline.AnswerAsync(MakePersona(), Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        var spoke = Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        Assert.Equal("Not taking the bait.", spoke.Content);
        Assert.Contains(ResponsePipeline.OwedReason, spoke.Appraisal);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, line.MessageId, null), Times.Once);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<string>()), Times.Never);
        fx.Reflex!.Verify(r => r.PerceiveAsync(It.IsAny<PartyTown.World.Reflex.Percept>()), Times.Never);
        Assert.DoesNotContain(Passing.Cue, Guidance(jobs.Single()));
    }

    [Fact]
    public async Task AnswerAsync_APassWrittenAnyway_IsNeverPublished_TheyTryAgain()
    {
        var fx = BuildFixture();
        var jobs = WireCapturing(fx.Router, "(pass)", "Rye. Obviously.");
        var line = Msg(HumanId, "bet nobody here can name a bread");
        Hears(fx, line);

        await fx.Pipeline.AnswerAsync(MakePersona(), Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.Equal("Rye. Obviously.", Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes)).Content);
        Assert.Equal(2, jobs.Count);
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.GenerationRetry);
        Assert.DoesNotContain(fx.Events, e => e.Event == MessageStreamEvent.ContentChunk && e.Data.Contains("pass"));
    }

    [Fact]
    public async Task AnswerAsync_NotSpeakingHere_DoesNothing()
    {
        var fx = BuildFixture();
        var line = Msg(Guid.NewGuid(), "hello?");
        var stranger = new Persona(Guid.NewGuid(), "Stranger", "You are a stranger.", null);

        await fx.Pipeline.AnswerAsync(stranger, Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_HappyPath_OneVoiceCall_AppendsMessageWithAppraisal()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(0, deliberate: false, "speak"));
        var jobs = WireCapturing(fx.Router, "Bring it. Let's roll.");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var spoke = Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        Assert.Equal("Bring it. Let's roll.", spoke.Content);
        Assert.Single(jobs);
        Assert.Equal(JobComplexity.CharacterVoice, jobs[0].JobComplexity);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, triggering.MessageId, It.IsAny<double?>()), Times.Once);
        Assert.Contains("\"instruction\":null", spoke.Appraisal);
        Assert.Contains("\"reason\":\"reflex: speak\"", spoke.Appraisal);
        Assert.Contains("\"stop\":false", spoke.Appraisal);
        Assert.DoesNotContain("How this moment hits you", Guidance(jobs[0]));
        Assert.DoesNotContain("What you mean to get across", Guidance(jobs[0]));
    }

    [Fact]
    public async Task HandleAsync_TheRoomSeesTyping_OnlyOnceTheVoiceStarts()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, "ok");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var order = fx.Events.Select(e => e.Event).ToList();
        var attend = order.IndexOf(MessageStreamEvent.PersonaEvaluatingResponse);
        var typing = order.IndexOf(MessageStreamEvent.PersonaEvaluationComplete);
        var first = order.IndexOf(MessageStreamEvent.ContentChunk);
        Assert.True(attend >= 0 && attend < typing && typing < first);
        Assert.Single(fx.Events, e => e.Event == MessageStreamEvent.PersonaEvaluationComplete);
        Assert.Contains("\"instruction\":null", fx.Events.Single(e => e.Event == MessageStreamEvent.PersonaEvaluationComplete).Data);
    }

    [Fact]
    public async Task HandleAsync_UntilTheVoiceStarts_TheTurnIsStillCheapToCancel()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fx = BuildFixture(judge: new RoomJudge(holds: release.Task));
        var startedSpeaking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint.Setup(e => e.GetAttributionAsync()).ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "speaker" });
        endpoint.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SignalThenHang(startedSpeaking, ct));
        var emote = new Mock<ILlmEndpointGrain>();
        emote.Setup(e => e.GetAttributionAsync()).ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "emoter" });
        emote.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => StreamChunks(new[] { "*stops*" }, ct));
        var routed = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routed) == 1 ? endpoint.Object : emote.Object);
        var room = Guid.NewGuid();
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        var turn = Handle(fx, triggering, room);
        while (fx.Store.SnapshotForChatGroup(room).Count == 0) await Task.Delay(5);
        var (_, gen) = Assert.Single(fx.Store.SnapshotForChatGroup(room));
        Assert.Equal(InFlightPhase.Weighing, gen.Snapshot().Phase);

        release.SetResult();
        await startedSpeaking.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var speaking = gen.Snapshot();
        Assert.Equal(InFlightPhase.Speaking, speaking.Phase);
        Assert.Contains("anyone up for chess", speaking.ReplyingTo);
        Assert.Equal(string.Empty, speaking.Feeling);
        Assert.Equal(string.Empty, speaking.Intent);

        gen.CancelForRace("never mind", "Mira");
        await turn.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task HandleAsync_TheWeighedGravityReachesTheSpeakingCall()
    {
        var judge = new RoomJudge(gravity: 1.0);
        var fx = BuildFixture(judge: judge);
        var jobs = WireCapturing(fx.Router, "Honestly? Mixed.");
        var triggering = Msg(Guid.NewGuid(), "how do you feel about your life lately?", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        Assert.Contains(UtteranceRhythm.Cue(UtteranceRhythm.Length.Full), Guidance(jobs.Last()));
        var (state, questions) = Assert.Single(judge.Asked);
        Assert.Equal(Gravity.Question(PersonaName).Instructions, Assert.IsType<NoulQuestion>(questions[Gravity.Key]).Instructions);
        Assert.False(questions.ContainsKey(Gravity.RecallKey));
        Assert.Contains("how do you feel about your life lately?", state);
        Assert.DoesNotContain("means to get across", state);
        Assert.DoesNotContain("put this off", state);
    }

    [Fact]
    public async Task InitiateAsync_TheyOpenIt_WhereAReplyAnswers()
    {
        var fx = BuildFixture();
        var old = Msg(Guid.NewGuid(), "zondag kom ik misschien even langs", senderType: "assistant");
        Hears(fx, old);

        var opening = WireCapturing(fx.Router, "zondag nog steeds?");
        await fx.Pipeline.InitiateAsync(MakePersona(), Guid.NewGuid(), fx.ChatGroup.Object, fx.Store, "Ask whether Sunday still stands.", "restless", CancellationToken.None);
        var replying = WireCapturing(fx.Router, "yes");
        await Handle(fx, Msg(Guid.NewGuid(), "anyone up for chess"));

        Assert.Contains(SpeakingSession.Opening(false), Guidance(opening.Last()));
        Assert.Contains("Now write only what", Guidance(opening.Last()));
        Assert.Contains("How this moment hits you: restless", Guidance(opening.Last()));
        Assert.Contains("What you mean to get across: Ask whether Sunday still stands.", Guidance(opening.Last()));
        Assert.DoesNotContain(Passing.Cue, Guidance(opening.Last()));
        Assert.DoesNotContain("Nobody just", Guidance(replying.Last()));
        Assert.Contains("Now reply with only what", Guidance(replying.Last()));
    }

    [Fact]
    public async Task HandleAsync_TheJudgeFails_TodaysDrawStands()
    {
        var fx = BuildFixture(judge: new RoomJudge(fails: true));
        var jobs = WireCapturing(fx.Router, "yes");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Equal("yes", Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes)).Content);
        Assert.Contains(OldCues, cue => Guidance(jobs.Last()).Contains(cue));
    }

    [Fact]
    public async Task AnswerAsync_TheJudgeHearsTheyPutItOffToAnswerProperly()
    {
        var judge = new RoomJudge(gravity: 0.0);
        var fx = BuildFixture(judge: judge);
        var jobs = WireCapturing(fx.Router, "ok so.");
        var line = Msg(Guid.NewGuid(), "how do you feel about your job?", senderType: "assistant");
        Hears(fx, line);

        await fx.Pipeline.AnswerAsync(MakePersona(), Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.Contains($"{PersonaName} put this off earlier to answer it properly.", Assert.Single(judge.Asked).State);
        Assert.Contains(OldCues, cue => Guidance(jobs.Last()).Contains(cue));
    }

    [Fact]
    public async Task HandleAsync_TheJudgesMemory_SurfacesInTheVoice_AndIsStrengthened()
    {
        var remembered = new[]
        {
            Memory("Mira beat you at chess last spring and never let it go.", 0.8, RecallArm.Relevant),
            Memory("You promised Mira a rematch on a rainy day.", 0.6, RecallArm.Relevant),
        };
        var judge = new RoomJudge(memory: "2");
        var fx = BuildFixture(judge: judge, memories: remembered);
        var jobs = WireCapturing(fx.Router, "Rematch?");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var spoke = Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        var system = jobs.Single().Messages.First(m => m.Role == "system").Content;
        Assert.Contains("# A memory surfacing for you", system);
        Assert.Contains("You promised Mira a rematch on a rainy day.", system);
        Assert.DoesNotContain("never let it go", system);
        fx.MemoryRepo.Verify(m => m.StrengthenRecollectionAsync(remembered[1].EdgeId, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("\"picked\":2", spoke.Appraisal);
        Assert.DoesNotContain("\"autoDeployed\":true", spoke.Appraisal);
        var recall = Assert.IsType<ChoiceQuestion>(Assert.Single(judge.Asked).Questions[Gravity.RecallKey]);
        Assert.Equal(new[] { Gravity.Nothing, "1", "2" }, recall.Options.Keys.ToArray());
    }

    [Fact]
    public async Task HandleAsync_TheJudgeHasNoneOnTheirMind_NothingSurfaces_EvenOverTheFloor()
    {
        var remembered = new[] { Memory("Mira beat you at chess last spring.", 0.9, RecallArm.Relevant) };
        var fx = BuildFixture(judge: new RoomJudge(memory: Gravity.Nothing), memories: remembered);
        var jobs = WireCapturing(fx.Router, "Sure.");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var spoke = Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        Assert.DoesNotContain("# A memory surfacing for you", jobs.Single().Messages.First(m => m.Role == "system").Content);
        fx.MemoryRepo.Verify(m => m.StrengthenRecollectionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Contains("\"candidates\"", spoke.Appraisal);
        Assert.Contains("\"picked\":null", spoke.Appraisal);
    }

    [Fact]
    public async Task HandleAsync_NoViewFromTheJudge_TheSalienceFloorStillDeploys()
    {
        var remembered = new[] { Memory("Mira beat you at chess last spring.", 0.9, RecallArm.Relevant) };
        var fx = BuildFixture(judge: new RoomJudge(), memories: remembered);
        var jobs = WireCapturing(fx.Router, "Sure.");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var spoke = Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        Assert.Contains("Mira beat you at chess last spring.", jobs.Single().Messages.First(m => m.Role == "system").Content);
        Assert.Contains("\"autoDeployed\":true", spoke.Appraisal);
        fx.MemoryRepo.Verify(m => m.StrengthenRecollectionAsync(remembered[0].EdgeId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ADmGetsAMemoryWeighedToo()
    {
        var remembered = new[] { Memory("Sam told you about the new job in Utrecht.", 0.5) };
        var fx = BuildFixture(kind: ChatGroupKinds.Direct, judge: new RoomJudge(memory: "1"), memories: remembered);
        var jobs = WireCapturing(fx.Router, "How's Utrecht?");
        var triggering = Msg(HumanId, "hey you");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Contains("Sam told you about the new job in Utrecht.", jobs.Single().Messages.First(m => m.Role == "system").Content);
    }

    [Fact]
    public async Task HandleAsync_Asleep_DeclinesWithoutALlmCall()
    {
        var fx = BuildFixture(asleep: true);
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Contains(ResponsePipeline.AsleepReason, Declared(fx));
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.PersonaDeclinedResponse && e.Done);
        fx.Router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NothingSayable_DeclinesAndTellsTheRoom()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, "   ");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Contains(ResponsePipeline.UnsayableReason, Declared(fx));
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.PersonaDeclinedResponse && e.Done);
    }

    [Fact]
    public async Task HandleAsync_SpeakingFailsOnce_RetriesAndAccumulatesFromFreshStream()
    {
        var fx = BuildFixture();
        var speakingCall = 0;
        var speakingEp = new Mock<ILlmEndpointGrain>();
        speakingEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "speaker" });
        speakingEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) =>
                Interlocked.Increment(ref speakingCall) == 1
                    ? MidStreamFailure("partial", ct)
                    : StreamChunks(new[] { "recovered content" }, ct));
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(speakingEp.Object);
        var triggering = Msg(Guid.NewGuid(), "kick things off");
        Hears(fx, triggering);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Handle(fx, triggering, ct: cts.Token);

        Assert.Equal("recovered content", Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes)).Content);
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.GenerationRetry);
        Assert.Equal(2, speakingCall);
    }

    [Fact]
    public async Task HandleAsync_RaceCancelDuringSpeaking_GeneratesEmote()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(0, deliberate: false, "now"));
        var emoteText = "*pulls back, listening instead*";
        var chatGroupId = Guid.NewGuid();
        var startedSpeaking = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) == 1 ? speakingEp.Object : emoteEp.Object);

        var triggering = Msg(Guid.NewGuid(), "kick things off", senderType: "assistant");
        Hears(fx, triggering);

        var pipelineTask = Handle(fx, triggering, chatGroupId);

        await startedSpeaking.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var (_, inFlightGen) = Assert.Single(fx.Store.SnapshotForChatGroup(chatGroupId));
        inFlightGen.CancelForRace("forget it — totally different topic", "Mira");

        await pipelineTask.WaitAsync(TimeSpan.FromSeconds(5));

        var interrupted = Assert.IsType<TurnOutcome.Interrupted>(Assert.Single(fx.Outcomes));
        Assert.Equal(emoteText, interrupted.Emote);
        Assert.Contains("\"raceCancelled\":true", interrupted.Appraisal);
        Assert.Contains("\"reason\":\"reflex: now\"", interrupted.Appraisal);
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.GenerationComplete && e.Done);
    }

    [Fact]
    public async Task HandleAsync_RaceCancelBeforeTheVoice_DeclinesWithTheRaceReason_NoEmote()
    {
        var never = new TaskCompletionSource();
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(0, deliberate: false, "now"), judge: new RoomJudge(holds: never.Task));
        var room = Guid.NewGuid();
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        var turn = Handle(fx, triggering, room);
        while (fx.Store.SnapshotForChatGroup(room).Count == 0) await Task.Delay(5);
        var (_, gen) = Assert.Single(fx.Store.SnapshotForChatGroup(room));
        Assert.Equal(InFlightPhase.Weighing, gen.Snapshot().Phase);
        gen.CancelForRace("forget chess, pub?", "Mira");
        await turn.WaitAsync(TimeSpan.FromSeconds(5));

        var appraisal = Declared(fx);
        Assert.Contains("\"raceCancelled\":true", appraisal);
        Assert.Contains("\"reason\":\"reflex: now\"", appraisal);
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.PersonaDeclinedResponse && e.Done && e.Data.Contains("reflex: now"));
        fx.Router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnswerAsync_NothingSayable_DeclinesAndTellsTheRoom()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, "  ");
        var line = Msg(HumanId, "bet nobody here can name a bread");
        Hears(fx, line);

        await fx.Pipeline.AnswerAsync(MakePersona(), Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.Contains(ResponsePipeline.UnsayableReason, Declared(fx));
        Assert.Contains(fx.Events, e => e.Event == MessageStreamEvent.PersonaDeclinedResponse && e.Done);
    }

    [Fact]
    public async Task AnswerAsync_ALineThatMentionsPassing_IsPublishedAsItIs()
    {
        var fx = BuildFixture();
        var jobs = WireCapturing(fx.Router, "Math (passed), physics was the hard one.");
        var line = Msg(HumanId, "how did the exams go?");
        Hears(fx, line);

        await fx.Pipeline.AnswerAsync(MakePersona(), Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.Equal("Math (passed), physics was the hard one.", Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes)).Content);
        Assert.Single(jobs);
    }

    [Fact]
    public async Task HandleAsync_TheMarkAmongOtherWords_IsStripped_TheRestIsSaid()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, "Sure (pa", "ss)");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.Equal("Sure", Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes)).Content);
        Assert.DoesNotContain("pass", string.Concat(fx.Events.Where(e => e.Event == MessageStreamEvent.ContentChunk).Select(e => e.Data)));
    }

    [Fact]
    public async Task AHumansLine_EveryoneLetsItGo_TheRoomStillGetsItAnswered()
    {
        var bob = Guid.NewGuid();
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("listen"), also: bob);
        var bobsReflex = new Mock<PartyTown.World.Reflex.IPersonaReflexGrain>();
        bobsReflex.Setup(g => g.PerceiveAsync(It.IsAny<PartyTown.World.Reflex.Percept>())).ReturnsAsync(PartyTown.World.Reflex.ReflexVerdict.Reply(0, false, "speak"));
        bobsReflex.Setup(g => g.GetMindAsync()).ReturnsAsync(string.Empty);
        fx.GrainFactory.Setup(f => f.GetGrain<PartyTown.World.Reflex.IPersonaReflexGrain>(It.Is<string>(k => k.EndsWith(bob.ToString("N"))), null)).Returns(bobsReflex.Object);
        var voiced = new List<LlmGenerationJob>();
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var ep = new Mock<ILlmEndpointGrain>();
                ep.Setup(e => e.GetAttributionAsync()).ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
                ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
                    .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
                    {
                        lock (voiced) voiced.Add(job);
                        return StreamChunks(new[] { Guidance(job).Contains(Passing.Cue) ? "(pass)" : "Koffie om tien?" }, ct);
                    });
                return ep.Object;
            });

        var line = Msg(HumanId, "goedemorgen allemaal, iemand zin in koffie straks?", messageId: 1);
        Hears(fx, line);
        var owed = new OwedAnswers(TimeSpan.FromMinutes(10));
        owed.Expect(line.MessageId, HumanId, new[] { PersonaId, bob }, 0);
        var turns = new Dictionary<int, Guid>();
        var next = 100;
        fx.ChatGroup.Setup(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()))
            .ReturnsAsync((Guid who, int? on, double? urge) =>
            {
                lock (turns) turns[++next] = who;
                if (on is { } l) owed.Urge(l, who, urge);
                return next;
            });
        fx.ChatGroup.Setup(g => g.EndTurnAsync(It.IsAny<int>(), It.IsAny<TurnOutcome>()))
            .Callback<int, TurnOutcome>((turn, outcome) =>
            {
                lock (fx.Outcomes) fx.Outcomes.Add(outcome);
                if (outcome is TurnOutcome.Spoke) owed.Landed(turn, turns[turn], null);
                owed.Ended(line.MessageId, turns[turn], failed: outcome is TurnOutcome.Failed);
            })
            .Returns(Task.CompletedTask);
        fx.ChatGroup.Setup(g => g.RecordSkippedTurnAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<string>()))
            .Callback<Guid, string, int, double, string>((who, _, on, urge, reason) =>
            {
                owed.Urge(on, who, urge);
                if (reason.StartsWith("reflex: " + PartyTown.World.Reflex.ReflexVerdict.Holding, StringComparison.Ordinal)) owed.Answered(on);
                else owed.Ended(on, who);
            })
            .Returns(Task.CompletedTask);
        var alice = MakePersona();
        var bobPersona = new Persona(bob, "Bob", "You are Bob.", bio: null);

        await Handle(fx, line);
        await fx.Pipeline.HandleAsync(bobPersona, Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.Contains("\"passed\":true", Assert.IsType<TurnOutcome.Declined>(Assert.Single(fx.Outcomes)).Appraisal);
        var present = new[] { new SelfView(PersonaId, PersonaName, DriverKind.LLM, null, null, 0.5, 0.5), new SelfView(bob, "Bob", DriverKind.LLM, null, null, 0.5, 0.5) };
        var (lineId, who) = Assert.Single(owed.Due(_ => false, present));
        await fx.Pipeline.AnswerAsync(who == bob ? bobPersona : alice, Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.Equal(line.MessageId, lineId);
        Assert.Equal("Koffie om tien?", Assert.IsType<TurnOutcome.Spoke>(fx.Outcomes.Last()).Content);
        Assert.DoesNotContain(Passing.Cue, Guidance(voiced.Last()));
    }

    [Fact]
    public async Task HandleAsync_ExternalCancelDuringSpeaking_MarksFailedWithCancelled()
    {
        var fx = BuildFixture();
        var speakingEp = new Mock<ILlmEndpointGrain>();
        speakingEp.Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "speaker" });
        speakingEp.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => HangUntilCancelled(ct));
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(speakingEp.Object);
        var triggering = Msg(Guid.NewGuid(), "kick things off");
        Hears(fx, triggering);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await Handle(fx, triggering, ct: cts.Token);

        Assert.Equal("cancelled", Assert.IsType<TurnOutcome.Failed>(Assert.Single(fx.Outcomes)).Error);
    }

    [Fact]
    public async Task HandleAsync_RepairHint_ReachesTheVoice_AndIsUsedOnce()
    {
        var fx = BuildFixture();
        var chatGroupId = Guid.NewGuid();
        fx.Store.SetRepairHint(chatGroupId, new RepairHint(MissedMessageId: 42, MissedSenderName: "Mira", MissedContent: "you missed this"));
        var jobs = WireCapturing(fx.Router, "oh, sorry Mira");
        var triggering = Msg(Guid.NewGuid(), "anyone home?");
        Hears(fx, triggering);

        await Handle(fx, triggering, chatGroupId);

        Assert.Contains("Just before you spoke, Mira said: \"you missed this\". You hadn't seen it when you wrote your last line; pick it up if that's natural.", Guidance(jobs.Single()));
        Assert.Null(fx.Store.ConsumeRepairHint(chatGroupId));
    }

    [Fact]
    public async Task HandleAsync_WithStances_RendersWhereYouStand()
    {
        var fx = BuildFixture();
        fx.MemoryRepo.Setup(m => m.RecallStancesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StanceLine("Vlad exaggerates — you've caught him inflating three stories", -0.7),
                new StanceLine("Lisp is elegant once it clicks", 0.8),
            });
        var jobs = WireCapturing(fx.Router, "Heard this one before.");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var systemPrompt = jobs.Single().Messages.First(m => m.Role == "system").Content;
        Assert.Contains("# Where you stand", systemPrompt);
        Assert.Contains("Vlad exaggerates", systemPrompt);
        Assert.Contains("Lisp is elegant", systemPrompt);
        fx.MemoryRepo.Verify(m => m.RecallStancesAsync(
            It.IsAny<Guid>(), PersonaId, It.IsAny<IReadOnlyList<Guid>>(),
            triggering.Content!, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NoStances_OmitsWhereYouStandBlock()
    {
        var fx = BuildFixture();
        var jobs = WireCapturing(fx.Router, "Sure.");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        Assert.DoesNotContain("# Where you stand", jobs.Single().Messages.First(m => m.Role == "system").Content);
    }

    [Fact]
    public async Task HandleAsync_TalkTouchingASkill_RendersTheKnowHow()
    {
        var fx = BuildFixture();
        var skills = new Mock<PartyTown.World.Skills.ISkillsGrain>();
        skills.Setup(g => g.GetAsync()).ReturnsAsync(new List<PartyTown.World.Skills.SkillView>
        {
            new(Guid.NewGuid(), "playing chess", "You develop knights before bishops and never trade your queen early.", new List<string> { "chess", "board" },
                PartyTown.World.Skills.SkillSource.Sheet, PartyTown.World.Skills.SkillLevel.SecondNature, false, 30, 0, true),
            new(Guid.NewGuid(), "pulling espresso", "You dial the grinder in every morning.", new List<string> { "espresso", "coffee" },
                PartyTown.World.Skills.SkillSource.Sheet, PartyTown.World.Skills.SkillLevel.SecondNature, false, 30, 0, true),
        });
        fx.GrainFactory.Setup(f => f.GetGrain<PartyTown.World.Skills.ISkillsGrain>(It.IsAny<string>(), null)).Returns(skills.Object);
        var jobs = WireCapturing(fx.Router, "Me!");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        Hears(fx, triggering);

        await Handle(fx, triggering);

        var systemPrompt = jobs.Single().Messages.First(m => m.Role == "system").Content;
        Assert.Contains("# What you know how to do\n- Playing chess (second nature): You develop knights", systemPrompt);
        Assert.DoesNotContain("espresso", systemPrompt);
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
