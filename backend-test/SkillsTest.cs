using System.Text.Json.Nodes;
using PartyTown.World.Skills;

namespace BackendTest;

/// <summary>Skill rules (ADR 0054): levels from practice, rust, keys, and which Skill a moment touches.</summary>
public class SkillsTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-12T12:00:00Z");

    private static SkillView View(string name, string[] cues, int practice = 10, bool intrinsic = false, long? practisedAt = null)
        => SkillRules.View(new Skill
        {
            Name = name, How = $"How {name} goes.", Cues = cues.ToList(), Practice = practice,
            Since = Now.AddDays(-60).ToUnixTimeMilliseconds(), PractisedAt = practisedAt ?? Now.ToUnixTimeMilliseconds(),
        }, intrinsic, Now);

    [Theory]
    [InlineData(0, SkillLevel.New)]
    [InlineData(2, SkillLevel.New)]
    [InlineData(3, SkillLevel.Learning)]
    [InlineData(9, SkillLevel.Learning)]
    [InlineData(10, SkillLevel.Practised)]
    [InlineData(30, SkillLevel.SecondNature)]
    public void Level_ComesFromPractice(int practice, SkillLevel level) => Assert.Equal(level, SkillRules.Level(practice));

    [Fact]
    public void Intrinsic_IsSecondNatureAndNeverRusty()
    {
        var old = View("pulling espresso", ["espresso"], practice: 0, intrinsic: true, practisedAt: Now.AddDays(-90).ToUnixTimeMilliseconds());
        Assert.Equal(SkillLevel.SecondNature, old.Level);
        Assert.False(old.Rusty);
    }

    [Fact]
    public void Acquired_RustsAfterAMonthUntouched()
    {
        Assert.True(View("fixing bikes", ["bike"], practisedAt: Now.AddDays(-31).ToUnixTimeMilliseconds()).Rusty);
        Assert.False(View("fixing bikes", ["bike"], practisedAt: Now.AddDays(-29).ToUnixTimeMilliseconds()).Rusty);
    }

    [Fact]
    public void Key_IsTheSameThingAnywhere()
    {
        Assert.Equal("feature:look through the telescope", SkillRules.Key("feature", "  Look through   the Telescope. "));
        Assert.Equal(SkillRules.Key("work", "Barista"), SkillRules.Key("work", "barista"));
    }

    [Theory]
    [InlineData("bikes", "bike")]
    [InlineData("biking", "bike")]
    [InlineData("telescopes", "telescope")]
    [InlineData("baking", "bake")]
    public void Stem_MatchesWordForms(string a, string b) => Assert.Equal(SkillRules.Stem(a), SkillRules.Stem(b));

    [Fact]
    public void Match_PicksTheSkillTheMomentTouches()
    {
        var skills = new[] { View("pulling espresso", ["espresso", "grinder", "milk"]), View("fixing bikes", ["bike", "puncture", "chain"]) };
        Assert.Equal("fixing bikes", SkillRules.Match(skills, "ugh my bike chain came off again on the way here")!.Name);
        Assert.Equal("pulling espresso", SkillRules.Match(skills, "Is the grinder broken? This espresso is sour")!.Name);
        Assert.Null(SkillRules.Match(skills, "how was your weekend, anything nice?"));
        Assert.Null(SkillRules.Match(skills, null));
    }

    [Fact]
    public void Match_PrefersMoreHitsThenTheFreshHand()
    {
        var rusty = View("fixing bikes", ["bike", "chain"], practisedAt: Now.AddDays(-40).ToUnixTimeMilliseconds());
        var fresh = View("riding in traffic", ["bike", "traffic"]);
        Assert.Equal("riding in traffic", SkillRules.Match(new[] { rusty, fresh }, "my bike")!.Name);
        Assert.Equal("fixing bikes", SkillRules.Match(new[] { rusty, fresh }, "bike chain snapped")!.Name);
    }

    [Fact]
    public void Toward_NamesTheKnowHowOnTheOption()
    {
        Assert.Equal("Look through the telescope", SkillRules.Toward("Look through the telescope", null));
        Assert.Equal("Look through the telescope (you know how: reading the night sky)",
            SkillRules.Toward("Look through the telescope", View("reading the night sky", ["telescope"])));
    }

    [Fact]
    public void Render_IsOneSkillAsKnowHowNotATopic()
    {
        Assert.Equal(string.Empty, SkillRules.Render(null));
        var block = SkillRules.Render(View("fixing bikes", ["bike"]));
        Assert.StartsWith("# What you know how to do\n- Fixing bikes (practised): How fixing bikes goes.", block);
        Assert.Contains("Never bring it up for its own sake", block);
    }

    [Fact]
    public void Parse_TakesAWrittenSkillAndRefusesNone()
    {
        var json = JsonNode.Parse("""{"name": "reading the night sky.", "how": "You find the Plough first and hop from it.", "cues": "Telescope, stars, sky"}""");
        var skill = SkillRules.Parse(json, SkillSource.Feature, 1, practice: 3)!;
        Assert.Equal("reading the night sky", skill.Name);
        Assert.Equal(new[] { "telescope", "stars", "sky" }, skill.Cues);
        Assert.Equal(SkillLevel.Learning, skill.WrittenFor);
        Assert.Null(SkillRules.Parse(JsonNode.Parse("""{"name": "none", "how": "nothing to know about this at all"}"""), SkillSource.Feature, 1));
        Assert.Null(SkillRules.Parse(JsonNode.Parse("""{"name": "waving", "how": ""}"""), SkillSource.Feature, 1));
    }
}
