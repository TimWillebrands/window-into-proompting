using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>Places perception rules that keep Life from playing musical chairs.</summary>
public class PlacesAppTest
{
    private static LifeContext With(params WorldActivity[] recent) => new(
        Guid.NewGuid(), new CastMember { Id = Guid.NewGuid(), Name = "Denise" }, new List<CastMember>(),
        new HashSet<Guid>(), new List<ChatGroupInfo>(), recent.ToList(), DateTimeOffset.UtcNow,
        new Dictionary<Guid, PartyTown.World.Self.AttentionLook>());

    private static WorldActivity Did(string app, string action, int minutesAgo)
        => new() { App = app, Action = action, Ts = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo).ToUnixTimeMilliseconds() };

    [Fact]
    public void AfterAWalk_YouStayPut()
    {
        Assert.True(PlacesApp.JustWalked(With(Did("places", "walk", 2))));
        Assert.True(PlacesApp.JustWalked(With(Did("board", "reply", 1), Did("places", "walk", 3))));
    }

    [Fact]
    public void AnOldOrBuriedWalk_DoesNotPin()
    {
        Assert.False(PlacesApp.JustWalked(With(Did("places", "walk", 40))));
        Assert.False(PlacesApp.JustWalked(With(Did("board", "reply", 1), Did("diary", "diary", 2), Did("places", "walk", 3))));
        Assert.False(PlacesApp.JustWalked(With(Did("places", "say", 1))));
    }
}
