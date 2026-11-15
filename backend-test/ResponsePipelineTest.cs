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

    private sealed record Fixture(
        ResponsePipeline Pipeline,
        InFlightStore Store,
        Mock<IChatGroupGrain> ChatGroup,
        Mock<IGrainFactory> GrainFactory,
        Mock<ILlmRouterGrain> Router,
        Mock<IPersonaRootGrain> PersonaRoot,
        Mock<IMemoryRepository> MemoryRepo,
        List<TurnOutcome> Outcomes);

    private static Fixture BuildFixture(PartyTown.World.Reflex.ReflexVerdict? verdict = null)
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
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage>());
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(0);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
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
                new Persona { Id = PersonaId, Name = PersonaName, SystemPrompt = $"You are {PersonaName}.", Bio = null })
        });

        chatGroup.Setup(g => g.GetCastAsync()).ReturnsAsync(() => RoomCast.Create(
            partyGrain.Object.GetCastAsync().Result,
            new HashSet<Guid> { PersonaId },
            new Dictionary<Guid, DriverKind>(),
            ChatGroupKinds.Room));

        var factory = new Mock<IGrainFactory>();
        factory.Setup(f => f.GetGrain<ILlmRouterGrain>(0L, null)).Returns(router.Object);
        factory.Setup(f => f.GetGrain<IPersonaRootGrain>(Guid.Empty, null)).Returns(personaRoot.Object);
        factory.Setup(f => f.GetGrain<IPartyGrain>(partyId, null)).Returns(partyGrain.Object);

        var memoryRepo = new Mock<IMemoryRepository>();
        memoryRepo.Setup(m => m.RecallAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ReturnsAsync(Array.Empty<RecalledMemory>());
        memoryRepo.Setup(m => m.RecallStancesAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StanceLine>());

        PartyTown.World.Reflex.ReflexGate? reflex = null;
        if (verdict is not null)
        {
            var reflexGrain = new Mock<PartyTown.World.Reflex.IPersonaReflexGrain>();
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
            reflex: reflex);

        return new Fixture(pipeline, store, chatGroup, factory, router, personaRoot, memoryRepo, outcomes);
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
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task HandleAsync_PreGateObviousSkip_RecordsSkippedTurnAndSkipsLlm()
    {
        var fx = BuildFixture();
        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "carry on without me");

        var assistantHistory = new List<ChatMessage>
        {
            new() { MessageId = 10, SenderId = otherSenderId, SenderType = "user", Content = "hi all" },
            new() { MessageId = 11, SenderId = PersonaId,     SenderType = "assistant", Content = "yo" },
            new() { MessageId = 12, SenderId = PersonaId,     SenderType = "assistant", Content = "and another thing" },
        };
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(int.MaxValue, It.IsAny<Guid?>()))
            .ReturnsAsync(assistantHistory);
        fx.ChatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(5);

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

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

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        fx.ChatGroup.Verify(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(),
            It.IsAny<double>(), It.IsAny<string>()), Times.Never);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.Router.Verify(r => r.RouteAsync(
            It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_DecisionDeclines_MarksStoppedWithDeclinedAppraisal()
    {
        var fx = BuildFixture();
        var declineJson = """{"gutReaction":"not interested","memoryToReference":null,"wouldSay":"","respond":false}""";
        WireRouter(fx.Router, declineJson);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        var declinedEvents = new List<MessageStreamEvent>();
        fx.ChatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Callback<int, MessageStreamEvent>((_, evt) => declinedEvents.Add(evt))
            .Returns(Task.CompletedTask);

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        var capturedAppraisal = Assert.IsType<TurnOutcome.Declined>(Assert.Single(fx.Outcomes)).Appraisal;
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, triggering.MessageId, It.IsAny<double?>()), Times.Once);
        Assert.NotNull(capturedAppraisal);
        Assert.Contains("\"reason\"", capturedAppraisal);
        Assert.Contains("\"stop\":true", capturedAppraisal);
        Assert.Contains("not interested", capturedAppraisal);

        Assert.Contains(declinedEvents, e => e.Event == MessageStreamEvent.PersonaDeclinedResponse && e.Done);
    }

    [Fact]
    public async Task HandleAsync_ReflexTakesIt_NoSlotNoLlm_ReasonInTheThoughtLog()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("putting it off"));
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess", senderType: "assistant");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(MakePersona(), Guid.NewGuid(), triggering, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.Router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(PersonaId, PersonaName, triggering.MessageId, It.IsAny<double>(), "reflex: putting it off"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_HumanOpensAQuietRoom_ReflexCantDropIt()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("listen"));
        WireRouter(fx.Router, """{"gutReaction":"hm","memoryToReference":null,"wouldSay":"","respond":false}""");
        var triggering = Msg(Guid.NewGuid(), "hiya");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(MakePersona(), Guid.NewGuid(), triggering, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, triggering.MessageId, It.IsAny<double?>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ReflexTorn_NamedLine_GoesToTheDecisionModel_NotAutoRespond()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(0, deliberate: true, "torn"));
        WireRouter(fx.Router, """{"gutReaction":"not now","memoryToReference":null,"wouldSay":"","respond":false}""");
        var triggering = Msg(Guid.NewGuid(), $"{PersonaName} what do you reckon", senderType: "assistant");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(MakePersona(), Guid.NewGuid(), triggering, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.IsType<TurnOutcome.Declined>(Assert.Single(fx.Outcomes));
    }

    [Fact]
    public async Task HandleAsync_NewerLineWhileReading_OnlyTheNewerOneReplies()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Reply(5_000, deliberate: false, "now"));
        var room = Guid.NewGuid();
        var first = Msg(Guid.NewGuid(), "anyone up for chess", messageId: 1, senderType: "assistant");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { first });

        var reading = fx.Pipeline.HandleAsync(MakePersona(), room, first, fx.ChatGroup.Object, fx.Store, CancellationToken.None);
        await fx.Store.ReadAsync(room, TimeSpan.FromMilliseconds(1), CancellationToken.None);
        await reading;

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<double?>()), Times.Never);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(PersonaId, PersonaName, first.MessageId, It.IsAny<double>(), "reflex: read on to a newer line"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_TurnTellsTheRoomHowKeenTheyWere()
    {
        var fx = BuildFixture();
        WireRouter(fx.Router, """{"gutReaction":"hm","memoryToReference":null,"wouldSay":"","respond":false}""");
        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(MakePersona(), Guid.NewGuid(), triggering, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, triggering.MessageId, It.Is<double?>(u => u > 0)), Times.Once);
    }

    [Fact]
    public async Task AnswerAsync_TheDecisionPhaseDeclines_TheyAnswerAnyway_FromTheirReaction()
    {
        var fx = BuildFixture(PartyTown.World.Reflex.ReflexVerdict.Taken("listen"));
        var jobs = new List<LlmGenerationJob>();
        var call = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var content = Interlocked.Increment(ref call) == 1
                    ? """{"gutReaction":"I'm not rising to it","memoryToReference":null,"wouldSay":"","respond":false}"""
                    : "Not taking the bait.";
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
        var line = Msg(Guid.NewGuid(), "bet nobody here can name a bread");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage>
            {
                line,
                new() { MessageId = 2, SenderId = PersonaId, SenderType = "assistant", Content = "yo" },
                new() { MessageId = 3, SenderId = PersonaId, SenderType = "assistant", Content = "and another thing" },
            });
        fx.ChatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(5);

        await fx.Pipeline.AnswerAsync(MakePersona(), Guid.NewGuid(), line, fx.ChatGroup.Object, fx.Store, CancellationToken.None);

        Assert.Equal("Not taking the bait.", Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes)).Content);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, line.MessageId, null), Times.Once);
        fx.ChatGroup.Verify(g => g.RecordSkippedTurnAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<string>()), Times.Never);
        Assert.Contains(jobs.Last().Messages, m => m.Content?.Contains("I'm not rising to it") == true);
    }

    [Fact]
    public void Answering_KeepsTheirReaction_ButNotAnUnreadableDecision()
    {
        var declined = new ShouldRespondResult { Respond = false, Instruction = "", Reason = "I'm not rising to it" };
        var unreadable = declined with { Reason = "Fallback — unparseable decision. Raw: {" };
        var spoke = declined with { Respond = true, Instruction = "Rye." };

        Assert.Equal(declined with { Respond = true }, ResponsePipeline.Answering(declined));
        Assert.True(ResponsePipeline.Answering(unreadable) is { Respond: true, Shortcut: true });
        Assert.Same(spoke, ResponsePipeline.Answering(spoke));
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
    public async Task HandleAsync_HappyPath_AppendsMessageWithAppraisal()
    {
        var fx = BuildFixture();
        var decisionJson = """{"gutReaction":"I have thoughts","memoryToReference":null,"wouldSay":"Bring it.","respond":true}""";
        var speakingText = "Bring it. Let's roll.";
        WireRouterSequence(fx.Router, decisionJson, speakingText);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        var spoke = Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes));
        var capturedAppraisal = spoke.Appraisal;
        Assert.Equal(speakingText, spoke.Content);
        fx.ChatGroup.Verify(g => g.BeginTurnAsync(PersonaId, triggering.MessageId, It.IsAny<double?>()), Times.Once);
        Assert.NotNull(capturedAppraisal);
        Assert.Contains("\"reason\":\"I have thoughts\"", capturedAppraisal);
        Assert.Contains("\"instruction\":\"Bring it.\"", capturedAppraisal);
        Assert.Contains("\"stop\":false", capturedAppraisal);
    }

    [Fact]
    public async Task HandleAsync_SpeakingFailsOnce_RetriesAndAccumulatesFromFreshStream()
    {
        var fx = BuildFixture();
        var decisionJson = """{"gutReaction":"sure","memoryToReference":null,"wouldSay":"on it.","respond":true}""";

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
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) == 1 ? decisionEp.Object : speakingEp.Object);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "kick things off");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        var streamEvents = new List<MessageStreamEvent>();
        fx.ChatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>()))
            .Callback<int, MessageStreamEvent>((_, evt) => streamEvents.Add(evt))
            .Returns(Task.CompletedTask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: cts.Token);

        Assert.Equal("recovered content", Assert.IsType<TurnOutcome.Spoke>(Assert.Single(fx.Outcomes)).Content);
        Assert.Contains(streamEvents, e => e.Event == MessageStreamEvent.GenerationRetry);
        Assert.Equal(2, speakingCall);
    }

    [Fact]
    public async Task HandleAsync_RaceCancelDuringSpeaking_GeneratesEmote()
    {
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
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) switch
            {
                1 => decisionEp.Object,
                2 => speakingEp.Object,
                _ => emoteEp.Object,
            });

        var triggering = Msg(Guid.NewGuid(), "kick things off");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

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
        inFlightGen.CancelForRace("forget it — totally different topic", "Mira");

        await pipelineTask.WaitAsync(TimeSpan.FromSeconds(5));

        var interrupted = Assert.IsType<TurnOutcome.Interrupted>(Assert.Single(fx.Outcomes));
        var capturedAppraisal = interrupted.Appraisal;
        Assert.Equal(emoteText, interrupted.Emote);
        Assert.NotNull(capturedAppraisal);
        Assert.Contains("\"raceCancelled\":true", capturedAppraisal);
    }

    [Fact]
    public async Task HandleAsync_ExternalCancelDuringSpeaking_MarksFailedWithCancelled()
    {
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
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Interlocked.Increment(ref routeCall) == 1 ? decisionEp.Object : speakingEp.Object);

        var triggering = Msg(Guid.NewGuid(), "kick things off");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));
        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: cts.Token);

        Assert.Equal("cancelled", Assert.IsType<TurnOutcome.Failed>(Assert.Single(fx.Outcomes)).Error);
    }

    [Fact]
    public async Task HandleAsync_RepairHintPresent_ConsumedAndClearedAfterDecisionPass()
    {
        var fx = BuildFixture();
        var chatGroupId = Guid.NewGuid();
        var hint = new RepairHint(MissedMessageId: 42, MissedSenderName: "Mira", MissedContent: "you missed this");
        fx.Store.SetRepairHint(chatGroupId, hint);

        var declineJson = """{"gutReaction":"meh","memoryToReference":null,"wouldSay":"","respond":false}""";
        WireRouter(fx.Router, declineJson);

        var otherSenderId = Guid.NewGuid();
        var triggering = Msg(otherSenderId, "anyone home?");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId,
            triggering,
            fx.ChatGroup.Object,
            fx.Store,
            CancellationToken.None);

        Assert.IsType<TurnOutcome.Declined>(Assert.Single(fx.Outcomes));
        Assert.Null(fx.Store.ConsumeRepairHint(chatGroupId));
    }

    [Fact]
    public async Task HandleAsync_WithStances_RendersWhereYouStandInBothPrompts()
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

        var decisionJson = """{"gutReaction":"oh, this again","memoryToReference":null,"wouldSay":"Heard this one before.","respond":true}""";
        var speakingText = "Heard this one before.";

        var capturedJobs = new List<LlmGenerationJob>();
        var routeCall = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var content = Interlocked.Increment(ref routeCall) == 1 ? decisionJson : speakingText;
                var ep = new Mock<ILlmEndpointGrain>();
                ep.Setup(e => e.GetAttributionAsync())
                    .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
                ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
                    .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
                    {
                        lock (capturedJobs) capturedJobs.Add(job);
                        return StreamChunks(new[] { content }, ct);
                    });
                return ep.Object;
            });

        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        Assert.Equal(2, capturedJobs.Count);
        foreach (var job in capturedJobs)
        {
            var systemPrompt = job.Messages.First(m => m.Role == "system").Content;
            Assert.Contains("# Where you stand", systemPrompt);
            Assert.Contains("Vlad exaggerates", systemPrompt);
            Assert.Contains("Lisp is elegant", systemPrompt);
        }

        fx.MemoryRepo.Verify(m => m.RecallStancesAsync(
            It.IsAny<Guid>(), PersonaId, It.IsAny<IReadOnlyList<Guid>>(),
            triggering.Content!, 5, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_NoStances_OmitsWhereYouStandBlock()
    {
        var fx = BuildFixture();
        var decisionJson = """{"gutReaction":"hm","memoryToReference":null,"wouldSay":"Sure.","respond":true}""";

        var capturedJobs = new List<LlmGenerationJob>();
        var routeCall = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var content = Interlocked.Increment(ref routeCall) == 1 ? decisionJson : "Sure.";
                var ep = new Mock<ILlmEndpointGrain>();
                ep.Setup(e => e.GetAttributionAsync())
                    .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
                ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
                    .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
                    {
                        lock (capturedJobs) capturedJobs.Add(job);
                        return StreamChunks(new[] { content }, ct);
                    });
                return ep.Object;
            });

        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        Assert.Equal(2, capturedJobs.Count);
        Assert.All(capturedJobs, job =>
            Assert.DoesNotContain("# Where you stand",
                job.Messages.First(m => m.Role == "system").Content));
    }

    [Fact]
    public async Task HandleAsync_TalkTouchingASkill_RendersTheKnowHowInBothPrompts()
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

        var decisionJson = """{"gutReaction":"ooh","memoryToReference":null,"wouldSay":"Me!","respond":true}""";
        var capturedJobs = new List<LlmGenerationJob>();
        var routeCall = 0;
        fx.Router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var content = Interlocked.Increment(ref routeCall) == 1 ? decisionJson : "Me!";
                var ep = new Mock<ILlmEndpointGrain>();
                ep.Setup(e => e.GetAttributionAsync())
                    .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "test-model" });
                ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
                    .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
                    {
                        lock (capturedJobs) capturedJobs.Add(job);
                        return StreamChunks(new[] { content }, ct);
                    });
                return ep.Object;
            });

        var triggering = Msg(Guid.NewGuid(), "anyone up for chess");
        fx.ChatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>()))
            .ReturnsAsync(new List<ChatMessage> { triggering });

        await fx.Pipeline.HandleAsync(
            MakePersona(),
            chatGroupId: Guid.NewGuid(),
            triggeringMessage: triggering,
            chatGroupGrain: fx.ChatGroup.Object,
            store: fx.Store,
            ct: CancellationToken.None);

        Assert.Equal(2, capturedJobs.Count);
        foreach (var job in capturedJobs)
        {
            var systemPrompt = job.Messages.First(m => m.Role == "system").Content;
            Assert.Contains("# What you know how to do\n- Playing chess (second nature): You develop knights", systemPrompt);
            Assert.DoesNotContain("espresso", systemPrompt);
        }
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
