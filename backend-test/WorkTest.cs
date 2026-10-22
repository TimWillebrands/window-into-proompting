using PartyTown.World.Places;
using PartyTown.World.Work;

namespace BackendTest;

/// <summary>Jobs happen in time (ADR 0026): when shifts start and end, what they pay, how they read.</summary>
public class WorkTest
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);

    // 2026-10-19 is a Monday.
    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, 19 + day, hour, minute, 0, Offset);

    private static Occupation Barista(Guid? placeId = null) => new()
    {
        Title = "barista", Workplace = "the Café", PlaceId = placeId ?? Guid.NewGuid(), Days = Shifts.Weekdays, Start = 9 * 60, End = 17 * 60,
    };

    [Fact]
    public void Step_StartsOnAWorkdayOnceTheShiftBegins()
    {
        var job = Barista();
        Assert.Equal(ShiftStep.None, Shifts.Step(job, null, At(0, 8, 59), asleep: false));
        Assert.Equal(ShiftStep.Start, Shifts.Step(job, null, At(0, 9), asleep: false));
        Assert.Equal(ShiftStep.None, Shifts.Step(job, null, At(5, 10), asleep: false));
    }

    [Fact]
    public void Step_WaitsForASleeperAndMissesOnlyAfterTheShift()
    {
        var job = Barista();
        Assert.Equal(ShiftStep.None, Shifts.Step(job, null, At(0, 10), asleep: true));
        Assert.Equal(ShiftStep.Miss, Shifts.Step(job, null, At(0, 17), asleep: true));
    }

    [Fact]
    public void Step_EndsAtClosingOrWhenTheDayRollsOver()
    {
        var job = Barista();
        var on = new ShiftRecord { Day = Shifts.DayKey(At(0, 9)), Status = ShiftStatus.On };
        Assert.Equal(ShiftStep.None, Shifts.Step(job, on, At(0, 16, 59), false));
        Assert.Equal(ShiftStep.End, Shifts.Step(job, on, At(0, 17), false));
        Assert.Equal(ShiftStep.End, Shifts.Step(job, on, At(1, 2), false));
    }

    [Fact]
    public void Step_DoesNothingTwiceADay()
    {
        var done = new ShiftRecord { Day = Shifts.DayKey(At(0, 9)), Status = ShiftStatus.Done };
        Assert.Equal(ShiftStep.None, Shifts.Step(Barista(), done, At(0, 12), false));
    }

    [Fact]
    public void Pay_FiveShiftsMatchAWeekOfFlatPay()
    {
        var eightHours = 8 * 3_600_000L;
        Assert.Equal(70, Shifts.Pay("getting by", 0, eightHours));
        Assert.Equal(35, Shifts.Pay("getting by", 0, eightHours / 2));
        Assert.Equal(0, Shifts.Pay("getting by", eightHours, 0));
    }

    [Theory]
    [InlineData("Mon-Fri", Shifts.Weekdays)]
    [InlineData("weekdays", Shifts.Weekdays)]
    [InlineData("every day", Shifts.EveryDay)]
    [InlineData("weekends", 0b1000001)]
    [InlineData("Tue-Sat", 0b1111100)]
    [InlineData("Monday to Wednesday", 0b0001110)]
    [InlineData("Tue, Thu, Sat", 0b1010100)]
    [InlineData("Sat-Sun", 0b1000001)]
    [InlineData("whenever", Shifts.Weekdays)]
    public void ParseDays_ReadsRangesAndLists(string text, int mask) => Assert.Equal(mask, Shifts.ParseDays(text));

    [Theory]
    [InlineData("09:00-17:00", 540, 1020)]
    [InlineData("9-17", 540, 1020)]
    [InlineData("7am to 3pm", 420, 900)]
    [InlineData("10.30–18.30", 630, 1110)]
    public void ParseHours_ReadsCommonShapes(string text, int start, int end) => Assert.Equal((start, end), Shifts.ParseHours(text));

    [Theory]
    [InlineData("22:00-06:00")]
    [InlineData("whenever")]
    [InlineData("06:00-23:00")]
    public void ParseHours_RefusesWrapsAndNonsense(string text) => Assert.Null(Shifts.ParseHours(text));

    [Fact]
    public void Parse_MatchesAPlaceInTheWorldElseStaysOffscreen()
    {
        var cafe = new Place { Id = Guid.NewGuid(), Name = "the Café" };
        var places = new[] { cafe };
        var onscreen = OccupationGrain.Parse("barista", "Café", "Mon-Fri", "08:00-16:00", places)!;
        Assert.Equal(cafe.Id, onscreen.PlaceId);
        Assert.Equal("the Café", onscreen.Workplace);
        Assert.Equal(480, onscreen.Start);

        var offscreen = OccupationGrain.Parse("junior accountant", "the office in Zuid", "Mon-Fri", "nonsense", places)!;
        Assert.Null(offscreen.PlaceId);
        Assert.Equal(540, offscreen.Start);
        Assert.Null(OccupationGrain.Parse("none", "", "", "", places));
    }

    [Fact]
    public void Line_SaysFactsOnlyWhenWorkIsNear()
    {
        var job = Barista();
        Assert.Contains("starts at 09:00 (in 30 min)", Shifts.Line(job, null, At(0, 8, 30), false));
        Assert.Null(Shifts.Line(job, null, At(0, 6), false));
        Assert.Contains("Day off", Shifts.Line(job, null, At(5, 12), false));
        var on = new ShiftRecord { Day = Shifts.DayKey(At(0, 9)), Status = ShiftStatus.On };
        Assert.Contains("You're at work (barista, the Café) until 17:00", Shifts.Line(job, on, At(0, 12), atWork: true));
        Assert.Contains("you're not there", Shifts.Line(job, on, At(0, 12), atWork: false));
        Assert.DoesNotContain("dutiful", Shifts.Line(job, on, At(0, 12), true));
    }

    [Fact]
    public void FixedToday_IsTheShiftOnWorkdaysOnly()
    {
        Assert.Equal("work as barista at the Café 09:00–17:00", Shifts.FixedToday(Barista(), At(2, 7)));
        Assert.Null(Shifts.FixedToday(Barista(), At(6, 7)));
        Assert.Null(Shifts.FixedToday(null, At(2, 7)));
    }

    [Fact]
    public void Damping_HoldsOtherAppsBackOnShiftOnly()
    {
        Assert.Equal(1, Shifts.Damping("board", onShift: true));
        Assert.Equal(0, Shifts.Damping(WorkApp.AppId, onShift: true));
        Assert.Equal(0, Shifts.Damping("board", onShift: false));
    }

    [Fact]
    public void Sync_FollowsOpeningsClosingsAndRenames()
    {
        var id = Guid.NewGuid();
        var job = new Occupation { Workplace = "Ana's work", LocaleId = id };
        WorkRules.Sync(job, new Place { Id = id, Name = "Ana's office", Kind = PlaceKinds.Public });
        Assert.Equal((id, "Ana's office"), (job.PlaceId!.Value, job.Workplace));
        WorkRules.Sync(job, new Place { Id = id, Name = "Ana's office", Kind = PlaceKinds.Locale });
        Assert.Null(job.PlaceId);
        Assert.Equal(id, job.LocaleId);
    }
}
