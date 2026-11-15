using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

public class ChatGroupGrainTest
{
    private readonly Guid _partyId = Guid.NewGuid();
    private readonly Guid _chatGroupId = Guid.NewGuid();

    private ChatGroupState NewState(IEnumerable<Guid>? participantIds = null)
    {
        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = _partyId,
            ParticipantIds = participantIds is null
                ? new HashSet<Guid>()
                : new HashSet<Guid>(participantIds)
        });
        return state;
    }

    private int ReserveSlot(ChatGroupState state, Guid? senderId = null, string senderType = "assistant")
    {
        state.Apply(new ChatGroupMessageSlotReservedEvent
        {
            ChatGroupId = _chatGroupId,
            SenderId = senderId,
            SenderType = senderType
        });
        return state.NextMessageId;
    }

    [Fact]
    public void Apply_InitializedEvent_SetsPartyIdAndMarkInitialized()
    {
        var state = new ChatGroupState();
        Assert.False(state.Initialized);

        var alice = Guid.NewGuid();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = _partyId,
            ParticipantIds = new HashSet<Guid> { alice }
        });

        Assert.True(state.Initialized);
        Assert.Equal(_partyId, state.PartyId);
        Assert.Single(state.ParticipantIds);
        Assert.Contains(alice, state.ParticipantIds);
    }

    [Fact]
    public void Apply_InitializedEvent_PopulatesScenarioWhenProvided()
    {
        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = _partyId,
            ParticipantIds = new HashSet<Guid>(),
            Scenario = "Office of a stealth horticulture startup."
        });

        Assert.Equal("Office of a stealth horticulture startup.", state.Scenario);
    }

    [Fact]
    public void Apply_ScenarioSetEvent_UpdatesOnlyScenario()
    {
        var existing = Guid.NewGuid();
        var state = NewState([existing]);
        state.Apply(new ChatGroupScenarioSetEvent { Scenario = "After-hours bar." });

        Assert.Equal("After-hours bar.", state.Scenario);
        Assert.Equal(_partyId, state.PartyId);
        Assert.Single(state.ParticipantIds);
        Assert.Contains(existing, state.ParticipantIds);
    }

    [Fact]
    public void Apply_ScenarioSetEvent_NullClearsExistingScenario()
    {
        var state = NewState();
        state.Apply(new ChatGroupScenarioSetEvent { Scenario = "Previous setting." });
        Assert.Equal("Previous setting.", state.Scenario);

        state.Apply(new ChatGroupScenarioSetEvent { Scenario = null });

        Assert.Null(state.Scenario);
    }

    [Fact]
    public void Apply_SlotReserved_IncrementsNextMessageIdMonotonically()
    {
        var state = NewState();

        var id1 = ReserveSlot(state);
        var id2 = ReserveSlot(state);
        var id3 = ReserveSlot(state);

        Assert.Equal(1, id1);
        Assert.Equal(2, id2);
        Assert.Equal(3, id3);
    }

    [Fact]
    public void Apply_SlotReserved_CreatesEmptyStubWithCorrectSenderType()
    {
        var senderId = Guid.NewGuid();
        var state = NewState();

        ReserveSlot(state, senderId, "assistant");

        var stub = Assert.Single(state.Messages);
        Assert.Equal(string.Empty, stub.Content);
        Assert.Equal("assistant", stub.SenderType);
        Assert.Equal(senderId, stub.SenderId);
        Assert.Equal(_chatGroupId, stub.ChatGroupId);
    }

    [Fact]
    public void Apply_GenerationCompleted_FillsReservedSlotWithAllFields()
    {
        var senderId = Guid.NewGuid();
        var state = NewState();
        var messageId = ReserveSlot(state, senderId);

        state.Apply(new ChatGroupGenerationCompletedEvent
        {
            MessageId = messageId,
            Content = "Hello world",
            Reasoning = "internal reasoning",
            Appraisal = "sounded natural",
            SenderId = senderId,
            SendAt = 1234567890L,
            Metadata = new ChatMessageMetadata { Provider = "ollama", ModelName = "qwen2.5:14b" }
        });

        var msg = Assert.Single(state.Messages);
        Assert.Equal("Hello world", msg.Content);
        Assert.Equal("internal reasoning", msg.Reasoning);
        Assert.Equal("sounded natural", msg.Appraisal);
        Assert.Equal(senderId, msg.SenderId);
        Assert.Equal(1234567890L, msg.SendAt);
        Assert.Equal("ollama", msg.Metadata?.Provider);
        Assert.Equal("qwen2.5:14b", msg.Metadata?.ModelName);
    }

    [Fact]
    public void Apply_GenerationCompleted_LegacyEventWithoutMetadata_LeavesMetadataNull()
    {
        var senderId = Guid.NewGuid();
        var state = NewState();
        var messageId = ReserveSlot(state, senderId);

        state.Apply(new ChatGroupGenerationCompletedEvent
        {
            MessageId = messageId,
            Content = "Pre-papertrail message",
            SenderId = senderId,
            SendAt = 1L
        });

        var msg = Assert.Single(state.Messages);
        Assert.Null(msg.Metadata);
    }

    [Fact]
    public void Apply_GenerationCompleted_OnUnknownMessageId_IsNoOp()
    {
        var state = NewState();
        ReserveSlot(state);

        state.Apply(new ChatGroupGenerationCompletedEvent
        {
            MessageId = 99,
            Content = "orphaned content",
            SenderId = Guid.NewGuid(),
            SendAt = 0
        });

        Assert.Single(state.Messages);
        Assert.Equal(string.Empty, state.Messages[0].Content);
    }

    [Fact]
    public void Apply_GenerationStopped_PreservesSenderIdAndSetsAppraisal()
    {
        var senderId = Guid.NewGuid();
        var state = NewState();
        var messageId = ReserveSlot(state, senderId);

        state.Apply(new ChatGroupGenerationStoppedEvent
        {
            MessageId = messageId,
            Appraisal = "nothing worth saying",
            SendAt = 9999L
        });

        var msg = Assert.Single(state.Messages);
        Assert.Equal(senderId, msg.SenderId);
        Assert.Equal("nothing worth saying", msg.Appraisal);
    }

    [Fact]
    public void Apply_GenerationFailed_SetsErrorFieldOnMessage()
    {
        var state = NewState();
        var messageId = ReserveSlot(state);

        state.Apply(new ChatGroupGenerationFailedEvent
        {
            MessageId = messageId,
            Error = "upstream provider timeout",
            SendAt = 0
        });

        var msg = Assert.Single(state.Messages);
        Assert.Equal("upstream provider timeout", msg.Error);
    }

    [Fact]
    public void Apply_MessageDeleted_RemovesOnlyTargetMessage()
    {
        var state = NewState();
        var id1 = ReserveSlot(state);
        var id2 = ReserveSlot(state);
        var id3 = ReserveSlot(state);

        state.Apply(new ChatGroupMessageDeletedEvent { MessageId = id2 });

        Assert.Equal(2, state.Messages.Count);
        Assert.Contains(state.Messages, m => m.MessageId == id1);
        Assert.Contains(state.Messages, m => m.MessageId == id3);
        Assert.DoesNotContain(state.Messages, m => m.MessageId == id2);
    }

    [Fact]
    public void Apply_MessagesAfterDeleted_TrimsLogAndResetsNextMessageId()
    {
        var state = NewState();
        for (var i = 0; i < 5; i++)
            ReserveSlot(state);

        state.Apply(new ChatGroupMessagesAfterDeletedEvent { MessageId = 2 });

        Assert.Equal(2, state.Messages.Count);
        Assert.All(state.Messages, m => Assert.True(m.MessageId <= 2));
        Assert.Equal(2, state.NextMessageId);

        var nextId = ReserveSlot(state);
        Assert.Equal(3, nextId);
    }

    [Fact]
    public void Apply_MessagesAfterDeleted_WhenAllDeleted_NextMessageIdResetsToZero()
    {
        var state = NewState();
        ReserveSlot(state);
        ReserveSlot(state);

        state.Apply(new ChatGroupMessagesAfterDeletedEvent { MessageId = 0 });

        Assert.Empty(state.Messages);
        Assert.Equal(0, state.NextMessageId);

        var nextId = ReserveSlot(state);
        Assert.Equal(1, nextId);
    }

    [Fact]
    public void Apply_ParticipantIdsSet_ReplacesEntireSet()
    {
        var aliceId = Guid.NewGuid();
        var charlieId = Guid.NewGuid();
        var danaId = Guid.NewGuid();
        var state = NewState([aliceId]);

        state.Apply(new ChatGroupParticipantIdsSetEvent
        {
            ParticipantIds = new HashSet<Guid> { charlieId, danaId }
        });

        Assert.Equal(2, state.ParticipantIds.Count);
        Assert.Contains(charlieId, state.ParticipantIds);
        Assert.Contains(danaId, state.ParticipantIds);
        Assert.DoesNotContain(aliceId, state.ParticipantIds);
    }

    [Fact]
    public void Apply_DriverOverrideSet_AddsEntryToMap()
    {
        var personaId = Guid.NewGuid();
        var state = NewState([personaId]);

        state.Apply(new ChatGroupDriverOverrideSetEvent { PersonaId = personaId, Kind = DriverKind.User });

        Assert.Single(state.DriverOverrides);
        Assert.Equal(DriverKind.User, state.DriverOverrides[personaId]);
    }

    [Fact]
    public void Apply_DriverOverrideSet_TwiceOnSamePersona_OverwritesValue()
    {
        var personaId = Guid.NewGuid();
        var state = NewState([personaId]);

        state.Apply(new ChatGroupDriverOverrideSetEvent { PersonaId = personaId, Kind = DriverKind.User });
        state.Apply(new ChatGroupDriverOverrideSetEvent { PersonaId = personaId, Kind = DriverKind.LLM });

        Assert.Equal(DriverKind.LLM, state.DriverOverrides[personaId]);
    }

    [Fact]
    public void Apply_DriverOverrideCleared_RemovesEntry()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var state = NewState([a, b]);
        state.Apply(new ChatGroupDriverOverrideSetEvent { PersonaId = a, Kind = DriverKind.User });
        state.Apply(new ChatGroupDriverOverrideSetEvent { PersonaId = b, Kind = DriverKind.User });

        state.Apply(new ChatGroupDriverOverrideClearedEvent { PersonaId = a });

        Assert.Single(state.DriverOverrides);
        Assert.False(state.DriverOverrides.ContainsKey(a));
        Assert.Equal(DriverKind.User, state.DriverOverrides[b]);
    }

    [Fact]
    public void Apply_DriverOverrideCleared_OnUnknownPersona_IsNoOp()
    {
        var state = NewState();
        state.Apply(new ChatGroupDriverOverrideClearedEvent { PersonaId = Guid.NewGuid() });
        Assert.Empty(state.DriverOverrides);
    }

    [Fact]
    public void Apply_UserMessageEvent_InsertsMessageByIdWhenNotPresent()
    {
        var senderId = Guid.NewGuid();
        var state = NewState();

        var message = new ChatMessage
        {
            MessageId = 1,
            SenderId = senderId,
            SenderType = "user",
            Content = "Hello!",
            ChatGroupId = _chatGroupId,
            SendAt = 100L
        };

        state.Apply(new ChatGroupUserMessageEvent { SenderId = senderId, Message = message });

        var msg = Assert.Single(state.Messages);
        Assert.Equal("Hello!", msg.Content);
        Assert.Equal("user", msg.SenderType);
        Assert.Equal(Math.Max(0, 1), state.NextMessageId);
    }

    [Fact]
    public void Apply_UserMessageEvent_UpdatesExistingSlotWhenIdMatches()
    {
        var senderId = Guid.NewGuid();
        var state = NewState();
        var messageId = ReserveSlot(state, senderId, "user");

        var message = new ChatMessage
        {
            MessageId = messageId,
            SenderId = senderId,
            SenderType = "user",
            Content = "The real message",
            ChatGroupId = _chatGroupId,
            SendAt = 200L
        };

        state.Apply(new ChatGroupUserMessageEvent { SenderId = senderId, Message = message });

        var msg = Assert.Single(state.Messages);
        Assert.Equal("The real message", msg.Content);
    }

    [Fact]
    public void Messages_OrderedByMessageId_ReturnsAscendingOrder()
    {
        var state = NewState();
        ReserveSlot(state);
        ReserveSlot(state);
        ReserveSlot(state);

        var ordered = state.Messages.OrderBy(m => m.MessageId).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(m => m.MessageId));
    }

    [Fact]
    public void CountTrailingAssistantMessages_IgnoresEmptyStubs()
    {
        var senderId = Guid.NewGuid();
        var state = NewState();

        var id1 = ReserveSlot(state, senderId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id1, Content = "Response 1", SenderId = senderId, SendAt = 1 });

        var id2 = ReserveSlot(state, senderId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id2, Content = "Response 2", SenderId = senderId, SendAt = 2 });

        ReserveSlot(state, senderId);

        var count = CountTrailingAssistant(state);
        Assert.Equal(2, count);
    }

    [Fact]
    public void CountTrailingAssistantMessages_StopsAtFirstUserMessage()
    {
        var userId = Guid.NewGuid();
        var personaId = Guid.NewGuid();
        var state = NewState();

        ReserveSlot(state, userId, "user");
        state.Apply(new ChatGroupUserMessageEvent
        {
            SenderId = userId,
            Message = new ChatMessage { MessageId = 1, SenderId = userId, SenderType = "user", Content = "Hello", ChatGroupId = _chatGroupId }
        });

        var id2 = ReserveSlot(state, personaId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id2, Content = "R1", SenderId = personaId, SendAt = 2 });
        var id3 = ReserveSlot(state, personaId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id3, Content = "R2", SenderId = personaId, SendAt = 3 });

        var count = CountTrailingAssistant(state);
        Assert.Equal(2, count);
    }

    private static int CountTrailingAssistant(ChatGroupState state)
    {
        return PartyTown.Services.ResponsePipeline.UrgeMath.CountExchangeRounds(state.Messages);
    }
}
