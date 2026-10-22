using System.Text.Json.Nodes;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>ADR 0027: a District's streets are built, not designed, and always pass the design check.</summary>
public class DistrictSceneTest
{
    private static Place Venue(string name, string archetype) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = PlaceKinds.Public, Archetype = archetype, OpenedAt = 1,
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(7)]
    public void TheStreets_PassTheCheck_ForAnyNumberOfVenues(int count)
    {
        var venues = Enumerable.Range(0, count).Select(i => Venue($"the place {i}", i % 2 == 0 ? "office" : "gym")).ToList();

        var scene = DistrictScene.Build("amsterdam-zuid", venues);

        Assert.Empty(PlaceDesign.Check(scene, DistrictScene.Spots()));
        Assert.Equal(Math.Min(count, DistrictScene.MaxShown), (scene["legend"] as JsonObject)!.Count);
    }

    [Fact]
    public void EachVenue_GetsASignedFront_AndADoorKeyedByItsScene()
    {
        var gym = Venue("the climbing gym", "gym");
        var office = Venue("Denise's office", "office");
        var other = Venue("the other office", "office");
        var scene = DistrictScene.Build("zuid", new[] { gym, office, other });

        Assert.Equal("GYM", DistrictScene.Sign(gym));
        var entrances = (JsonObject)scene["entrances"]!;
        Assert.Equal(4, entrances.Count);
        Assert.True(entrances.ContainsKey(office.Id.ToString()));
        Assert.True(entrances.ContainsKey(other.Id.ToString()));
        Assert.Equal(scene.ToJsonString(), DistrictScene.Build("zuid", new[] { gym, office, other }).ToJsonString());
    }
}
