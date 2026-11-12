using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Streaming;

namespace BackendTest;

/// <summary>AI rounds count the current exchange, not the Room's lifetime (#128).</summary>
public class ExchangeRoundsTest
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly long T0 = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static ChatMessage Line(int id, Guid sender, long at, string content = "mm, sure.", string type = "assistant") => new()
    {
        MessageId = id, SenderId = sender, SenderType = type, Content = content, SendAt = at,
    };

    /// <summary>Alice and Bob alternating, one line every <paramref name="gap"/>, starting at <paramref name="from"/>.</summary>
    private static List<ChatMessage> Alternating(int count, TimeSpan gap, long from, int firstId = 1)
        => Enumerable.Range(0, count)
            .Select(i => Line(firstId + i, i % 2 == 0 ? Alice : Bob, from + (long)(gap.TotalMilliseconds * i)))
            .ToList();

    // ── The count ──

    [Fact]
    public void OldAllAiHistory_CountsOnlyTheCurrentExchange()
    {
        // Thirty lines at Life tempo: nothing before the latest is within the exchange window.
        var history = Alternating(30, TimeSpan.FromMinutes(30), T0);

        Assert.Equal(1, UrgeMath.CountExchangeRounds(history));
    }

    [Fact]
    public void QuickLinesAfterOldHistory_CountOnlyTheQuickRun()
    {
        var history = Alternating(20, TimeSpan.FromMinutes(30), T0);
        var start = history[^1].SendAt!.Value + (long)TimeSpan.FromHours(3).TotalMilliseconds;
        history.AddRange(Alternating(6, TimeSpan.FromSeconds(10), start, firstId: 21));

        Assert.Equal(6, UrgeMath.CountExchangeRounds(history));
    }

    [Fact]
    public void LinesAFewMinutesApart_StayOneExchange()
    {
        // Life adds a line every few minutes: no long gap, but only the window counts.
        var history = Alternating(40, TimeSpan.FromMinutes(4), T0);

        var window = (int)(UrgeMath.ExchangeWindow / TimeSpan.FromMinutes(4)) + 1;
        Assert.Equal(window, UrgeMath.CountExchangeRounds(history));
    }

    [Fact]
    public void HumanLine_StillEndsTheRun()
    {
        var history = Alternating(3, TimeSpan.FromSeconds(5), T0);
        history.Insert(1, Line(99, Guid.NewGuid(), T0 + 1, "hey", "user"));

        Assert.Equal(2, UrgeMath.CountExchangeRounds(history));
    }

    [Fact]
    public void UntimedHistory_CountsTheWholeRun()
    {
        var history = Alternating(8, TimeSpan.Zero, T0).Select(m => m with { SendAt = null }).ToList();

        Assert.Equal(8, UrgeMath.CountExchangeRounds(history));
    }

    // ── Both counters agree ──

    [Fact]
    public void GrainAndPipelineSnapshot_CountTheSameHistory()
    {
        var room = Guid.NewGuid();
        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent { PartyId = Guid.NewGuid(), ParticipantIds = new HashSet<Guid> { Alice, Bob } });

        void Spoke(Guid who, long at)
        {
            state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = room, SenderId = who, SenderType = "assistant" });
            state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = state.NextMessageId, SenderId = who, Content = "line", SendAt = at });
        }

        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = room, SenderId = Bob, SenderType = "user" });
        state.Apply(new ChatGroupUserMessageEvent
        {
            SenderId = Bob,
            Message = new ChatMessage { MessageId = state.NextMessageId, SenderId = Bob, SenderType = "user", Content = "hi", ChatGroupId = room, SendAt = T0 },
        });
        for (var i = 1; i <= 10; i++) Spoke(i % 2 == 0 ? Alice : Bob, T0 + (long)TimeSpan.FromMinutes(30 * i).TotalMilliseconds);
        var quick = T0 + (long)TimeSpan.FromHours(8).TotalMilliseconds;
        for (var i = 0; i < 4; i++) Spoke(i % 2 == 0 ? Alice : Bob, quick + i * 10_000L);
        // A declined turn and an open slot sit in the log as empty stubs.
        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = room, SenderId = Alice, SenderType = "assistant" });
        state.Apply(new ChatGroupGenerationStoppedEvent { MessageId = state.NextMessageId, SendAt = quick + 50_000 });
        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = room, SenderId = Bob, SenderType = "assistant" });

        // What ChatGroupGrain.CountTrailingAssistantMessagesAsync counts over, and the pipeline's GetMessagesUntilAsync snapshot.
        var grain = UrgeMath.CountExchangeRounds(state.Messages);
        var openSlot = state.NextMessageId;
        var snapshot = state.Messages.Where(m => m.MessageId < openSlot).OrderBy(m => m.MessageId).Select(m => m with { }).ToList();
        var pipeline = UrgeMath.CountExchangeRounds(snapshot);

        Assert.Equal(4, grain);
        Assert.Equal(grain, pipeline);
    }

    // ── Places ──

    [Fact]
    public void Place_OldAiLines_UnnamedLineIsNoObviousSkip()
    {
        var self = new SelfView(Alice, "Alice", DriverKind.LLM, "bio", null, 0.5, 0.5);
        var history = Alternating(12, TimeSpan.FromMinutes(30), T0);
        history.Add(Line(13, Bob, history[^1].SendAt!.Value + (long)TimeSpan.FromMinutes(30).TotalMilliseconds, "nice light in here today."));

        var rounds = UrgeMath.CountExchangeRounds(history);
        var recentSelf = UrgeMath.CountRecentSelfMessages(history, Alice);
        var urge = UrgeMath.CalculateResponseUrge(self, history, rounds);

        Assert.Equal(1, rounds);
        Assert.Equal(0, recentSelf);
        Assert.False(UrgeMath.IsObviousSkip(urge, rounds, recentSelf));
    }

    [Fact]
    public void Place_HeatedExchange_UnnamedLineStillObviousSkips()
    {
        var self = new SelfView(Alice, "Alice", DriverKind.LLM, "bio", null, 0.5, 0.5);
        var history = Alternating(12, TimeSpan.FromSeconds(10), T0);
        history.Add(Line(13, Bob, history[^1].SendAt!.Value + 10_000, "nice light in here today."));

        var rounds = UrgeMath.CountExchangeRounds(history);
        var recentSelf = UrgeMath.CountRecentSelfMessages(history, Alice);
        var urge = UrgeMath.CalculateResponseUrge(self, history, rounds);

        Assert.True(UrgeMath.IsObviousSkip(urge, rounds, recentSelf));
    }

    // ── Persona-to-Persona DMs ──

    [Fact]
    public async Task Dm_OldAllAiHistory_IsNotExhausted()
    {
        var history = Alternating(30, TimeSpan.FromMinutes(30), T0);
        var chatGroup = DmChatGroup(history);
        var trigger = history[^1];

        await HandleAsync(chatGroup, trigger);

        chatGroup.Verify(g => g.RecordSkippedTurnAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<string>()), Times.Never);
        chatGroup.Verify(g => g.BeginTurnAsync(Alice, trigger.MessageId, It.IsAny<double?>()), Times.Once);
    }

    [Fact]
    public async Task Dm_SixQuickLinesInOneExchange_HitTheCapUnderItsOwnReason()
    {
        var history = Alternating(20, TimeSpan.FromMinutes(30), T0);
        var start = history[^1].SendAt!.Value + (long)TimeSpan.FromHours(3).TotalMilliseconds;
        history.AddRange(Alternating(6, TimeSpan.FromSeconds(10), start, firstId: 21));
        var chatGroup = DmChatGroup(history);
        var trigger = history[^1];

        await HandleAsync(chatGroup, trigger);

        chatGroup.Verify(g => g.RecordSkippedTurnAsync(
            Alice, "Alice", trigger.MessageId, It.IsAny<double>(),
            It.Is<string>(r => r.StartsWith("dm-cap"))), Times.Once);
        chatGroup.Verify(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>()), Times.Never);
    }

    [Fact]
    public async Task Dm_FiveQuickLines_StillGetAnswered()
    {
        var history = Alternating(20, TimeSpan.FromMinutes(30), T0);
        var start = history[^1].SendAt!.Value + (long)TimeSpan.FromHours(3).TotalMilliseconds;
        history.AddRange(Alternating(6, TimeSpan.FromSeconds(10), start, firstId: 21).Skip(1));
        var chatGroup = DmChatGroup(history);
        var trigger = history[^1];

        await HandleAsync(chatGroup, trigger);

        chatGroup.Verify(g => g.BeginTurnAsync(Alice, trigger.MessageId, It.IsAny<double?>()), Times.Once);
    }

    /// <summary>A DM between Alice and Bob whose grain counts rounds the way ChatGroupGrain does.</summary>
    private static Mock<IChatGroupGrain> DmChatGroup(List<ChatMessage> history)
    {
        Assert.Equal(Bob, history[^1].SenderId);
        var cast = new[] { Alice, Bob }.Select(id =>
        {
            var name = id == Alice ? "Alice" : "Bob";
            return CastMember.Create(
                new PartyParticipant { Id = id, Name = name, Driver = DriverKind.LLM },
                new Persona(id, name, $"You are {name}.", bio: null));
        }).ToList();

        var chatGroup = new Mock<IChatGroupGrain>();
        chatGroup.Setup(g => g.GetPartyIdAsync()).ReturnsAsync(Guid.NewGuid());
        chatGroup.Setup(g => g.GetScenarioAsync()).ReturnsAsync((string?)null);
        chatGroup.Setup(g => g.GetCastAsync()).ReturnsAsync(RoomCast.Create(
            cast, new HashSet<Guid> { Alice, Bob }, new Dictionary<Guid, DriverKind>(), ChatGroupKinds.Direct));
        chatGroup.Setup(g => g.GetMessagesUntilAsync(It.IsAny<int>(), It.IsAny<Guid?>())).ReturnsAsync(history);
        chatGroup.Setup(g => g.CountTrailingAssistantMessagesAsync()).ReturnsAsync(UrgeMath.CountExchangeRounds(history));
        chatGroup.Setup(g => g.RecordSkippedTurnAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<double>(), It.IsAny<string>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.BeginTurnAsync(It.IsAny<Guid>(), It.IsAny<int?>())).ReturnsAsync(999);
        chatGroup.Setup(g => g.EndTurnAsync(It.IsAny<int>(), It.IsAny<TurnOutcome>())).Returns(Task.CompletedTask);
        chatGroup.Setup(g => g.NotifyStreamChunkAsync(It.IsAny<int>(), It.IsAny<MessageStreamEvent>())).Returns(Task.CompletedTask);
        return chatGroup;
    }

    /// <summary>Alice hears the trigger; past the pre-gate the turn is cancelled, all that matters is whether it began.</summary>
    private static Task HandleAsync(Mock<IChatGroupGrain> chatGroup, ChatMessage trigger)
    {
        var pipeline = new ResponsePipeline(
            new Mock<IGrainFactory>().Object, new Mock<IMemoryRepository>().Object,
            NullLoggerFactory.Instance, NullLogger<ResponsePipeline>.Instance);
        return pipeline.HandleAsync(
            new Persona(Alice, "Alice", "You are Alice.", bio: null), Guid.NewGuid(), trigger,
            chatGroup.Object, new InFlightStore(), new CancellationToken(canceled: true));
    }
}
