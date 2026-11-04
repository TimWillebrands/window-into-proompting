using PartyTown.World.Work;
using PartyTown.World.Work.Practices;

namespace BackendTest;

/// <summary>When Sessions happen and end, and how they read (ADR 0030).</summary>
public class PracticeRulesTest
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);
    private static readonly Guid Coach = Guid.NewGuid();
    private static readonly Guid Client = Guid.NewGuid();

    // 2026-10-19 is a Monday.
    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, 19 + day, hour, minute, 0, Offset);

    private static Occupation Job() => new() { Title = "career coach", Workplace = "the office", Days = Shifts.Weekdays, Start = 9 * 60, End = 17 * 60 };

    private static PracticeSession Booked(DateTimeOffset at, SessionStatus status = SessionStatus.Booked)
        => new() { StartsAt = at.ToUnixTimeMilliseconds(), Status = status, PractitionerId = Coach, ClientId = Client, PractitionerName = "Bo", ClientName = "Ana", Reason = "stuck at work" };

    private static string Local(long? at) => DateTimeOffset.FromUnixTimeMilliseconds(at!.Value).ToOffset(Offset).ToString("ddd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void NextSlot_LeavesALeadAndRoundsToAQuarter()
        => Assert.Equal("Mon 10:30", Local(PracticeRules.NextSlot(Job(), new List<PracticeSession>(), At(0, 10, 7))));

    [Fact]
    public void NextSlot_SkipsWhatsTaken()
    {
        var taken = new List<PracticeSession> { Booked(At(0, 10, 30)) };

        Assert.Equal("Mon 11:00", Local(PracticeRules.NextSlot(Job(), taken, At(0, 10, 7))));
    }

    [Fact]
    public void NextSlot_IgnoresWhatsOver()
    {
        var taken = new List<PracticeSession> { Booked(At(0, 10, 30), SessionStatus.Done), Booked(At(0, 10, 30), SessionStatus.Missed) };

        Assert.Equal("Mon 10:30", Local(PracticeRules.NextSlot(Job(), taken, At(0, 10, 7))));
    }

    [Fact]
    public void NextSlot_MustFitWhollyInsideTheDay()
        => Assert.Equal("Tue 09:00", Local(PracticeRules.NextSlot(Job(), new List<PracticeSession>(), At(0, 16, 20))));

    [Fact]
    public void NextSlot_SkipsTheWeekend()
        => Assert.Equal("Mon 09:00", Local(PracticeRules.NextSlot(Job(), new List<PracticeSession>(), At(4, 17))));

    [Fact]
    public void NextSlot_NoWorkingDays_IsNoSlot()
    {
        var job = Job();
        job.Days = 0;

        Assert.Null(PracticeRules.NextSlot(job, new List<PracticeSession>(), At(0, 10)));
    }

    [Theory]
    [InlineData(10, 4, 8, false)]
    [InlineData(31, 4, 30, true)]
    [InlineData(10, PracticeRules.MaxLines, 9, true)]
    [InlineData(10, 4, 4, true)]
    public void Over_OnTimeLinesOrSilence(int minutesIn, int lines, int lastLineAt, bool over)
    {
        var start = At(0, 10);
        var session = Booked(start, SessionStatus.Running);
        session.StartedAt = start.ToUnixTimeMilliseconds();

        Assert.Equal(over, PracticeRules.Over(session, lines, start.AddMinutes(lastLineAt).ToUnixTimeMilliseconds(), start.AddMinutes(minutesIn).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void TookPlace_NeedsBothOfThemToSpeak()
    {
        var session = Booked(At(0, 10));

        Assert.True(PracticeRules.TookPlace(session, new[] { Coach, Client, Coach }));
        Assert.False(PracticeRules.TookPlace(session, new[] { Coach, Coach }));
        Assert.False(PracticeRules.TookPlace(session, Array.Empty<Guid>()));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0.9, 2)]
    [InlineData(0.6, 2)]
    [InlineData(0.4, 1)]
    public void Pull_FollowsTheFit(double? fit, int pull) => Assert.Equal(pull, PracticeRules.Pull(fit));

    [Fact]
    public void Pull_NoFit_IsNoOffer() => Assert.Null(PracticeRules.Pull(0.2));

    [Fact]
    public void Owed_NamesTheSessionToBothSides()
    {
        var sessions = new List<PracticeSession> { Booked(At(0, 11)) };

        var client = PracticeRules.Owed(sessions, new List<Homework>(), Client, _ => "Bo", At(0, 10)).Single();
        var coach = PracticeRules.Owed(sessions, new List<Homework>(), Coach, _ => "Ana", At(0, 10)).Single();

        Assert.Contains("session with Bo", client.Text);
        Assert.Equal(Coach, client.HolderId);
        Assert.Contains("seeing Ana", coach.Text);
    }

    [Fact]
    public void Owed_DuringASession_TellsEachSideWhyTheyreThere()
    {
        var running = Booked(At(0, 10), SessionStatus.Running);
        var homework = new List<Homework> { new() { ClientId = Client, PractitionerId = Coach, Text = "call the recruiter", DueAt = At(1, 10).ToUnixTimeMilliseconds() } };

        var client = PracticeRules.Owed(new List<PracticeSession> { running }, new List<Homework>(), Client, _ => "Bo", At(0, 10, 5)).Single();
        var coach = PracticeRules.Owed(new List<PracticeSession> { running }, homework, Coach, _ => "Ana", At(0, 10, 5), "blunt").Single();

        Assert.Contains("you came because: stuck at work", client.Text);
        Assert.Contains("they came because: stuck at work", coach.Text);
        Assert.Contains("How you work: blunt", coach.Text);
        Assert.Contains("Last time they were going to: call the recruiter", coach.Text);
    }

    [Theory]
    [InlineData("desks", "desks", false, true)]
    [InlineData("desks", "window", false, false)]
    [InlineData("desks", "desks", true, false)]
    public void Together_MeansSettledAtTheSameSpot(string coachSpot, string clientSpot, bool clientWalking, bool together)
    {
        var place = Guid.NewGuid();
        var bodies = new List<PartyTown.World.Places.Body>
        {
            new() { ParticipantId = Coach, PlaceId = place, SpotId = coachSpot },
            new() { ParticipantId = Client, PlaceId = place, SpotId = clientSpot, DestPlaceId = clientWalking ? place : null },
        };

        Assert.Equal(together, PracticeRules.Together(bodies, Coach, Client));
    }

    [Fact]
    public void Owed_HomeworkIsHeldByThePractitionerUntilItLapses()
    {
        var homework = new List<Homework> { new() { ClientId = Client, PractitionerId = Coach, Text = "call the recruiter", DueAt = At(1, 10).ToUnixTimeMilliseconds() } };

        var owed = PracticeRules.Owed(new List<PracticeSession>(), homework, Client, _ => "Bo", At(0, 10)).Single();

        Assert.Equal("Bo is expecting you to: call the recruiter", owed.Text);
        Assert.Equal(Coach, owed.HolderId);
        Assert.Empty(PracticeRules.Owed(new List<PracticeSession>(), homework, Coach, _ => "Bo", At(0, 10)));
        Assert.Empty(PracticeRules.Owed(new List<PracticeSession>(), homework, Client, _ => "Bo", At(9, 10)));
        homework[0].Kept = true;
        Assert.Empty(PracticeRules.Owed(new List<PracticeSession>(), homework, Client, _ => "Bo", At(0, 10)));
    }
}
