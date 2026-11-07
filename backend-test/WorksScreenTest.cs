using System.Text.Json.Nodes;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Work.Companies;
using PartyTown.World.Work.Works;

namespace BackendTest;

/// <summary>What an Architect sees and how a Commission moves (ADR 0039): facts come from the Runner, not the builder's say-so.</summary>
public class WorksScreenTest
{
    private static readonly List<RunnerModel> Models = new() { new("free-one", "quick", 0, 0), new("dear-one", "careful", 3, 15) };
    private static readonly Company Works = new() { Id = Guid.NewGuid(), Name = "The Works", Mandate = "keeps the world running", Kind = CompanyKinds.Works };
    private static readonly WorldHealth Quiet = new(0, 0, 0, Array.Empty<(string, int)>());
    private static readonly RunnerStatus Up = new(false, 40, Models);

    private static Commission Open(CommissionStatus status, int files = 0, CommissionKind kind = CommissionKind.Change) => new()
    {
        Id = Guid.NewGuid(), Title = "Mend the doors", Brief = "Doors should stay shut", ArchitectName = "Ria", Kind = kind, Status = status,
        Files = files, BudgetUsd = 2, SpentUsd = 0.5,
    };

    private static WorksDesk Desk(RunnerStatus? runner, params Commission[] commissions) => new(Works, commissions, runner, Quiet, Guid.NewGuid());

    private static string Verbs(WorksDesk desk) => string.Join(",", WorksScreen.Affordances(desk).Select(a => a.Verb));

    [Fact]
    public void Affordances_FreeBench_OffersACommissionAndASurvey()
        => Assert.Equal("commission,survey", Verbs(Desk(Up)));

    [Fact]
    public void Affordances_BuilderUnreachable_OffersNothing()
        => Assert.Empty(WorksScreen.Affordances(Desk(null)));

    [Fact]
    public void Affordances_BuilderWorking_LeavesOnlyWaiting()
        => Assert.Empty(WorksScreen.Affordances(Desk(Up with { Busy = true }, Open(CommissionStatus.Working))));

    [Fact]
    public void Affordances_Waiting_OffersTheMovesOnThatWork()
        => Assert.Equal("survey,steer,propose,drop", Verbs(Desk(Up, Open(CommissionStatus.Waiting, files: 2))));

    [Fact]
    public void Affordances_WaitingWithNothingChanged_HasNothingToPropose()
        => Assert.DoesNotContain("propose", Verbs(Desk(Up, Open(CommissionStatus.Waiting))));

    [Fact]
    public void Affordances_APropositionWaitingOnTheSpirits_LeavesTheBenchFree()
        => Assert.StartsWith("commission", Verbs(Desk(Up, Open(CommissionStatus.Proposed, files: 1))));

    [Fact]
    public void Render_KeepsTheBuildersClaimApartFromWhatChanged()
    {
        var waiting = Open(CommissionStatus.Waiting);
        waiting.Report = "All done, everything works.";

        var screen = WorksScreen.Render(Desk(Up, waiting));

        Assert.Contains("The code has not changed.", screen);
        Assert.Contains("which is only what it says: All done", screen);
    }

    [Fact]
    public void Render_SaysWhenTheMoneyRanOut()
    {
        var waiting = Open(CommissionStatus.Waiting, files: 1);
        waiting.Stop = "budget";

        Assert.Contains("the money you gave it ran out", WorksScreen.Render(Desk(Up, waiting)));
    }

    [Fact]
    public void Render_ShowsTheReserveAndTheBuilders()
    {
        var screen = WorksScreen.Render(Desk(Up));

        Assert.Contains("The reserve: $40.00.", screen);
        Assert.Contains("free-one: quick, free", screen);
        Assert.Contains("dear-one: careful, $3.00 in, $15.00 out", screen);
    }

    [Fact]
    public void Render_AnUnknownReserveIsNotCalledEmpty()
        => Assert.Contains("nobody can read it", WorksScreen.Render(Desk(Up with { ReserveUsd = null })));

