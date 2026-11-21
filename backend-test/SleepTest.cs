using PartyTown.World.Places;

namespace BackendTest;

public class SleepTest
{
    [Fact]
    public void Chronotype_IsStableAndPlausible()
    {
        var id = Guid.NewGuid();
        var (bed, wake) = Sleep.Window(id);

        Assert.Equal((bed, wake), Sleep.Window(id));
        Assert.InRange(wake, 390, 570);
        Assert.True(bed >= 1350 || bed <= 90, $"bed {bed}");
    }

    [Fact]
    public void Night_WrapsMidnight()
    {
        Assert.True(Sleep.IsNight(23 * 60, bed: 22 * 60 + 30, wake: 7 * 60));
        Assert.True(Sleep.IsNight(3 * 60, bed: 22 * 60 + 30, wake: 7 * 60));
        Assert.False(Sleep.IsNight(12 * 60, bed: 22 * 60 + 30, wake: 7 * 60));
        Assert.True(Sleep.IsNight(2 * 60, bed: 60, wake: 8 * 60));
        Assert.False(Sleep.IsNight(23 * 60, bed: 60, wake: 8 * 60));
    }

    [Fact]
    public void TurningIn_LastsUntilJustAfterTonightsBedtime_ButNeverMoreThanSixHours()
    {
        const long now = 1_000_000;
        long Minutes(long until) => (until - now) / 60_000;

        Assert.Equal(2 * 60 + 15, Minutes(Sleep.TurnInUntil(now, 20 * 60 + 30, bed: 22 * 60 + 30, wake: 7 * 60)));
        Assert.Equal(6 * 60, Minutes(Sleep.TurnInUntil(now, 20 * 60, bed: 2 * 60 + 30, wake: 7 * 60)));
        Assert.Equal(15, Minutes(Sleep.TurnInUntil(now, 23 * 60, bed: 22 * 60 + 30, wake: 7 * 60)));
    }

    [Fact]
    public void ATurnedInBody_StaysTurnedInUntilTheRealNight_AndNotIntoTheNextDay()
    {
        const int bed = 2 * 60 + 30;
        const int wake = 7 * 60;
        const long until = 10 * 3_600_000L;

        Assert.True(Sleep.TurnedIn(until, until - 1, 2 * 60, bed, wake));
        Assert.True(Sleep.TurnedIn(until, until + 20 * 60_000, 2 * 60 + 20, bed, wake));
        Assert.False(Sleep.TurnedIn(until, until + 5 * 3_600_000L, 8 * 60, bed, wake));
        Assert.False(Sleep.TurnedIn(until, until + 22 * 3_600_000L, 2 * 60, bed, wake));
        Assert.False(Sleep.TurnedIn(null, 0, 2 * 60, bed, wake));
    }

    [Fact]
    public void TheDay_SetsWaking_AndPushesBedtimePastTheLastBeat()
    {
        var (bed, wake) = Sleep.Window(Guid.Empty, firstBeat: 6 * 60, lastBeat: 23 * 60 + 45);

        Assert.Equal(6 * 60, wake);
        Assert.Equal(15, bed);
    }
}
