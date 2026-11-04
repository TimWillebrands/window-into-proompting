using System.Text.Json.Nodes;
using PartyTown.World.Places;

namespace PartyTown.Tests;

public class FigureDesignTest
{
    private static JsonObject Doc(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    public void Clean_ClampsNumbers_AndDropsWhatItDoesntKnow()
    {
        var clean = Doc(FigureDesign.Clean(Doc("""
            {"body": {"skin": "#abc", "height": 9, "build": "wide", "wings": 1},
             "shoes": {"kind": "moon-boots", "color": "not a colour", "platform": -3},
             "nonsense": {"a": 1}}
            """))!);

        Assert.Equal("#aabbcc", clean["body"]!["skin"]!.ToString());
        Assert.Equal(1, clean["body"]!["height"]!.GetValue<double>());
        Assert.Equal(0.5, clean["body"]!["build"]!.GetValue<double>());
        Assert.Null(clean["body"]!["wings"]);
        Assert.Equal("sneaker", clean["shoes"]!["kind"]!.ToString());
        Assert.Equal("#2b2b2b", clean["shoes"]!["color"]!.ToString());
        Assert.Equal(0, clean["shoes"]!["platform"]!.GetValue<double>());
        Assert.Null(clean["nonsense"]);
    }

    [Theory]
    [InlineData("""{"nothing": "useful"}""")]
    [InlineData("""[1, 2, 3]""")]
    public void Clean_ReturnsNull_WhenNoSlotSurvives(string json)
        => Assert.Null(FigureDesign.Clean(JsonNode.Parse(json)));

    [Fact]
    public void Clean_CapsHairZonesExtrasAndPrints()
    {
        var zones = string.Join(",", Enumerable.Repeat("""{"at": "crown", "count": 900, "len": 40}""", 30));
        var extras = string.Join(",", Enumerable.Repeat("""{"name": "x", "socket": "neck", "blobs": [{"shape": "ball", "r": 99}]}""", 30));
        var prints = string.Join(",", Enumerable.Repeat("""{"at": "chest", "brief": "a skull"}""", 9));
        var clean = Doc(FigureDesign.Clean(Doc($$$"""{"hair": {"zones": [{{{zones}}}]}, "extras": [{{{extras}}}], "top": {"prints": [{{{prints}}}]}}"""))!);

        var kept = clean["hair"]!["zones"]!.AsArray();
        Assert.Equal(FigureDesign.MaxZones, kept.Count);
        Assert.Equal(40, kept[0]!["count"]!.GetValue<int>());
        Assert.Equal(2.2, kept[0]!["len"]!.GetValue<double>());
        Assert.Equal(FigureDesign.MaxExtras, clean["extras"]!.AsArray().Count);
        Assert.Equal(1, clean["extras"]![0]!["blobs"]![0]!["r"]!.GetValue<double>());
        Assert.Equal(FigureDesign.MaxPrints, clean["top"]!["prints"]!.AsArray().Count);
    }

    [Fact]
    public void Clean_DropsBlobsItCantDraw_AndExtrasLeftEmpty()
    {
        var clean = Doc(FigureDesign.Clean(Doc("""
            {"extras": [
                {"name": "ok", "socket": "wrist-l", "blobs": [{"shape": "capsule", "r": 0.1}, {"shape": "ball", "r": 0.1, "metal": true}]},
                {"name": "gone", "socket": "neck", "blobs": [{"shape": "teapot"}]}]}
            """))!);

        var extras = clean["extras"]!.AsArray();
        var extra = Assert.Single(extras);
        var blob = Assert.Single(extra!["blobs"]!.AsArray());
        Assert.Equal("ball", blob!["shape"]!.ToString());
        Assert.True(blob["metal"]!.GetValue<bool>());
    }

    [Fact]
    public void Clean_KeepsPosesInRange()
    {
        var clean = Doc(FigureDesign.Clean(Doc("""
            {"stance": {"spine": [999, 0, 0], "elbowL": 400, "handR": "horns", "handL": "jazz"},
             "moves": {"My Dance!": {"loop": 99, "keys": [{"t": 0.5, "armL": [10, 170, 0]}]}, "empty": {"keys": []}}}
            """))!);

        Assert.Equal(60, clean["stance"]!["spine"]![0]!.GetValue<double>());
        Assert.Equal(150, clean["stance"]!["elbowL"]!.GetValue<double>());
        Assert.Equal("horns", clean["stance"]!["handR"]!.ToString());
        Assert.Null(clean["stance"]!["handL"]);
        Assert.Equal(12, clean["moves"]!["mydance"]!["loop"]!.GetValue<double>());
        Assert.Null(clean["moves"]!["empty"]);
    }

    [Theory]
    [InlineData("""<svg viewBox="0 0 256 256"><script>alert(1)</script><circle r="9"/></svg>""", "circle", "script")]
    [InlineData("""<svg><foreignObject><div/></foreignObject><rect width="9"/></svg>""", "rect", "foreignObject")]
    [InlineData("""<svg onload="alert(1)"><path d="M0 0" onclick="x()"/></svg>""", "path", "onclick")]
    [InlineData("""<svg><image href="http://evil/x.png"/><text>hi</text></svg>""", "text", "image")]
    [InlineData("""<svg><rect fill="url(http://evil/#a)" width="9"/></svg>""", "rect", "evil")]
    public void Svg_KeepsDrawingOnly(string raw, string kept, string gone)
    {
        var clean = FigureDesign.Svg(raw);

        Assert.NotNull(clean);
        Assert.Contains(kept, clean);
        Assert.DoesNotContain(gone, clean);
        Assert.Contains("""xmlns="http://www.w3.org/2000/svg" """.Trim(), clean);
    }

    [Theory]
    [InlineData("not svg at all")]
    [InlineData("<svg><unclosed></svg>")]
    [InlineData("""<!DOCTYPE svg [<!ENTITY x "y">]><svg>&x;</svg>""")]
    public void Svg_RefusesWhatIsntSvg(string raw)
    {
        var clean = FigureDesign.Svg(raw);

        Assert.True(clean is null || !clean.Contains('&'));
    }

    [Fact]
    public void Svg_FindsTheDrawingInsideChatter()
        => Assert.Contains("circle", FigureDesign.Svg("""Sure! Here you go: <svg><circle r="3"/></svg> enjoy"""));

    [Fact]
    public void Picks_TakeTheirSlotOutOfTheDesign()
    {
        var before = new Appearance
        {
            Design = """{"top": {"color": "#111111"}, "outer": {"color": "#222222"}, "hair": {"color": "#333333", "tips": "#444444", "cap": 1, "zones": [{"at": "crown", "color": "#555555"}]}}""",
            Summary = "all in black",
        };

        var after = AppearanceRules.Merge(before, new Dictionary<string, string?> { ["topStyle"] = "hoodie", ["hair"] = "pink" });

        var design = Doc(after.Design!);
        Assert.Null(design["top"]);
        Assert.Null(design["outer"]);
        Assert.Null(design["hair"]!["color"]);
        Assert.Null(design["hair"]!["tips"]);
        Assert.Null(design["hair"]!["zones"]![0]!["color"]);
        Assert.Equal(1, design["hair"]!["cap"]!.GetValue<double>());
        Assert.Null(after.Summary);
    }

    [Fact]
    public void Picks_ThatChangeNothing_LeaveTheDesignAndSummary()
    {
        var before = new Appearance { TopStyle = "hoodie", Design = """{"top": {"color": "#111111"}}""", Summary = "all in black" };

        var after = AppearanceRules.Merge(before, new Dictionary<string, string?> { ["topStyle"] = "hoodie" });

        Assert.Equal(before.Design, after.Design);
        Assert.Equal("all in black", after.Summary);
    }

    [Fact]
    public void Over_ReplacesWholeSlots()
    {
        var merged = Doc(FigureDesign.Over("""{"top": {"color": "#111111", "fit": 1}, "shoes": {"color": "#222222"}}""", """{"top": {"color": "#333333"}}""")!);

        Assert.Equal("#333333", merged["top"]!["color"]!.ToString());
        Assert.Null(merged["top"]!["fit"]);
        Assert.Equal("#222222", merged["shoes"]!["color"]!.ToString());
    }

    [Fact]
    public void Artwork_FillsPrints_AndDropsTheOnesThatDidntComeOut()
    {
        var design = FigureDesign.Clean(Doc("""{"top": {"prints": [{"at": "chest", "brief": "a skull"}, {"at": "back", "brief": "a bat"}]}}"""));

        var briefs = FigureDesign.Briefs(design);
        var drawn = FigureDesign.WithArtwork(design, new Dictionary<(string, int), string> { [("top", 0)] = "<svg/>" });

        Assert.Equal(new[] { "a skull", "a bat" }, briefs.Select(b => b.Brief));
        var kept = Assert.Single(Doc(drawn!)["top"]!["prints"]!.AsArray());
        Assert.Equal("<svg/>", kept!["svg"]!.ToString());
        Assert.Null(kept["brief"]);
    }

    [Fact]
    public void TheFirstFivePaletteStylesStayPutForOldDefaults()
    {
        Assert.Equal(new[] { "short", "long", "curly", "bun", "bald" }, AppearanceRules.Styles.Take(5));
        Assert.Equal(new[] { "tshirt", "hoodie", "shirt", "sweater", "jacket" }, AppearanceRules.TopStyles.Take(5));
    }

    [Fact]
    public void ThePromptOffersEveryPaletteField()
    {
        foreach (var field in AppearanceRules.Fields) Assert.Contains($"{field.Key}: ", FigureDesign.Prompt);
    }
}
