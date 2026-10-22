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

    [Fact]
    public void OtherDistricts_CollapseToOneLineAndOneTramEach()
    {
        var districts = new List<District> { new() { Id = "zuid", Name = "Zuid" }, new() { Id = "noord", Name = "Noord" } };
        var further = new List<(Place, List<string>)>();
        for (var i = 0; i < 6; i++) further.Add((new Place { Id = Guid.NewGuid(), Name = $"the zuid place {i}", DistrictId = "zuid" }, new List<string>()));
        for (var i = 0; i < 6; i++) further.Add((new Place { Id = Guid.NewGuid(), Name = $"the noord place {i}", DistrictId = "noord" }, new List<string>()));
        var bar = new Place { Id = Guid.NewGuid(), Name = "the Bar", DistrictId = "zuid" };
        further.Add((bar, new List<string> { "Ana", "Bo" }));

        var (line, trams) = PlacesApp.FurtherAway("places", further, districts);

        Assert.Equal(2, trams.Count);
        var zuid = trams.Single(t => t.Label.Contains("Zuid"));
        Assert.Equal((bar.Id, 1), (zuid.TargetId!.Value, zuid.Priority));
        Assert.Equal("Take the tram to Zuid (the Bar, Ana, Bo there)", zuid.Label);
        Assert.Equal(0, trams.Single(t => t.Label.Contains("Noord")).Priority);
        Assert.Equal("Further away, a tram ride — Zuid: the Bar: Ana, Bo (7 places); Noord: nobody (6 places).", line);
    }

    [Fact]
    public void ShoutsDontCarryAcrossDistricts()
    {
        var lobby = new Place { Id = Guid.NewGuid(), Kind = PlaceKinds.Public, DistrictId = "centre" };
        var cafe = new Place { Id = Guid.NewGuid(), Kind = PlaceKinds.Public, DistrictId = "centre" };
        var bar = new Place { Id = Guid.NewGuid(), Kind = PlaceKinds.Public, DistrictId = "zuid" };

        Assert.True(Overhearing.NextDoor(lobby, cafe));
        Assert.False(Overhearing.NextDoor(lobby, bar));
        Assert.Equal("amsterdam-zuid", Districts.Slug("Amsterdam Zuid"));
        Assert.Equal("pijp", Districts.Slug("the Pijp!"));
        Assert.True(Districts.Same("zuid", "amsterdam-zuid"));
        Assert.False(Districts.Same("zuid", "noord"));
        Assert.False(Districts.Same("oost", "amsterdam-zuid"));
    }
}
