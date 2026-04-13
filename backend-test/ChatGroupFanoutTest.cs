using BackendTest.Infrastructure;
using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>
/// Grain-level tests for <see cref="ChatGroupGrain"/> fanout, running against a real
/// Orleans <see cref="Orleans.TestingHost.TestCluster"/>. Calls to <c>IPersonaGrain</c>
/// are intercepted by <see cref="FanoutInterceptor"/> — the real persona body never
/// executes, so these tests are pure fanout assertions (not end-to-end feedback loop).
/// End-to-end loop coverage lives in <c>PersonaGrainFeedbackTest</c> and
/// <c>SimulatorTest</c>.
///
/// Invariants asserted:
///   • Fanout count = |participants| × triggering messages (no drops, no dupes)
///   • Every participant is notified with the triggering message id
///   • Non-reentrant ChatGroupGrain does not deadlock under concurrent sends
/// </summary>
public class ChatGroupFanoutTest : IClassFixture<ChatGroupClusterFixture>
{
    private readonly ChatGroupClusterFixture _fx;

    public ChatGroupFanoutTest(ChatGroupClusterFixture fx)
    {
        _fx = fx;
        FanoutInterceptor.Reset();
    }

    /// <summary>
    /// Fanout from ChatGroupGrain is fire-and-forget — the notify calls may not have
    /// reached the incoming filter by the time <c>SendNewMessageAsync</c> returns.
    /// Poll with a bounded timeout so the test is deterministic without arbitrary sleeps.
    /// </summary>
    private static async Task WaitForFanoutAsync(int expected, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var n = FanoutInterceptor.Calls.Count(c => c.Method == nameof(IPersonaGrain.NotifyMessageAsync));
            if (n >= expected) return;
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task SendNewMessage_FansOutToEveryParticipantExactlyOnce()
    {
        var (_, chatGroupId, participants) = await _fx.CreatePartyWithChatGroupAsync("Alice", "Bob", "Carol");
        var chatGroup = _fx.GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);

        await chatGroup.SendNewMessageAsync(Guid.NewGuid(), "Hello everyone!", null, null);
        await WaitForFanoutAsync(participants.Count);

        var calls = FanoutInterceptor.Calls
            .Where(c => c.Method == nameof(IPersonaGrain.NotifyMessageAsync))
            .ToList();
        Assert.Equal(participants.Count, calls.Count);
        foreach (var p in participants)
            Assert.Contains(calls, c => c.PersonaId == p.Id && c.ChatGroupId == chatGroupId);
    }

    [Fact]
    public async Task SendMultipleMessages_FansOutOncePerMessagePerParticipant()
    {
        var (_, chatGroupId, participants) = await _fx.CreatePartyWithChatGroupAsync("Alice", "Bob");
        var chatGroup = _fx.GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);

        await chatGroup.SendNewMessageAsync(Guid.NewGuid(), "first", null, null);
        await chatGroup.SendNewMessageAsync(Guid.NewGuid(), "second", null, null);
        await chatGroup.SendNewMessageAsync(Guid.NewGuid(), "third", null, null);
        await WaitForFanoutAsync(participants.Count * 3);

        var calls = FanoutInterceptor.Calls
            .Where(c => c.Method == nameof(IPersonaGrain.NotifyMessageAsync))
            .ToList();
        Assert.Equal(participants.Count * 3, calls.Count);
        foreach (var p in participants)
            Assert.Equal(3, calls.Count(c => c.PersonaId == p.Id));
    }

    [Fact]
    public async Task AppendMessage_FromAssistant_ReFansOutToAllParticipants()
    {
        // Desired: when a persona completes (AppendMessageAsync path), ChatGroupGrain
        // re-fans out so the OTHER personas can evaluate the new assistant message.
        // This is the feedback loop; every append should produce another round of fanout.
        var (_, chatGroupId, participants) = await _fx.CreatePartyWithChatGroupAsync("Alice", "Bob");
        var chatGroup = _fx.GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);

        await chatGroup.SendNewMessageAsync(Guid.NewGuid(), "kick off", null, null);
        await WaitForFanoutAsync(participants.Count);
        var initialCount = FanoutInterceptor.Calls.Count;

        // Simulate Alice responding: reserve a slot, then append.
        var alice = participants[0];
        var slotId = await chatGroup.GetNextMessageIdAsync(alice.Id, "assistant");
        await chatGroup.AppendMessageAsync(slotId, "Alice says hi", null, null);
        await WaitForFanoutAsync(initialCount + 1);

        var total = FanoutInterceptor.Calls.Count;
        Assert.True(total > initialCount,
            $"Expected re-fanout after AppendMessageAsync, got {total - initialCount} new calls (initial={initialCount}, total={total})");
    }

    [Fact]
    public async Task ConcurrentSends_NonReentrantChatGroup_DoesNotDeadlock()
    {
        var (_, chatGroupId, participants) = await _fx.CreatePartyWithChatGroupAsync("A", "B", "C", "D", "E");
        var chatGroup = _fx.GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sends = Enumerable.Range(0, 5)
            .Select(i => chatGroup.SendNewMessageAsync(Guid.NewGuid(), $"msg{i}", null, null, cts.Token))
            .ToArray();
        await Task.WhenAll(sends);
        await WaitForFanoutAsync(participants.Count * 5);

        var calls = FanoutInterceptor.Calls
            .Where(c => c.Method == nameof(IPersonaGrain.NotifyMessageAsync))
            .ToList();
        Assert.Equal(participants.Count * 5, calls.Count);
    }

    [Fact]
    public async Task Fanout_DeliversTriggeringMessageIdToEveryParticipant()
    {
        var (_, chatGroupId, participants) = await _fx.CreatePartyWithChatGroupAsync("Alice", "Bob");
        var chatGroup = _fx.GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);

        var userMsg = await chatGroup.SendNewMessageAsync(Guid.NewGuid(), "hello", null, null);
        await WaitForFanoutAsync(participants.Count);

        var calls = FanoutInterceptor.Calls
            .Where(c => c.Method == nameof(IPersonaGrain.NotifyMessageAsync))
            .ToList();
        Assert.All(calls, c => Assert.Equal(userMsg.MessageId, c.MessageId));
        Assert.Equal(participants.Count, calls.Select(c => c.PersonaId).Distinct().Count());
    }
}
