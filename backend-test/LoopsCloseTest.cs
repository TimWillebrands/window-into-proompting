using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Threads close and life brings news (ADR 0055): promises settle off-screen, talk gets ratified, lives happen.</summary>
public class LoopsCloseTest
{
    // Saturday 21:25 in Amsterdam winter time.
    private static readonly DateTimeOffset Sat = new(2026, 11, 7, 21, 25, 0, TimeSpan.FromHours(1));
    private static readonly Guid Denise = Guid.NewGuid();

    private static Desire Justins(string when = PromiseRules.Soon, DateTimeOffset? at = null, bool coming = false)
        => PromiseRules.Make(Denise, coming, "Ik stuur het btw-formulier vanavond.", "stuur het dan", when, at ?? Sat);

    [Fact]
    public void SayingItAgain_KeepsTheFirstDeadline()
    {
        var first = Justins();
        var (due, until, settles) = (first.DueAt, first.Until, first.SettlesAt);

        Assert.True(PromiseRules.Restate(new List<Desire> { first }, Justins(PromiseRules.Tomorrow, Sat.AddHours(20)), Sat.AddHours(20)));

        Assert.Equal(1, first.Again);
        Assert.Equal(due, first.DueAt);
        Assert.Equal(until, first.Until);
        Assert.Equal(settles, first.SettlesAt);
        Assert.True(DesireRules.Fades(first, intrinsic: false, Sat.AddHours(24)));
    }

