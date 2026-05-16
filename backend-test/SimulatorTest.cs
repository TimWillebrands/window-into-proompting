using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>
/// Deterministic simulation tests inspired by TigerBeetle's VOPR fuzzer.
///
/// Philosophy:
///   Instead of hand-crafted examples, a seeded <see cref="Random"/> drives random
///   interleavings of user messages, persona decisions, and endpoint faults.  After
///   every operation, a set of invariants is checked against the state.  Failures
///   include the seed so they replay deterministically.
///
/// Invariants checked after every step:
///   1. <c>NextMessageId</c> is monotonically non-decreasing (never goes backwards).
///   2. Message IDs in the log are unique.
///   3. Participants list is never null.
///
/// Scope: exercises <see cref="ChatGroupState.Apply"/> overloads directly — no Orleans
/// silo needed.  The "personas" here are pure C# lambdas that call Apply in a scripted
/// pattern (respond / decline / fail / hang-then-cancel).
///
/// End-to-end loop tests (involving the real <c>ChatGroupGrain</c> grain) live in
/// <c>ChatGroupFanoutTest.cs</c> and are currently skipped pending JournaledGrain DI.
/// </summary>
public class SimulatorTest
{
    // ── Invariant helpers ─────────────────────────────────────────────────────

    /// <summary>Checks all invariants; returns a description of any violation.</summary>
    private static string? CheckInvariants(ChatGroupState state, int previousNextMessageId)
    {
        // 1. Monotonic NextMessageId
        if (state.NextMessageId < previousNextMessageId)
            return $"NextMessageId went backwards: {previousNextMessageId} → {state.NextMessageId}";

        // 4. Unique message IDs
        var ids = state.Messages.Select(m => m.MessageId).ToList();
        var distinctIds = ids.Distinct().ToList();
        if (ids.Count != distinctIds.Count)
            return $"Duplicate message IDs in log: [{string.Join(", ", ids)}]";

        // 5. Participants list not null
        if (state.Participants is null)
            return "Participants list is null";

        return null;
    }

    private static void AssertInvariant(ChatGroupState state, int prevId, int seed)
    {
        var violation = CheckInvariants(state, prevId);
        Assert.True(violation is null,
            $"[seed={seed}] Invariant violated: {violation}");
    }

    // ── Fixed scenario: all personas respond ──────────────────────────────────

