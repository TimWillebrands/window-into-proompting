using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>Realtime snapshot keeps each Room's newest messages; MessageId is per Room.</summary>
public class SnapshotMessagesTest
{
    private static List<ChatMessage> Room(Guid id, int count) =>
        Enumerable.Range(0, count).Select(i => new ChatMessage { ChatGroupId = id, MessageId = i, Content = $"m{i}" }).ToList();

    [Fact]
    public void EveryRoom_KeepsItsNewest()
    {
        var rooms = Enumerable.Range(0, 60).Select(_ => Room(Guid.NewGuid(), 10)).ToList();

        var kept = PartyGrain.NewestPerRoom(rooms, 3);

        Assert.Equal(60 * 3, kept.Count);
        foreach (var room in kept.GroupBy(m => m.ChatGroupId))
            Assert.Equal(new[] { 7, 8, 9 }, room.Select(m => m.MessageId));
    }

    [Fact]
    public void ShortRoom_KeptWhole()
    {
        var kept = PartyGrain.NewestPerRoom(new[] { Room(Guid.NewGuid(), 2) }, 200);

        Assert.Equal(new[] { 0, 1 }, kept.Select(m => m.MessageId));
    }
}
