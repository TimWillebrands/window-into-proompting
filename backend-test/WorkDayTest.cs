using PartyTown.World;
using PartyTown.World.Self;
using PartyTown.World.Work;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace BackendTest;

public class WorkDayTest
{
    private static readonly Guid Mara = Guid.NewGuid();
    private static readonly Guid Shop = Guid.NewGuid();

    private static WorldActivity Act(string app, string action, string summary, long ts, Guid? who = null, Guid? target = null)
        => new() { PersonaId = who ?? Mara, App = app, Action = action, Summary = summary, Ts = ts, TargetId = target };

    [Fact]
    public void Did_KeepsWhatWasDoneAtWorkThisShift_NotItsRoutine()
    {
        var did = WorkDayRules.Did(new[]
        {
            Act(WorkApp.AppId, "start", "started work at the Café", 100),
            Act(WorkApp.AppId, "work", "is busy at work (barista, the Café)", 110),
            Act(WorkApp.AppId, "greet", "greeted someone at the Café: \"morning\"", 120),
            Act(DeskApp.AppId, "trade", "bought 5,000 credits of Rain for Fonds", 130),
            Act(CduScreen.AppId, "look", "You looked at what the CDU did.", 135),
            Act(CduScreen.AppId, "park", "wrote to the CDU, but its allowance was spent", 138),
            Act("board", "post", "posted about the ferry", 140),
            Act(WorkApp.AppId, "greet", "greeted someone at the Café: \"late\"", 300),
            Act(WorkApp.AppId, "greet", "greeted someone at the Café: \"hi\"", 150, who: Guid.NewGuid()),
        }, Mara, 100, 200);

        Assert.Equal(new[]
        {
            "You greeted someone at the Café: \"morning\"",
            "You bought 5,000 credits of Rain for Fonds",
            "You wrote to the CDU, but its allowance was spent",
        }, did);
    }

    [Fact]
    public void News_IsWhatReachedTheCompanyThisShift_NotAReplyMeantForOnePerson()
    {
        var news = WorkDayRules.News(new[]
        {
            Act(DeskApp.AppId, "result", "Rain settled for Fonds: +400 credits.", 120, Guid.Empty, Shop),
            Act(CduScreen.AppId, "replied", "The CDU answered you: done.", 130, Guid.Empty, Shop),
            Act(CduScreen.AppId, "merged", "The One merged PR #4.", 140, Guid.Empty, Shop),
            Act(DeskApp.AppId, "result", "Old news.", 50, Guid.Empty, Shop),
            Act(DeskApp.AppId, "result", "Another company.", 150, Guid.Empty, Guid.NewGuid()),
        }, Shop, 100, 200);

        Assert.Equal(new[] { "Rain settled for Fonds: +400 credits.", "The One merged PR #4." }, news);
        Assert.Empty(WorkDayRules.News(Array.Empty<WorldActivity>(), null, 100, 200));
    }

    [Fact]
    public void Beats_AreTheOnesInsideTheShift()
    {
        var beats = new[]
        {
            new DayBeat { Minute = 7 * 60, Text = "07:00 coffee" }, new DayBeat { Minute = 10 * 60, Text = "10:00 the grinder jams" },
            new DayBeat { Minute = 16 * 60, Text = "16:00 a regular asks after her mother" }, new DayBeat { Minute = 19 * 60, Text = "19:00 dinner" },
        };
        var day = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal(new[] { "10:00 the grinder jams", "16:00 a regular asks after her mother" }, WorkDayRules.Beats(beats, day.AddHours(9), day.AddHours(17)));
        Assert.Empty(WorkDayRules.Beats(beats, day.AddHours(22), day.AddHours(30)));
    }

    [Fact]
    public void Memory_NoneOrTooShort_IsNoMemory()
    {
        Assert.Null(WorkDayRules.Memory("none"));
        Assert.Null(WorkDayRules.Memory(" None. "));
        Assert.Null(WorkDayRules.Memory("ok"));
        Assert.Null(WorkDayRules.Memory(null));
        Assert.Equal("The grinder jammed twice and I fixed it with a spoon.", WorkDayRules.Memory("\"The grinder jammed twice and I fixed it with a spoon.\""));
    }

    [Fact]
    public void Brief_AnOrdinaryJob_AsksForAMemoryNotALog()
    {
        var day = new WorkDay("barista", "the Café") { Did = new[] { "You greeted someone" } };
        var brief = WorkDayRules.Brief("You are Mara.", day);

        Assert.StartsWith("You are Mara.\n\nYour shift as barista at the Café just ended.", brief);
        Assert.Contains("It's a memory, not a log", brief);
        Assert.Contains("the memory is \"none\"", brief);
        Assert.DoesNotContain("Forms", brief);
        Assert.Equal("What you did there:\n- You greeted someone", WorkDayRules.Material(day));
        Assert.Equal("[Looking back on today's shift as barista at the Café] It was fine.", WorkDayRules.Line(day, "It was fine."));
    }

    [Fact]
    public void Brief_ACytheurgist_RemembersTheWorldNotTheMachine()
    {
        var works = new Company { Id = Shop, Name = "Vormwerk", Mandate = "keeps the Forms", Kind = CompanyKinds.Cdu };
        var day = new WorkDay("CTO", "Vormwerk")
        {
            Frame = CduScreen.Calling(works), Lens = CduScreen.Lens,
            Desk = "With the CDU this shift:\n- You asked it: \"why does nobody sleep well\" It said: beds give no rest.",
        };
        var brief = WorkDayRules.Brief("You are Denise.", day);

        Assert.Contains("Your work is the Forms: the order this world runs by", brief);
        Assert.Contains("what today showed you about how this world works or fails the people in it", brief);
        Assert.Contains("leave them at the desk", brief);
        Assert.DoesNotContain("allowance", brief);
        Assert.False(day.Empty);
        Assert.True(new WorkDay("CTO", "Vormwerk") { Frame = "x", Lens = "y" }.Empty);
    }
}
