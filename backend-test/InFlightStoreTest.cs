using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

public class InFlightStoreTest
{
    [Fact]
    public void Register_AddsInFlightGeneration_SnapshotReturnsIt()
    {
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var messageId = 42;
        using var cts = new CancellationTokenSource();

        var gen = store.Register(chatGroupId, messageId, cts);

        var snapshot = store.SnapshotForChatGroup(chatGroupId);
        var entry = Assert.Single(snapshot);
        Assert.Equal(messageId, entry.messageId);
        Assert.Same(gen, entry.gen);
    }

    [Fact]
    public void Register_MultipleChatGroups_OnlyReturnsRequested()
    {
        var store = new InFlightStore();
        var groupA = Guid.NewGuid();
        var groupB = Guid.NewGuid();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();

        store.Register(groupA, 1, cts1);
        store.Register(groupB, 2, cts2);
        store.Register(groupA, 3, cts3);

        var snapshotA = store.SnapshotForChatGroup(groupA);
        Assert.Equal(2, snapshotA.Count);
        Assert.Contains(snapshotA, s => s.messageId == 1);
        Assert.Contains(snapshotA, s => s.messageId == 3);

        var snapshotB = store.SnapshotForChatGroup(groupB);
        var entryB = Assert.Single(snapshotB);
        Assert.Equal(2, entryB.messageId);
    }

    [Fact]
    public void Remove_ClearsEntryAndDisposesCts()
    {
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var messageId = 42;
        var cts = new CancellationTokenSource();

        store.Register(chatGroupId, messageId, cts);
        store.Remove(chatGroupId, messageId);

        Assert.Empty(store.SnapshotForChatGroup(chatGroupId));
        Assert.Throws<ObjectDisposedException>(() => cts.Token.Register(() => { }));
    }

    [Fact]
    public void Remove_UnknownKey_DoesNotThrow()
    {
        var store = new InFlightStore();

        store.Remove(Guid.NewGuid(), 999);
    }

    [Fact]
    public void SetRepairHint_ConsumeRepairHint_ReturnsAndClears()
    {
        var store = new InFlightStore();
        var chatGroupId = Guid.NewGuid();
        var hint = new RepairHint(42, "Alice", "hello");

        store.SetRepairHint(chatGroupId, hint);
        var consumed = store.ConsumeRepairHint(chatGroupId);

        Assert.True(consumed.HasValue);
        Assert.Equal(42, consumed.Value.MissedMessageId);
        Assert.Equal("Alice", consumed.Value.MissedSenderName);

        Assert.Null(store.ConsumeRepairHint(chatGroupId));
    }

    [Fact]
    public void ConsumeRepairHint_NoHint_ReturnsNull()
    {
        var store = new InFlightStore();

        var result = store.ConsumeRepairHint(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void CancelAllAsync_CancelsAllCts()
    {
        var store = new InFlightStore();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        store.Register(Guid.NewGuid(), 1, cts1);
        store.Register(Guid.NewGuid(), 2, cts2);

        store.CancelAllAsync();

        Assert.True(cts1.IsCancellationRequested);
        Assert.True(cts2.IsCancellationRequested);
    }

    [Fact]
    public void CancelAllAsync_AlreadyDisposedCts_DoesNotThrow()
    {
        var store = new InFlightStore();
        var cts = new CancellationTokenSource();
        store.Register(Guid.NewGuid(), 1, cts);
        cts.Dispose();

        store.CancelAllAsync();
    }
}
