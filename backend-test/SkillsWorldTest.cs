using System.Text.Json.Nodes;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Self;
using PartyTown.World.Skills;

namespace BackendTest;

/// <summary>Skills in a real cluster (ADR 0054): read off the lived sheet, learned by doing, grown with practice.</summary>
public class SkillsWorldTest : IClassFixture<WorldLifeTest.Fixture>
{
    private readonly WorldLifeTest.Fixture _fx;

    public SkillsWorldTest(WorldLifeTest.Fixture fx)
    {
        _fx = fx;
        WorldLifeTest.ScriptedLlm.JsonWith = Answer;
    }

    /// <summary>The World's answers about Skills; anything else falls through to the shared script.</summary>
    private static JsonObject? Answer(string system, string user)
    {
        if (system.Contains("2 to 4 of them"))
            return Obj("""{"skills": [{"name": "pulling espresso", "how": "You dial the grinder in each morning and dump a shot that runs fast.", "cues": ["espresso", "grinder", "coffee"]}, {"name": "none", "how": "x"}]}""");
        if (system.Contains("Someone has now done the same thing"))
        {
            if (user.Contains("wish")) return Obj("""{"none": true}""");
            if (user.Contains("steamed milk")) return Obj("""{"none": false, "same": "Pulling Espresso"}""");
            return Obj("""{"none": false, "same": "", "name": "reading the night sky", "how": "You find the Plough first and hop from it to the rest.", "cues": ["telescope", "stars"]}""");
        }
        if (system.Contains("Someone has got better at something"))
            return Obj("""{"how": "You know the sky by season and find faint things by looking just beside them.", "cues": ["telescope", "stars", "planets"]}""");
        return null;
    }

    private static JsonObject Obj(string json) => (JsonObject)JsonNode.Parse(json)!;

    private ISkillsGrain Skills(Guid partyId, Guid who) => _fx.GrainFactory.GetGrain<ISkillsGrain>(WorldKeys.Life(partyId, who));

    private static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTime.UtcNow > until) return value;
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task IntrinsicSkills_AreReadOffTheLivedSheetOnly()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var who = (await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Single(c => c.Id == denise);
        // No lived sheet yet: nothing is distilled from the raw one.
        Assert.Null(await LivedSheets.SourceAsync(_fx.GrainFactory, who));
        Assert.Empty(await Skills(partyId, denise).GetAsync());

        LivedSheets.Remember(denise, LivedSheets.Hash(who.SystemPrompt!), "# Denise\n## Who you are\nYou're 31 and run the espresso bar on the corner.");
        var skills = await Eventually(() => Skills(partyId, denise).GetAsync(), s => s.Count > 0);
        var espresso = Assert.Single(skills);
        Assert.Equal("pulling espresso", espresso.Name);
        Assert.True(espresso.Intrinsic);
        Assert.Equal(SkillLevel.SecondNature, espresso.Level);
    }

    [Fact]
    public async Task SourceAsync_IsTheBioForSomeoneWithoutASheet()
    {
        var who = new CastMember { Id = Guid.NewGuid(), Name = "Bo", Bio = "Fixes bikes in Oost." };
        Assert.Equal("Fixes bikes in Oost.", await LivedSheets.SourceAsync(_fx.GrainFactory, who));
    }

    [Fact]
    public async Task ThirdTime_TeachesASkill_AndALevelCrossedRewritesTheHow()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var grain = Skills(partyId, denise);
        var key = SkillRules.Key("feature", "Look through the telescope");
        var looked = new SkillPractice(key, "looked through the telescope at the Observatory", SkillSource.Feature);
        Assert.Null(await grain.PractiseAsync(looked));
        Assert.Null(await grain.PractiseAsync(looked));
        Assert.Null(await grain.PractiseAsync(looked));

        var learned = (await Eventually(() => grain.GetAsync(), s => s.Any(x => !x.Intrinsic))).Single(x => !x.Intrinsic);
        Assert.Equal("reading the night sky", learned.Name);
        Assert.Equal(3, learned.Practice);
        Assert.Equal(SkillLevel.Learning, learned.Level);
        Assert.Equal(SkillSource.Feature, learned.Source);

        for (var i = 0; i < 6; i++) Assert.NotNull(await grain.PractiseAsync(looked));
        var practised = await grain.PractiseAsync(looked);
        Assert.Equal(SkillLevel.Practised, practised!.Level);
        var grown = (await Eventually(() => grain.GetAsync(), s => s.Any(x => x.How.Contains("by season")))).Single(x => !x.Intrinsic);
        Assert.Contains("planets", grown.Cues);
    }

    [Fact]
    public async Task NothingToLearn_ClosesTheKey()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var grain = Skills(partyId, denise);
        var wished = new SkillPractice(SkillRules.Key("feature", "Write a wish on the wall"), "wrote a wish on the wall", SkillSource.Feature);
        for (var i = 0; i < 3; i++) await grain.PractiseAsync(wished);
        await Task.Delay(300);
        for (var i = 0; i < 3; i++) Assert.Null(await grain.PractiseAsync(wished));
        Assert.DoesNotContain(await grain.GetAsync(), s => !s.Intrinsic);
    }

    [Fact]
    public async Task TheSameKnowHow_JoinsTheSkillTheyHave()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        await _fx.GrainFactory.GetGrain<IIntrinsicSkillsGrain>(denise).SetAsync(new List<Skill>
        {
            new() { Name = "pulling espresso", How = "You dial the grinder in every morning.", Cues = new List<string> { "espresso" } },
        });
        var grain = Skills(partyId, denise);
        var shift = new SkillPractice(SkillRules.Key("work", "barista"), "worked a shift as barista, steamed milk all morning", SkillSource.Work, Times: SkillRules.Learns);
        Assert.Null(await grain.PractiseAsync(shift));
        var joined = await Eventually(() => grain.PractiseAsync(shift with { Times = 1 }), s => s is not null);
        Assert.Equal("pulling espresso", joined!.Name);
        Assert.True(joined.Intrinsic);
        Assert.DoesNotContain(await grain.GetAsync(), s => !s.Intrinsic);
    }

    [Fact]
    public async Task Promotion_MakesItWhoTheyAreInEveryParty()
    {
        var (partyA, denise) = await _fx.CreateWorldAsync();
        var added = await Skills(partyA, denise).AddAsync(new Skill { Name = "haggling at the market", How = "You walk away once.", Practice = 12 });
        var skill = await Skills(partyA, denise).RemoveAsync(added.Id);
        await _fx.GrainFactory.GetGrain<IIntrinsicSkillsGrain>(denise).AddAsync(skill!);

        var elsewhere = await Skills(Guid.NewGuid(), denise).GetAsync();
        Assert.Contains(elsewhere, s => s.Intrinsic && s.Name == "haggling at the market" && s.Level == SkillLevel.SecondNature);
    }
}
