using System.Text.Json.Nodes;
using PartyTown.World;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace BackendTest;

/// <summary>What a Cytheurgist sees and how the CDU's news becomes the World's (ADR 0039): facts from git and GitHub, not its say-so.</summary>
public class CduScreenTest
{
    private static readonly Company Shop = new() { Id = Guid.NewGuid(), Name = "Vormwerk", Mandate = "keeps the Forms", Kind = CompanyKinds.Cdu };
    private static readonly WorldHealth Quiet = new(0, 0, 0, Array.Empty<(string, int)>());
    private static readonly Guid Denise = Guid.NewGuid();
    private static readonly Guid Justin = Guid.NewGuid();

    private static CduStatus Up(int ahead = 0, CduPr? pr = null, bool awake = false) => new(
        awake, awake ? "Justin" : null, 0, "careful",
        new CduTrunk("abc", Enumerable.Range(1, ahead).Select(i => new CduCommit($"s{i}", $"fix: thing {i}", "Denise")).ToList()),
        pr, new CduAllowance(1.25, 5), 10, 3, null, Minds);

    private static readonly List<CduModel> Minds = new() { new("beam-free", "free preview", "free", 0, 0), new("kimi-k2.7-code", "the best coder", "capable", 0.95, 4) };

    private static CduExchange Said(Guid who, string status = CduRules.Done, bool asked = false, string kind = CduRules.Talk, string turn = "t1") => new()
    {
        TurnId = turn, CompanyId = Shop.Id, SpeakerId = who, SpeakerName = who == Denise ? "Denise" : "Justin", Kind = kind,
        Text = "The timeouts are killing us", Status = status, Reply = status == CduRules.Done ? (asked ? "I can. Which room first?" : "Done.") : null, Asked = asked,
    };

    private static CduDesk Desk(CduStatus? cdu, params CduExchange[] said) => new(Shop, new CduState { Exchanges = said.ToList() }, cdu, Quiet, Denise);

    private static string Verbs(CduDesk desk) => string.Join(",", CduScreen.Affordances(desk).Select(a => a.Verb));

    [Fact]
    public void Affordances_Quiet_OffersTalkAndLook()
        => Assert.Equal("talk,look", Verbs(Desk(Up())));

    [Fact]
    public void Affordances_AllowanceSpent_OnlyLook()
    {
        var spent = Up() with { Allowance = new CduAllowance(1.02, 1) };
        Assert.Equal("look", Verbs(Desk(spent, Said(Justin, asked: true))));
        Assert.Contains("spent today's allowance", CduScreen.Render(Desk(spent)));
    }

    [Fact]
    public void Affordances_Dark_OffersNothing()
        => Assert.Empty(CduScreen.Affordances(Desk(null)));

    [Fact]
    public void Affordances_AQuestionOpen_OffersAnAnswerFirst()
        => Assert.Equal("answer,talk,look", Verbs(Desk(Up(), Said(Justin, asked: true))));

    [Fact]
    public void Affordances_TheirWordsUnanswered_OnlyLook()
        => Assert.Equal("look", Verbs(Desk(Up(), Said(Justin, asked: true), Said(Denise, CduRules.Queued, turn: "t2"))));

    [Fact]
    public void Affordances_AQuestionAlreadyAnswered_IsNotOfferedAgain()
    {
        var state = new CduState();
        CduRules.Spoke(state, Said(Justin, asked: true));
        CduRules.Spoke(state, Said(Justin, CduRules.Queued, kind: CduRules.Answer, turn: "t2"));
        Assert.Equal("talk,look", Verbs(new CduDesk(Shop, state, Up(), Quiet, Denise)));
    }

    [Fact]
    public void Render_ShowsWhatItSaysAsItsWords_AndTheTrunkAndAgoraAsFacts()
    {
        var text = CduScreen.Render(Desk(Up(2, new CduPr(12, "u", "fix: thing 1", "OPEN", "h", "failing"), awake: true), Said(Justin, asked: true)));

        Assert.Contains("The CDU is awake, working on what Justin said.", text);
        Assert.Contains("$3.75 of $5.00 left", text);
        Assert.Contains("It said, which is only what it says: I can. Which room first?", text);
        Assert.Contains("It asked Justin something", text);
        Assert.Contains("Its trunk holds 2 commits not yet in the world.", text);
        Assert.Contains("PR #12, \"fix: thing 1\", its checks fail.", text);
    }

