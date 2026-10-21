using System.Text.Json.Nodes;
using PartyTown.World.Places;

namespace BackendTest;

public class PlaceRulesTest
{
    [Fact]
    public void ThingModel_DropsJunk_AndClampsIntoOneTile()
    {
        var raw = JsonNode.Parse("""{"wall": true, "parts": [{"box": [2, 9, 0.1], "at": [0, -3, -2], "c": "#ABC", "rx": 9}, {"sign": [0.5, 0.2], "text": "an extremely long sign text that goes on", "c": "#000000"}, "nope"]}""");

        var parts = JsonNode.Parse(ThingModel.Clean(raw)!)!["parts"]!.AsArray();

        Assert.Equal(2, parts.Count);
        Assert.Equal("[1,2,0.1]", parts[0]!["box"]!.ToJsonString());
        Assert.Equal("[0,0.3,-0.5]", parts[0]!["at"]!.ToJsonString());
        Assert.Equal("#abc", parts[0]!["c"]!.ToString());
        Assert.True(parts[1]!["text"]!.ToString().Length <= 25);
        Assert.Null(ThingModel.Clean(JsonNode.Parse("""{"parts": []}""")));
    }

    [Fact]
    public void PlaceDesign_KnowsTheClientsPrefabs()
    {
        Assert.Contains("counter", PlaceDesign.Prefabs.Keys);
        Assert.Equal(new[] { 3, 1 }, PlaceDesign.Prefabs["counter"].Size);
        Assert.Contains("dance-floor 3x3", PlaceDesign.Prompt(false));
    }

    [Fact]
    public void PlaceDesign_ExplainsWhatDoesntCheckOut()
    {
        var scene = JsonNode.Parse("""
            {"map": ["#####", "#...#", "#.#.#", "#####", "#...?"],
             "props": [{"p": "spaceship", "at": [1, 1]}, {"p": "sofa", "at": [4, 3]}],
             "spots": {"a": {"at": [2, 1]}}}
            """)!.AsObject();
        var spots = new List<Spot> { new() { Id = "a", Name = "a", Capacity = 2 }, new() { Id = "b", Name = "b" } };

        var errors = PlaceDesign.Check(scene, spots);

        Assert.Contains(errors, e => e.Contains("unknown map char '?'"));
        Assert.Contains(errors, e => e.Contains("no prefab 'spaceship'"));
        Assert.Contains(errors, e => e.Contains("doesn't fit"));
        Assert.Contains(errors, e => e.Contains("no way in"));
        Assert.Contains(errors, e => e.Contains("spot 'b' has no at"));
    }

    [Fact]
    public void Appearance_MergesOnlyPaletteValues_AndSaysWhatChanged()
    {
        var before = AppearanceRules.Default(Guid.NewGuid());
        var after = AppearanceRules.Merge(before, new Dictionary<string, string?> { ["hair"] = "Purple ", ["facialHair"] = "beard", ["build"] = "huge" });

        Assert.Equal("purple", after.Hair);
        Assert.Equal("beard", after.FacialHair);
        Assert.Equal(before.Build, after.Build);
        Assert.StartsWith("purple hair", AppearanceRules.Changes(before, after) + (before.Hair == "purple" ? "purple hair" : ""));
    }

    [Fact]
    public void Poses_FollowTheSpotAndTheIntent()
    {
        var bench = new Spot { Id = "bench", Name = "the bench by the canal", At = "on", Seat = true, Poses = new() { Poses.Lounge } };

        Assert.DoesNotContain(Poses.Hug, Poses.Allowed(bench, company: false));
        Assert.Contains(Poses.Hug, Poses.Allowed(bench, company: true));
        Assert.DoesNotContain(Poses.SlowDance, Poses.Allowed(bench, company: true));
        Assert.Equal(Poses.Lounge, Poses.FromIntent("Kick back on the bench and watch the boats", Poses.Allowed(bench, false)));
        Assert.Null(Poses.FromIntent("dance like nobody's watching", Poses.Allowed(bench, false)));
        Assert.Equal("lounging on the bench by the canal", Poses.Describe(Poses.Lounge, bench, null));
    }
}