    [Fact]
    public void Scenario_AllPersonasRespond_MonotonicIds()
    {
        var partyId = Guid.NewGuid();
        var chatGroupId = Guid.NewGuid();
        var alice = Guid.NewGuid();
        var bob   = Guid.NewGuid();

        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = partyId,
            Participants =
            [
                new() { Id = alice, Name = "Alice" },
                new() { Id = bob,   Name = "Bob" },
            ]
        });

        var prevId = state.NextMessageId;

        // User sends a message
        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = Guid.NewGuid(), SenderType = "user" });
        AssertInvariant(state, prevId, seed: 0);
        prevId = state.NextMessageId;

        state.Apply(new ChatGroupUserMessageEvent
        {
            SenderId = Guid.NewGuid(),
            Message = new ChatMessage { MessageId = state.NextMessageId, SenderType = "user", Content = "hello", ChatGroupId = chatGroupId }
        });
        AssertInvariant(state, prevId, seed: 0);

        // Alice reserves a slot
        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = alice, SenderType = "assistant" });
        prevId = state.NextMessageId;
        var aliceSlot = state.NextMessageId;
        AssertInvariant(state, prevId, seed: 0);

        // Bob reserves a slot
        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = bob, SenderType = "assistant" });
        prevId = state.NextMessageId;
        var bobSlot = state.NextMessageId;
        AssertInvariant(state, prevId, seed: 0);

        // Alice responds
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = aliceSlot, Content = "Hi there!", SenderId = alice, SendAt = 1L });
        AssertInvariant(state, prevId, seed: 0);

        // Bob responds
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = bobSlot, Content = "Hey!", SenderId = bob, SendAt = 2L });
        AssertInvariant(state, prevId, seed: 0);

        Assert.Equal(3, state.Messages.Count); // user + alice + bob
    }

    [Fact]
    public void Scenario_RepromptPath_TrimsLogAndResetsIdMonotonically()
    {
        var state = InitTwoPersonaState(out var alice, out var bob, out var chatGroupId);

        // Build up 5 messages
        for (var i = 0; i < 5; i++)
        {
            var prevId = state.NextMessageId;
            state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = alice, SenderType = "assistant" });
            state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = state.NextMessageId, Content = $"msg{i}", SenderId = alice, SendAt = i });
            AssertInvariant(state, prevId, seed: 0);
        }

        Assert.Equal(5, state.Messages.Count);

        // Reprompt from message 3: delete messages 4 and 5
        var beforeReprompt = state.NextMessageId;
        state.Apply(new ChatGroupMessagesAfterDeletedEvent { MessageId = 3 });
        Assert.Equal(3, state.Messages.Count);
        Assert.True(state.NextMessageId <= beforeReprompt,
            "NextMessageId should decrease or stay the same after trim");

        // After reprompt, the NEXT slot must be > max remaining id (monotonic going forward)
        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = alice, SenderType = "assistant" });
        Assert.Equal(state.NextMessageId, state.Messages.Max(m => m.MessageId));
        Assert.True(state.NextMessageId > 3, "Post-reprompt slots must advance beyond the trim point");
    }

    // ── Seeded fuzz ───────────────────────────────────────────────────────────

    // 50 random seeds. Each drives a sequence of user messages + random persona outcomes
    // (respond / decline / fail) and checks all invariants after every event application.
    public static TheoryData<int> FuzzSeeds()
    {
        var data = new TheoryData<int>();
        for (var i = 1; i <= 50; i++) data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(FuzzSeeds))]
    public void Fuzz_RandomMessagesAndOutcomes_PreservesAllInvariants(int seed)
    {
        var rng = new Random(seed);

        var partyId = Guid.NewGuid();
        var chatGroupId = Guid.NewGuid();
        var personas = Enumerable.Range(0, rng.Next(1, 5))
            .Select(_ => Guid.NewGuid())
            .ToList();

        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = partyId,
            Participants = personas.Select(id => new PartyParticipant { Id = id, Name = id.ToString("N")[..4] }).ToList()
        });

        var prevNextId = 0;

        for (var round = 0; round < 20; round++)
        {
            // User sends a message
            var userId = Guid.NewGuid();
            state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = userId, SenderType = "user" });
            var userMsgId = state.NextMessageId;
            state.Apply(new ChatGroupUserMessageEvent
            {
                SenderId = userId,
                Message = new ChatMessage { MessageId = userMsgId, SenderType = "user", Content = $"msg{round}", ChatGroupId = chatGroupId }
            });
            AssertInvariant(state, prevNextId, seed);
            prevNextId = state.NextMessageId;

            // Each persona independently decides: respond, decline, fail, or skip
            var slots = new List<(Guid persona, int slot)>();
            foreach (var personaId in personas)
            {
                // Random chance each persona bothers to reserve a slot at all
                if (rng.NextDouble() > 0.2)
                {
                    state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = personaId, SenderType = "assistant" });
                    slots.Add((personaId, state.NextMessageId));
                    AssertInvariant(state, prevNextId, seed);
                    prevNextId = state.NextMessageId;
                }
            }

            // Resolve all slots — shuffle order to test any interleaving
            var shuffled = slots.OrderBy(_ => rng.Next()).ToList();
            foreach (var (personaId, slotId) in shuffled)
            {
                var outcome = rng.Next(3);
                switch (outcome)
                {
                    case 0: // respond
                        state.Apply(new ChatGroupGenerationCompletedEvent
                        {
                            MessageId = slotId,
                            Content = $"response from {personaId:N}",
                            SenderId = personaId,
                            SendAt = round * 100L + slotId
                        });
                        break;
                    case 1: // decline
                        state.Apply(new ChatGroupGenerationStoppedEvent
                        {
                            MessageId = slotId,
                            Appraisal = "silent",
                            SendAt = round * 100L + slotId
                        });
                        break;
                    case 2: // fail
                        state.Apply(new ChatGroupGenerationFailedEvent
                        {
                            MessageId = slotId,
                            Error = "endpoint error",
                            SendAt = round * 100L + slotId
                        });
                        break;
                }
                AssertInvariant(state, prevNextId, seed);
            }

            // Occasionally simulate a reprompt (delete messages after random point)
            if (state.Messages.Count > 3 && rng.NextDouble() < 0.15)
            {
                var trimPoint = rng.Next(1, state.Messages.Count);
                var trimId = state.Messages.OrderBy(m => m.MessageId).ElementAt(trimPoint - 1).MessageId;
                var beforeTrim = state.NextMessageId;
                state.Apply(new ChatGroupMessagesAfterDeletedEvent { MessageId = trimId });
                Assert.True(state.NextMessageId <= beforeTrim,
                    $"[seed={seed}] NextMessageId went UP after reprompt trim: {beforeTrim} → {state.NextMessageId}");
                AssertInvariant(state, 0 /* allow reset */, seed);
                prevNextId = state.NextMessageId;
            }
        }
    }

    // ── Invariant: exactly one terminal event per slot ────────────────────────

    [Fact]
    public void Invariant_ExactlyOneTerminalEventPerSlot()
    {
        // Once a slot has a terminal event (content/error/stopped), applying another
        // terminal event to the same slot must not corrupt state.
        var state = InitTwoPersonaState(out var alice, out _, out var chatGroupId);

        state.Apply(new ChatGroupMessageSlotReservedEvent { ChatGroupId = chatGroupId, SenderId = alice, SenderType = "assistant" });
        var slotId = state.NextMessageId;

        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = slotId, Content = "first", SenderId = alice, SendAt = 1 });
        Assert.Equal("first", state.Messages.First(m => m.MessageId == slotId).Content);

        // Applying a second completion overwrites (the real system prevents this through grain
        // state, but the state machine itself should not panic)
        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = slotId, Content = "second", SenderId = alice, SendAt = 2 });
        Assert.Equal("second", state.Messages.First(m => m.MessageId == slotId).Content);

        Assert.Single(state.Messages, m => m.MessageId == slotId);
    }

    [Fact]
    public void Invariant_UnknownMessageId_IsNoOpForAllTerminalEvents()
    {
        // Terminal events for unknown message IDs must be silent no-ops — not exceptions.
        // This handles out-of-order events after a reprompt delete.
        var state = InitTwoPersonaState(out var alice, out _, out var chatGroupId);
        var initialCount = state.Messages.Count;

        state.Apply(new ChatGroupGenerationCompletedEvent { MessageId = 999, Content = "orphan", SenderId = alice, SendAt = 1 });
        state.Apply(new ChatGroupGenerationStoppedEvent   { MessageId = 999, Appraisal = "orphan", SendAt = 1 });
        state.Apply(new ChatGroupGenerationFailedEvent    { MessageId = 999, Error = "orphan", SendAt = 1 });

        Assert.Equal(initialCount, state.Messages.Count);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ChatGroupState InitTwoPersonaState(
        out Guid alice, out Guid bob, out Guid chatGroupId)
    {
        alice = Guid.NewGuid();
        bob   = Guid.NewGuid();
        chatGroupId = Guid.NewGuid();

        var state = new ChatGroupState();
        state.Apply(new ChatGroupInitializedEvent
        {
            PartyId = Guid.NewGuid(),
            Participants =
            [
                new() { Id = alice, Name = "Alice" },
                new() { Id = bob,   Name = "Bob" },
            ]
        });
        return state;
    }
}