    [Fact]
    public void Render_ReportsTheSpiritsWord()
    {
        var turned = Open(CommissionStatus.Rejected);
        turned.Note = "too loud";

        Assert.Contains("turned down \"Mend the doors\": too loud", WorksScreen.Render(Desk(Up, turned)));
    }

    [Fact]
    public void Glance_CountsWhatWaitsOnTheSpirits()
        => Assert.Equal("At The Works: Nothing on the bench. One proposition with the world-spirits.", WorksScreen.Glance(Works, new[] { Open(CommissionStatus.Proposed) }));

    [Fact]
    public void Health_CountsTheLastDayAndGroupsFailures()
    {
        var now = DateTimeOffset.UtcNow;
        var at = now.AddHours(-1).ToUnixTimeMilliseconds();
        var activities = new List<WorldActivity>
        {
            new() { Ts = at, Action = "error", Summary = "Response did not arrive on time in 00:03:00 for message: Request [abc]" },
            new() { Ts = at, Action = "error", Summary = "Response did not arrive on time in 00:03:00 for message: Request [def]" },
            new() { Ts = at, Action = "fizzle", Summary = "The operation was canceled." },
            new() { Ts = at, Action = "rest", Summary = "had a nap" },
            new() { Ts = now.AddDays(-2).ToUnixTimeMilliseconds(), Action = "error", Summary = "old news" },
        };

        var health = WorksScreen.Health(activities, now);

        Assert.Equal((4, 3, 1), (health.Activities, health.Failed, health.Resting));
        Assert.Equal(("Response did not arrive on time in 00:03:00", 2), health.Failures[0]);
    }

    private static JsonObject Voiced(string title = "Mend doors", string brief = "Keep them shut", string builder = "dear-one", string budget = "$3.50")
        => new() { ["title"] = title, ["why"] = "They bang", ["brief"] = brief, ["builder"] = builder, ["budget"] = budget };

    [Fact]
    public void ReadCommission_TakesTheirBuilderAndBudget()
    {
        var draft = WorksScreen.ReadCommission(Voiced(), Models, 40)!;

        Assert.Equal(("Mend doors", "They bang", "Keep them shut", "dear-one", 3.5), (draft.Title, draft.Why, draft.Brief, draft.Model, draft.BudgetUsd));
    }

    [Theory]
    [InlineData("none", "x")]
    [InlineData("", "x")]
    [InlineData("Mend", "")]
    public void ReadCommission_NothingDecided_IsNoCommission(string title, string brief)
        => Assert.Null(WorksScreen.ReadCommission(Voiced(title, brief), Models, 40));

    [Fact]
    public void ReadCommission_AnUnknownBuilder_FallsBackToTheFirst()
        => Assert.Equal("free-one", WorksScreen.ReadCommission(Voiced(builder: "the best one"), Models, 40)!.Model);

    [Theory]
    [InlineData("500", 40, WorksScreen.MaxBudget)]
    [InlineData("30", 12.5, 12.5)]
    [InlineData("0", 40, WorksScreen.MinBudget)]
    [InlineData("whatever", 40, WorksScreen.DefaultBudget)]
    [InlineData("5", 0, WorksScreen.MinBudget)]
    public void Budget_IsHeldToWhatExists(string said, double reserve, double expected)
        => Assert.Equal(expected, WorksScreen.Budget(JsonValue.Create(said), WorksScreen.DefaultBudget, reserve));

    [Fact]
    public void Budget_WithAnUnreadableReserve_StillHasACeiling()
        => Assert.Equal(WorksScreen.MaxBudget, WorksScreen.Budget(JsonValue.Create("9999"), 1, null));

    [Fact]
    public void ReadSteer_TakesTheMessageAndExtraMoney()
        => Assert.Equal(("Try again", 2d), WorksScreen.ReadSteer(new JsonObject { ["message"] = "Try again", ["add_budget"] = "2" }, 40));

    [Fact]
    public void ReadSteer_SilenceIsNoMove()
        => Assert.Null(WorksScreen.ReadSteer(new JsonObject { ["message"] = " " }, 40));

