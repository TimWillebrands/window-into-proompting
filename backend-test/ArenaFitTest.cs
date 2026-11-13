using PartyTown.World.Places;

namespace BackendTest;

/// <summary>ADR 0057: a Place's feel in facts, damped by how expected it is, and how soon it makes you restless.</summary>
public class ArenaFitTest
{
    private static Place Room(string? scene = null, params Furni[] furni) => new()
    {
        Id = Guid.NewGuid(), Name = "the room", Kind = PlaceKinds.Public, CustomScene = scene, Furni = furni.ToList(),
    };

    private static Furni Thing(string name, string kind = "plant", string? @do = null) => new() { Id = name, Name = name, Kind = kind, Do = @do };

    [Fact]
    public void Feel_NamesWhatsInIt_AndCallsAFewThingsBare()
    {
        Assert.Equal("Nothing much in it.", ArenaFit.Feel(Room()));
        Assert.Equal("Bare, apart from the sofa and the bed.", ArenaFit.Feel(Room(null, Thing("the sofa"), Thing("the bed"))));
        Assert.Equal("In it: a, b, c, d and e, and more.", ArenaFit.Feel(Room(null, "abcdef".Select(c => Thing(c.ToString())).ToArray())));
    }

    [Fact]
    public void Feel_ReadsLightAndAirFromTheScene_AndLeavesToDosToTheirOwnLine()
    {
        const string scene = """{"light": "candle", "ambience": "rain", "props": [{"p": "record-crates"}, {"p": "neon", "name": "the VINYL sign"}, {"p": "turntable", "do": "Put a record on"}]}""";

        var feel = ArenaFit.Feel(Room(scene, Thing("the jukebox", "jukebox"), Thing("the darts", "game", "Throw a dart")));

        Assert.Equal("Candlelight, rain coming down. In it: the jukebox, record crates and the VINYL sign, and more.", feel);
    }

    [Fact]
    public void Feel_SurvivesABrokenScene()
        => Assert.Equal("Bare, apart from the sofa.", ArenaFit.Feel(Room("{not json", Thing("the sofa"))));

    [Fact]
    public void Novelty_FadesWithFamiliarity_AndAChangeMakesItHalfNew()
    {
        Assert.Equal(1, ArenaFit.Novelty(0, changed: false));
        Assert.Equal(0.125, ArenaFit.Novelty(7, changed: false));
        Assert.Equal(0.5, ArenaFit.Novelty(7, changed: true));
        Assert.Equal(1, ArenaFit.Novelty(0, changed: true));
    }

    [Fact]
    public void Felt_IsTheAppraisalScaledTowardNeutral()
    {
        Assert.Equal(0.9, ArenaFit.Felt(0.9, 1), 6);
        Assert.Equal(0.55, ArenaFit.Felt(0.9, 0.125), 6);
        Assert.Equal(0.5, ArenaFit.Felt(0.1, 0), 6);
    }

    [Fact]
    public void Restless_ComesSoonerWhereItWearsOnYou_LaterWhereItLifts()
    {
        Assert.Equal(TimeSpan.FromMinutes(45), ArenaFit.Restless(null));
        Assert.Equal(TimeSpan.FromMinutes(45), ArenaFit.Restless(0.5));
        Assert.Equal(TimeSpan.FromMinutes(22.5), ArenaFit.Restless(0));
        Assert.Equal(TimeSpan.FromMinutes(90), ArenaFit.Restless(1));
    }

    [Fact]
    public void Signature_ChangesWhenSomethingIsAdded()
    {
        var room = Room(null, Thing("the sofa"));
        var before = ArenaFit.Signature(room, 0);
        room.Furni.Add(Thing("a poster", FurniKinds.Decor));
        Assert.NotEqual(before, ArenaFit.Signature(room, 0));
    }
}
