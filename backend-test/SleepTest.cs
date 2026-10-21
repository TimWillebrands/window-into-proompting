using PartyTown.World.Places;

namespace BackendTest;

/// <summary>When Bodies sleep: a stable chronotype per Persona, or their day's first beat as waking.</summary>
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
    public void TheDay_SetsWaking_AndPushesBedtimePastTheLastBeat()
    {
        var (bed, wake) = Sleep.Window(Guid.Empty, firstBeat: 6 * 60, lastBeat: 23 * 60 + 45);

        Assert.Equal(6 * 60, wake);
        Assert.Equal(15, bed); // 23:45 + 30 min, past midnight
    }
}