    [Fact]
    public void Order_SurveyAsksForNoChange()
    {
        var order = WorksScreen.Order(Works, Open(CommissionStatus.Filed, kind: CommissionKind.Survey));

        Assert.Contains("Change nothing.", order);
        Assert.Contains("Doors should stay shut", order);
    }

    [Fact]
    public void Order_ChangeCarriesTheBriefAndTheLimits()
    {
        var order = WorksScreen.Order(Works, Open(CommissionStatus.Filed));

        Assert.Contains("What should change: Doors should stay shut", order);
        Assert.Contains("no internet", order);
    }

    private static CommissionFacts Facts(CommissionStatus status, int files = 0, string? report = null) => new(
        status, 0.25, 900, report, files, 3, 1, new List<string> { "a.cs" }, null, null, null, new List<CommissionCheck>(), 7);

    [Theory]
    [InlineData("starting", CommissionStatus.Working)]
    [InlineData("working", CommissionStatus.Working)]
    [InlineData("idle", CommissionStatus.Waiting)]
    [InlineData("checking", CommissionStatus.Checking)]
    [InlineData("proposed", CommissionStatus.Proposed)]
    [InlineData("dropped", CommissionStatus.Dropped)]
    [InlineData("done", CommissionStatus.Answered)]
    [InlineData("failed", CommissionStatus.Failed)]
    [InlineData("gibberish", CommissionStatus.Failed)]
    public void StatusOf_MapsTheRunnersWords(string run, CommissionStatus expected)
        => Assert.Equal(expected, CommissionRules.StatusOf(run));

    [Fact]
    public void Apply_ReachingWaiting_IsNewsOnceOnly()
    {
        var commission = Open(CommissionStatus.Working);

        Assert.Equal(CommissionNews.TurnDone, CommissionRules.Apply(commission, Facts(CommissionStatus.Waiting, 2, "done"), 1));
        Assert.Equal(CommissionNews.None, CommissionRules.Apply(commission, Facts(CommissionStatus.Waiting, 2, "done"), 2));
        Assert.Equal((2, "done", 0.25, 7L), (commission.Files, commission.Report, commission.SpentUsd, commission.Cursor));
    }

    [Fact]
    public void Apply_NoNewReport_KeepsTheLastOne()
    {
        var commission = Open(CommissionStatus.Working);
        commission.Report = "earlier";

        CommissionRules.Apply(commission, Facts(CommissionStatus.Working), 1);

        Assert.Equal("earlier", commission.Report);
    }

    [Theory]
    [InlineData("merged", CommissionStatus.Accepted, CommissionNews.Accepted)]
    [InlineData("rejected", CommissionStatus.Rejected, CommissionNews.Rejected)]
    [InlineData("fallen", CommissionStatus.Fallen, CommissionNews.Rejected)]
    [InlineData("open", CommissionStatus.Proposed, CommissionNews.None)]
    public void Verdict_MovesOnlyAProposition(string said, CommissionStatus status, CommissionNews news)
    {
        var commission = Open(CommissionStatus.Proposed);

        Assert.Equal(news, CommissionRules.Verdict(commission, said, null, 1));
        Assert.Equal(status, commission.Status);
    }

    [Fact]
    public void Verdict_OnFinishedWork_ChangesNothing()
    {
        var commission = Open(CommissionStatus.Dropped);

        Assert.Equal(CommissionNews.None, CommissionRules.Verdict(commission, "merged", null, 1));
        Assert.Equal(CommissionStatus.Dropped, commission.Status);
    }

    [Fact]
    public void Trim_KeepsWhatIsStillWatched()
    {
        var all = Enumerable.Range(0, CommissionRules.Kept).Select(_ => Open(CommissionStatus.Dropped)).ToList();
        all.Insert(0, Open(CommissionStatus.Proposed));

        CommissionRules.Trim(all);

        Assert.Equal(CommissionRules.Kept, all.Count);
        Assert.Equal(CommissionStatus.Proposed, all[0].Status);
    }
}
