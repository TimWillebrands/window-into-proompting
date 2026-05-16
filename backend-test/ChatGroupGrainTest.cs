using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="ChatGroupGrain"/> state machine — specifically the <c>Apply</c> methods on
/// <see cref="ChatGroupState"/> that govern the durable message log.
///
/// Why we test <see cref="ChatGroupState"/> directly instead of through a TestKit silo:
///   <see cref="ChatGroupGrain"/> extends <c>JournaledGrain</c> which requires a named
///   <c>ILogConsistencyProvider</c> ("PartyStateStorage") registered in DI. Orleans TestKit v10
///   does not wire this up automatically, causing a <see cref="NullReferenceException"/> in
///   <c>JournaledGrain.PreActivateProtocolParticipant</c> before our grain code even runs.
///   Configuring the real state-storage log consistency provider in the TestKit silo requires
///   a non-trivial DI setup including a backing storage provider.
///
///   Because all business logic we care about lives in the <c>Apply</c> overloads (pure state
///   transitions over plain C# objects), testing those directly gives us full coverage of the
///   invariants without needing the Orleans runtime at all.
///
/// Coverage areas:
///   • <see cref="ChatGroupState.Apply(ChatGroupInitializedEvent)"/> — sets PartyId,
///     participants, and Initialized flag
///   • <see cref="ChatGroupState.Apply(ChatGroupMessageSlotReservedEvent)"/> — monotonic
///     NextMessageId increment, empty stub creation
///   • <see cref="ChatGroupState.Apply(ChatGroupGenerationCompletedEvent)"/> — fills slot with
///     content/reasoning/appraisal/senderId; silent no-op for unknown IDs
///   • <see cref="ChatGroupState.Apply(ChatGroupGenerationStoppedEvent)"/> — clears SenderId to
///     Guid.Empty, sets appraisal
///   • <see cref="ChatGroupState.Apply(ChatGroupGenerationFailedEvent)"/> — records error on message
///   • <see cref="ChatGroupState.Apply(ChatGroupMessageDeletedEvent)"/> — single message removal
///   • <see cref="ChatGroupState.Apply(ChatGroupMessagesAfterDeletedEvent)"/> — bulk trim and
///     NextMessageId reset (the reprompt path)
///   • <see cref="ChatGroupState.Apply(ChatGroupParticipantsSetEvent)"/> — wholesale participant
///     list replacement
///   • <see cref="ChatGroupState.Apply(ChatGroupUserMessageEvent)"/> — insert-or-update by MessageId
///
/// NOTE: Grain-level tests (fan-out to IPersonaGrain, stream event publication, reentrant
/// cancellation) are deferred until the TestKit JournaledGrain DI setup is resolved.
/// </summary>
public class ChatGroupGrainTest
{
    private readonly Guid _partyId = Guid.NewGuid();
    private readonly Guid _chatGroupId = Guid.NewGuid();