    [Fact]
    public void APromiseToDoSomething_SettlesOffScreen_BeforeItLapses()
    {
        var soon = Justins();
        var tomorrow = Justins(PromiseRules.Tomorrow);
        var midnight = new DateTimeOffset(2026, 11, 8, 0, 0, 0, TimeSpan.FromHours(1));

        Assert.False(PromiseRules.OffStageDue(soon, Sat.AddHours(3).ToUnixTimeMilliseconds()));
        Assert.True(PromiseRules.OffStageDue(soon, Sat.AddHours(4).ToUnixTimeMilliseconds()));
        Assert.False(PromiseRules.OffStageDue(tomorrow, midnight.AddHours(13).ToUnixTimeMilliseconds()));
        Assert.True(PromiseRules.OffStageDue(tomorrow, midnight.AddHours(14).ToUnixTimeMilliseconds()));
        Assert.True(PromiseRules.SettlesAt(tomorrow) < tomorrow.Until);
        Assert.False(PromiseRules.OffStageDue(soon, Sat.AddHours(24).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void AComeOver_IsKeptByBeingThere_NeverSettledForThem()
    {
        var coming = Justins(coming: true);

        Assert.Null(coming.SettlesAt);
        Assert.False(PromiseRules.OffStageDue(coming, Sat.AddHours(10).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void APromiseFromBefore_SettlesAWhileAfterItCameDue()
    {
        var old = Justins();
        old.SettlesAt = null;

        Assert.Equal(old.DueAt + (long)PromiseRules.SoonSettles.TotalMilliseconds, PromiseRules.SettlesAt(old));
        Assert.True(PromiseRules.OffStageDue(old, Sat.AddHours(5).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void ASettledOne_IsNotSettledAgain()
    {
        var kept = Justins();
        kept.Status = DesireStatus.Fulfilled;

        Assert.False(PromiseRules.OffStageDue(kept, Sat.AddHours(5).ToUnixTimeMilliseconds()));
    }

    [Fact]
    public void OffScreen_ThePutOffCounts_AndItEndsEitherWay()
    {
        var put = Justins();
        put.Again = 6;

        Assert.EndsWith("They said it again 6 times since.", OffStageRules.Said("Justin", "Denise", put));
        Assert.Contains("did it", OffStageRules.Brief(true));
        Assert.Contains("didn't do it", OffStageRules.Brief(false));
        Assert.Contains("Nothing is left to do", OffStageRules.Brief(false));
        Assert.Contains("\"outcome\"", OffStageRules.Brief(true));
        Assert.Null(OffStageRules.Outcome("  "));
        Assert.Equal("[Settled off-screen] Justin sent it.", OffStageRules.Line("Justin", "sent it."));
    }

    [Fact]
    public void Talk_IsOnlyPicked_WhenThereIsTalk()
    {
        var random = new Random(7);
        for (var i = 0; i < 300; i++)
            Assert.NotEqual(HappeningRules.Talk, HappeningRules.PickKind(new List<int>(), random, talk: false));
        Assert.Contains("keep talking about", HappeningRules.Kinds[HappeningRules.Talk]);
    }

    [Fact]
    public void Talk_HandsTheNarratorWhatPeopleSay_OtherKindsSteerAway()
    {
        var lines = new[] { "- Henkie to Denise: \"stuur de FAIL-output\"" };

        var talk = HappeningRules.Talking(HappeningRules.Talk, new List<string> { "fail-output", "staging" }, lines);
        var other = HappeningRules.Talking(0, new List<string> { "fail-output" }, lines);

        Assert.Contains("this time it's about one thing in here", talk);
        Assert.Contains("fail-output, staging", talk);
        Assert.Contains("FAIL-output", talk);
        Assert.Contains("steer away from it", other);
        Assert.DoesNotContain("Words they keep", other);
    }

    [Fact]
    public void OwnHappenings_GoToWhoeverHasntHadOne_AndRest()
    {
        var now = Sat.ToUnixTimeMilliseconds();
        var (fresh, never, rested) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var awake = new List<(Guid, long)>
        {
            (fresh, now - (long)TimeSpan.FromHours(1).TotalMilliseconds),
            (never, 0),
            (rested, now - (long)TimeSpan.FromHours(7).TotalMilliseconds),
        };
        var random = new Random(3);

        Assert.Equal(never, OwnHappenings.Pick(awake, now, random));
        Assert.Equal(rested, OwnHappenings.Pick(awake.Where(a => a.Item1 != never).ToList(), now, random));
        Assert.Null(OwnHappenings.Pick(new List<(Guid, long)> { (fresh, now - 1000) }, now, random));
    }

    [Fact]
    public void OwnHappenings_KindIsNeverOneOfTheLastFew()
    {
        var random = new Random(11);
        var lately = new Queue<int>();
        for (var i = 0; i < 100; i++)
        {
            var kind = OwnHappenings.PickKind(lately, random);
            Assert.DoesNotContain(kind, lately);
            lately.Enqueue(kind);
            if (lately.Count > OwnHappenings.KindMemory) lately.Dequeue();
        }
    }

    [Fact]
    public void OwnHappenings_ShowInRightNow_ThenCarryIntoTheNextDay_ThenGo()
    {
        var day = new SelfState
        {
            Beats = SelfMath.ParseBeats("08:00 coffee\n21:00 dishes"),
            Own = "Your sister calls: she's moving back to Utrecht and wants your spare room for a month.",
            OwnAt = Sat.ToUnixTimeMilliseconds(),
        };

        Assert.StartsWith("Happened in your own life", OwnHappenings.Line(day, Sat.AddMinutes(30)));
        Assert.Contains("Happened in your own life", SelfRender.Presence(day, 1, Sat.AddMinutes(30), new List<string>()));
        Assert.Null(OwnHappenings.Line(day, Sat.AddHours(13)));
        Assert.Contains("Happened to you lately: Your sister calls", OwnHappenings.Carry(day, Sat.AddHours(20)));
        Assert.Contains("Happened to you lately", SelfMath.DayState(day, Sat.AddHours(20), Guid.NewGuid(), false, "", "", "", ""));
        Assert.Equal(string.Empty, OwnHappenings.Carry(day, Sat.AddHours(40)));
    }

    [Fact]
    public void OwnHappenings_Material_HoldsTheirDaySoFar_AndWhatLastHappened()
    {
        var day = new SelfState
        {
            Beats = SelfMath.ParseBeats("08:00 coffee\n23:00 bed"),
            Threads = new List<string> { "the VAT return" },
            Own = "Your boiler makes a knocking noise.",
        };

        var material = OwnHappenings.Material("Justin", "A notary in Amsterdam.", day, Sat);

        Assert.Contains("Who Justin is:\nA notary in Amsterdam.", material);
        Assert.Contains("Their day so far: coffee", material);
        Assert.DoesNotContain("bed", material);
        Assert.Contains("Going on in their life lately: the VAT return", material);
        Assert.Contains("(not this again): Your boiler", material);
        Assert.Contains("\"summary\"", OwnHappenings.Brief("Justin", OwnHappenings.Kinds[0]));
    }
}