    [Fact]
    public void Render_Dark_SaysSoAndNothingElse()
        => Assert.Contains("The CDU is dark", CduScreen.Render(Desk(null)));

    [Fact]
    public void Glance_OffShift_HasNothingOfTheFrame()
    {
        var glance = CduScreen.Glance(Shop);
        Assert.DoesNotContain("Forms", glance);
        Assert.DoesNotContain("CDU", glance);
        Assert.Contains("Forms are code", CduScreen.Frame(Shop));
    }

    [Fact]
    public void Look_ReadsCommitsAndThePr()
    {
        var seen = CduScreen.Look(Up(1, new CduPr(3, "u", "fix: thing 1", "OPEN", "h", "passing")));
        Assert.Contains("\"fix: thing 1\" for Denise", seen);
        Assert.Contains("PR #3 \"fix: thing 1\" is open, its checks pass", seen);
        Assert.Contains("dark", CduScreen.Look(null));
    }

    [Fact]
    public void ReadMind_HeldToTheList_ElseTheCheapest()
    {
        Assert.Equal("kimi-k2.7-code", CduScreen.ReadMind(new JsonObject { ["mind"] = "KIMI-K2.7-code" }, Minds));
        Assert.Equal("beam-free", CduScreen.ReadMind(new JsonObject { ["mind"] = "gpt-9" }, Minds));
        Assert.Null(CduScreen.ReadMind(new JsonObject { ["mind"] = "x" }, null));
    }

    [Fact]
    public void Render_ListsTheMindsCheapestFirst_AndTheFrameAsksForFrugality()
    {
        var text = CduScreen.Render(Desk(Up()));
        Assert.Contains("- beam-free (free): free preview, free", text);
        Assert.Contains("- kimi-k2.7-code (capable): the best coder, $0.95 in, $4.00 out per million tokens", text);
        Assert.Contains("a free preview for small things", CduScreen.Frame(Shop));
    }

    [Fact]
    public void ReadMessage_NothingOrNone_IsSilence()
    {
        Assert.Null(CduScreen.ReadMessage(new JsonObject { ["message"] = "none" }));
        Assert.Null(CduScreen.ReadMessage(null));
        Assert.Equal("Fix the doors", CduScreen.ReadMessage(new JsonObject { ["message"] = " Fix the doors " }));
    }

    [Fact]
    public void Apply_AReply_ReachesTheSpeakerOnly_Once()
    {
        var state = new CduState();
        CduRules.Spoke(state, Said(Denise, CduRules.Queued));
        var news = new List<CduNews> { new(1, "replied", "Moved two Forms.\n\nMore?", "t1", Asked: true, Commits: 2) };

        var told = Assert.Single(CduRules.Apply(state, news, 5));
        Assert.Empty(CduRules.Apply(state, news, 6));

        Assert.Equal((Denise, "replied"), (told.Only, told.Kind));
        Assert.Contains("moved the Forms: 2 commits on the trunk", told.Text);
        Assert.Equal((CduRules.Done, true, 1L), (state.Exchanges[0].Status, state.Exchanges[0].Asked, state.News));
    }

    [Fact]
    public void Apply_Verdicts_ReachEveryoneWithAMood()
    {
        var told = CduRules.Apply(new CduState(), new List<CduNews>
        {
            new(1, "merged", "fix: doors", Number: 4),
            new(2, "closed", "feat: chaos", Number: 5),
            new(3, "checks", "failing", Number: 6),
            new(4, "rebased", "0 commits ahead"),
        }, 0);

        Assert.Equal(new bool?[] { true, false, null }, told.Select(t => t.Lifts).ToArray());
        Assert.All(told, t => Assert.Null(t.Only));
        Assert.Contains("The One merged PR #4", told[0].Text);
        Assert.Contains("drowned idea", told[1].Text);
    }

    [Fact]
    public void Edge_OnlyOnAChange()
    {
        Assert.Equal(CduShift.Began, CduRules.Edge(false, true));
        Assert.Equal(CduShift.Ended, CduRules.Edge(true, false));
        Assert.Equal(CduShift.None, CduRules.Edge(true, true));
    }

    [Fact]
    public void Kinds_TheWorksIsReadAsTheCdu()
        => Assert.True(CompanyKinds.IsCdu("works") && CompanyKinds.IsCdu("cdu") && !CompanyKinds.IsCdu("trading"));
}
