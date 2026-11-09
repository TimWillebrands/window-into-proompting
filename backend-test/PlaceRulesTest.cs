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
        Assert.Equal("[1,2.6,0.1]", parts[0]!["box"]!.ToJsonString());
        Assert.Equal("[0,0.3,-0.5]", parts[0]!["at"]!.ToJsonString());
        Assert.Equal("#abc", parts[0]!["c"]!.ToString());
        Assert.True(parts[1]!["text"]!.ToString().Length <= 25);
        Assert.Null(ThingModel.Clean(JsonNode.Parse("""{"parts": []}""")));
    }

    [Fact]
    public void ThingModel_KeepsGroupsRepeatsAndNames_WithinItsBudget()
    {
        var raw = JsonNode.Parse("""
            {"size": [2, 9], "seats": [{"at": [5, 0], "h": 0.4, "face": 7}], "light": {"at": [0, 1, 0], "colour": "fire"}, "tint": {"cloth": "Teal"},
             "parts": [{"group": [{"cyl": [0.02, 0.5], "at": [0.2, 0, 0], "c": "dark wood", "spin": 4}], "array": {"n": 99, "step": [0.1, 0, 0]}},
                       {"use": "chair"}, {"use": "spaceship"},
                       {"lathe": [[0.1, 0], [0.2, 0.3]], "c": "$cloth", "tag": "spin"}, {"box": [0.1, 0.1, 0.1], "c": "nonsense", "tag": "explode"},
                       {"extrude": [[0, 0], [1, 0]], "c": "red"}, {"tube": [[0, 0, 0], [0, 1, 0]], "c": "neon-pink", "glow": true}]}
            """);

        var model = ThingModel.CleanObject(raw)!;
        var parts = model["parts"]!.AsArray();

        Assert.Equal("[2,3]", model["size"]!.ToJsonString());
        Assert.Equal("[1,0]", model["seats"]![0]!["at"]!.ToJsonString());
        Assert.Equal(3, (int)model["seats"]![0]!["face"]!);
        Assert.StartsWith("#", model["light"]!["colour"]!.ToString());
        Assert.Equal("teal", model["tint"]!["cloth"]!.ToString());
        Assert.Equal(5, parts.Count);
        Assert.Equal(24, (int)parts[0]!["array"]!["n"]!);
        Assert.Equal("dark-wood", parts[0]!["group"]![0]!["c"]!.ToString());
        Assert.Equal("chair", parts[1]!["use"]!.ToString());
        Assert.Equal("$cloth", parts[2]!["c"]!.ToString());
        Assert.Equal("spin", parts[2]!["tag"]!.ToString());
        Assert.Equal("#999999", parts[3]!["c"]!.ToString());
        Assert.Null(parts[3]!["tag"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlaceDesign_TheGuidesOwnExampleChecksOut_WithThingsToDo(bool outdoor)
    {
        var json = JsonNode.Parse(outdoor ? PlaceDesign.OutdoorExample : PlaceDesign.IndoorExample)!.AsObject();

        var check = PlaceDesign.Inspect(json, outdoor);

        Assert.Empty(check.Errors);
        Assert.Empty(check.Notes);
        Assert.NotEmpty(check.Draft!.Features);
        var scene = JsonNode.Parse(check.Draft.Scene)!;
        foreach (var f in check.Draft.Features) Assert.NotNull(scene["furni"]![f.Id]!["p"]);
        Assert.Contains(outdoor ? "wishing-tree" : "record-crates", check.Draft.Features.Select(f => f.Id));
        Assert.Equal(outdoor, check.Draft.Features.Any(f => f.Leaves));
        Assert.Contains("1 spot", check.Preview);
    }

    [Fact]
    public void PlaceDesign_MendsSlips_AndSaysSo()
    {
        var json = JsonNode.Parse("""
            {"name": "x", "spots": [{"id": "a", "name": "the sofa"}, {"id": "b", "name": "the shelf"}],
             "scene": {"map": ["#######", "#.....#", "#.....#", "D.....#", "#######"], "floorStyle": "marble", "floor": "tan",
                       "props": [{"p": "Couch", "at": [1, 1], "spot": "a"}, {"prefab": "poster", "at": [3, 0]}, {"p": "library-shelves", "at": [5, 1]},
                                 {"p": "my-lamp", "at": [5, 3]}],
                       "models": {"My Lamp": {"parts": [{"cyl": [0.1, 1], "c": "brass"}]}},
                       "spots": {"a": {"at": [1, 1]}, "b": {"at": [4, 2]}}}}
            """)!.AsObject();

        var check = PlaceDesign.Inspect(json, outdoor: false);

        Assert.Empty(check.Errors);
        Assert.Contains(check.Notes, n => n.Contains("'Couch' read as 'sofa'"));
        Assert.Contains(check.Notes, n => n.Contains("moved off the wall"));
        Assert.Contains(check.Notes, n => n.Contains("spot 'a' moved"));
        Assert.Contains(check.Notes, n => n.Contains("floorStyle 'marble'"));
        var scene = JsonNode.Parse(check.Draft!.Scene)!;
        Assert.Equal("#c8a173", scene["floor"]!.ToString());
        Assert.NotNull(scene["models"]!["my-lamp"]);
        Assert.Equal(new[] { "poster", "library-shelves" }, check.Draft.Features.Select(f => f.Id));
    }

    [Fact]
    public void PlaceDesign_PointsAtTheNearestPrefab()
    {
        var scene = JsonNode.Parse("""{"map": ["#####", "#...#", "D...#", "#...#", "#####"], "props": [{"p": "telescope-big", "at": [1, 1]}], "spots": {}}""")!.AsObject();

        Assert.Contains(PlaceDesign.Check(scene, new List<Spot>()), e => e.Contains("did you mean telescope"));
    }

    [Fact]
    public void Features_ReadInTheThirdPerson_AndRestBetweenGoes()
    {
        Assert.Equal("looks through the telescope", Features.Third("Look through the telescope."));
        Assert.Equal("tosses a coin in the fountain", Features.Third("Toss a coin in the fountain"));
        Assert.Equal("studies the game", Features.Third("Study the game"));
        var who = Guid.NewGuid();
        var tree = new Furni { Kind = FurniKinds.Feature, Do = "Tie a wish to the tree", Leaves = true };
        Assert.True(Features.Fresh(tree, who, 1000));
        tree.Takes.Add(new Take { By = who, ByName = "Ana", Text = "that the roof holds", At = 1000 });
        Assert.False(Features.Fresh(tree, who, 2000));
        Assert.True(Features.Fresh(tree, who, 1000 + (long)Features.Again.TotalMilliseconds + 1));
        Assert.Empty(Features.Marks(tree, who, 2000));
        Assert.Single(Features.Marks(tree, Guid.NewGuid(), 2000));
    }

    [Fact]
    public void PlaceDesign_KnowsTheClientsPrefabs()
    {
        Assert.Contains("counter", PlaceDesign.Prefabs.Keys);
        Assert.Equal(new[] { 3, 1 }, PlaceDesign.Prefabs["counter"].Size);
        Assert.Contains("dance-floor 3x3", PlaceDesign.Prompt(false));
        Assert.Contains("telescope ★ 1x1", PlaceDesign.Prompt(true));
        Assert.Equal(new[] { 3, 3 }, PlaceDesign.Prefabs["dining-set"].Size);
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