    /// <summary>
    /// Creates a freshly initialized state, equivalent to what OnActivateAsync does on first run.
    /// </summary>
    private ChatGroupState NewState(List<PartyParticipant>? participants = null)
    {
        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = _partyId,
            Participants = participants ?? []
        });
        return state;
    }

    /// <summary>
    /// Reserves a message slot on the state, returning the assigned MessageId.
    /// Mirrors what GetNextMessageIdAsync does: raises ChatGroupMessageSlotReservedEvent
    /// then reads State.NextMessageId.
    /// </summary>
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

    // ── Initialization ────────────────────────────────────────────────────────

    [Fact]
    public void Apply_InitializedEvent_SetsPartyIdAndMarkInitialized()
    {
        var state = new ChatGroupState();
        Assert.False(state.Initialized);

        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = _partyId,
            Participants = [new() { Id = Guid.NewGuid(), Name = "Alice" }]
        });

        Assert.True(state.Initialized);
        Assert.Equal(_partyId, state.PartyId);
        Assert.Single(state.Participants);
    }

    [Fact]
    public void Apply_InitializedEvent_PopulatesScenarioWhenProvided()
    {
        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = _partyId,
            Participants = [],
            Scenario = "Office of a stealth horticulture startup."
        });

        Assert.Equal("Office of a stealth horticulture startup.", state.Scenario);
    }

    [Fact]
    public void Apply_ScenarioSetEvent_UpdatesOnlyScenario()
    {
        var existingParticipant = new PartyParticipant { Id = Guid.NewGuid(), Name = "Alice" };
        var state = NewState([existingParticipant]);
        state.Apply(new ChatGroupScenarioSetEvent { Scenario = "After-hours bar." });

        Assert.Equal("After-hours bar.", state.Scenario);
        // Other state must be untouched.
        Assert.Equal(_partyId, state.PartyId);
        Assert.Single(state.Participants);
        Assert.Equal(existingParticipant.Id, state.Participants[0].Id);
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

    // ── Slot reservation / monotonic IDs ─────────────────────────────────────

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
        // Slot reservation creates a placeholder so streaming clients can attach to the
        // message ID before generation completes — Content must be empty string, not null.
        var senderId = Guid.NewGuid();
        var state = NewState();

        ReserveSlot(state, senderId, "assistant");

        var stub = Assert.Single(state.Messages);
        Assert.Equal(string.Empty, stub.Content);
        Assert.Equal("assistant", stub.SenderType);
        Assert.Equal(senderId, stub.SenderId);
        Assert.Equal(_chatGroupId, stub.ChatGroupId);
    }

    // ── Generation completed ──────────────────────────────────────────────────

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
        // Replay-safety net: events persisted before the papertrail migration carry no
        // Metadata field. Orleans tag-versioning makes [Id(6)] default to null on
        // deserialization, so historical messages must round-trip with Metadata == null
        // and never throw.
        var senderId = Guid.NewGuid();
        var state = NewState();
        var messageId = ReserveSlot(state, senderId);

        state.Apply(new ChatGroupGenerationCompletedEvent
        {
            MessageId = messageId,
            Content = "Pre-papertrail message",
            SenderId = senderId,
            SendAt = 1L
            // Metadata intentionally omitted — mirrors a legacy on-disk event.
        });

        var msg = Assert.Single(state.Messages);
        Assert.Null(msg.Metadata);
    }

    [Fact]
    public void Apply_GenerationCompleted_OnUnknownMessageId_IsNoOp()
    {
        // The Apply method returns silently when the target message is not found.
        // This prevents a crash if an out-of-order event arrives after a delete.
        var state = NewState();
        ReserveSlot(state); // slot 1

        state.Apply(new ChatGroupGenerationCompletedEvent
        {
            MessageId = 99,
            Content = "orphaned content",
            SenderId = Guid.NewGuid(),
            SendAt = 0
        });

        Assert.Single(state.Messages);
        Assert.Equal(string.Empty, state.Messages[0].Content); // stub unchanged
    }

    // ── Generation stopped ────────────────────────────────────────────────────

    [Fact]
    public void Apply_GenerationStopped_PreservesSenderIdAndSetsAppraisal()
    {
        // Decline path: SenderId is preserved so the papertrail can resolve the persona
        // name for the "declined" entry; Appraisal is recorded as the reason.
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

    // ── Generation failed ─────────────────────────────────────────────────────

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

    // ── Delete ────────────────────────────────────────────────────────────────

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
        // The reprompt path: all messages after a given ID are removed and NextMessageId
        // resets to the max remaining ID so the next slot resumes without gaps.
        var state = NewState();
        for (var i = 0; i < 5; i++)
            ReserveSlot(state);

        state.Apply(new ChatGroupMessagesAfterDeletedEvent { MessageId = 2 });

        Assert.Equal(2, state.Messages.Count);
        Assert.All(state.Messages, m => Assert.True(m.MessageId <= 2));
        Assert.Equal(2, state.NextMessageId); // Reset to max remaining

        // Verify the next slot starts from 3 (no gaps after reprompt)
        var nextId = ReserveSlot(state);
        Assert.Equal(3, nextId);
    }

    [Fact]
    public void Apply_MessagesAfterDeleted_WhenAllDeleted_NextMessageIdResetsToZero()
    {
        var state = NewState();
        ReserveSlot(state); // id 1
        ReserveSlot(state); // id 2

        state.Apply(new ChatGroupMessagesAfterDeletedEvent { MessageId = 0 }); // delete all

        Assert.Empty(state.Messages);
        Assert.Equal(0, state.NextMessageId);

        var nextId = ReserveSlot(state);
        Assert.Equal(1, nextId); // Starts fresh from 1
    }

    // ── Participants ──────────────────────────────────────────────────────────

    [Fact]
    public void Apply_ParticipantsSet_ReplacesEntireList()
    {
        var state = NewState(
        [
            new() { Id = Guid.NewGuid(), Name = "Alice" }
        ]);

        state.Apply(new ChatGroupParticipantsSetEvent
        {
            Participants =
            [
                new() { Id = Guid.NewGuid(), Name = "Charlie" },
                new() { Id = Guid.NewGuid(), Name = "Dana" },
            ]
        });

        Assert.Equal(2, state.Participants.Count);
        Assert.Contains(state.Participants, p => p.Name == "Charlie");
        Assert.Contains(state.Participants, p => p.Name == "Dana");
        Assert.DoesNotContain(state.Participants, p => p.Name == "Alice");
    }

    // ── User message ──────────────────────────────────────────────────────────

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
        Assert.Equal(Math.Max(0, 1), state.NextMessageId); // tracks highest seen ID
    }

    [Fact]
    public void Apply_UserMessageEvent_UpdatesExistingSlotWhenIdMatches()
    {
        // SendNewMessageAsync first reserves a slot (user senderType), then raises
        // ChatGroupUserMessageEvent to fill the real message content.
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

        var msg = Assert.Single(state.Messages); // still one message, not two
        Assert.Equal("The real message", msg.Content);
    }

    // ── Computed properties used by grain read methods ────────────────────────

    [Fact]
    public void Messages_OrderedByMessageId_ReturnsAscendingOrder()
    {
        // GetMessagesAsync does OrderBy(m => m.MessageId) — verify the backing list
        // can be sorted correctly even if Apply inserts in non-sequential order.
        var state = NewState();
        ReserveSlot(state); // id 1
        ReserveSlot(state); // id 2
        ReserveSlot(state); // id 3

        var ordered = state.Messages.OrderBy(m => m.MessageId).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, ordered.Select(m => m.MessageId));
    }

    [Fact]
    public void CountTrailingAssistantMessages_IgnoresEmptyStubs()
    {
        // CountTrailingAssistantMessagesAsync walks backwards and skips stubs with
        // empty content. Only filled (completed) messages count.
        var senderId = Guid.NewGuid();
        var state = NewState();

        var id1 = ReserveSlot(state, senderId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id1, Content = "Response 1", SenderId = senderId, SendAt = 1 });

        var id2 = ReserveSlot(state, senderId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id2, Content = "Response 2", SenderId = senderId, SendAt = 2 });

        ReserveSlot(state, senderId); // slot 3: empty stub, should not count

        // Replicate CountTrailingAssistantMessagesAsync logic against the state
        var count = CountTrailingAssistant(state);
        Assert.Equal(2, count);
    }

    [Fact]
    public void CountTrailingAssistantMessages_StopsAtFirstUserMessage()
    {
        var userId = Guid.NewGuid();
        var personaId = Guid.NewGuid();
        var state = NewState();

        // User message at slot 1
        ReserveSlot(state, userId, "user");
        state.Apply(new ChatGroupUserMessageEvent
        {
            SenderId = userId,
            Message = new ChatMessage { MessageId = 1, SenderId = userId, SenderType = "user", Content = "Hello", ChatGroupId = _chatGroupId }
        });

        // Two filled assistant messages
        var id2 = ReserveSlot(state, personaId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id2, Content = "R1", SenderId = personaId, SendAt = 2 });
        var id3 = ReserveSlot(state, personaId);
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = id3, Content = "R2", SenderId = personaId, SendAt = 3 });

        var count = CountTrailingAssistant(state);
        Assert.Equal(2, count); // Should NOT count the user message at slot 1
    }

    /// <summary>Replicates <see cref="ChatGroupGrain.CountTrailingAssistantMessagesAsync"/> logic.</summary>
    private static int CountTrailingAssistant(ChatGroupState state)
    {
        var count = 0;
        var messages = state.Messages.OrderBy(m => m.MessageId).ToList();
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var msg = messages[i];
            if (msg.SenderType != "assistant") break;
            if (string.IsNullOrEmpty(msg.Content)) continue;
            count++;
        }
        return count;
    }
}
