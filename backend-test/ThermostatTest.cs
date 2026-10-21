using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Conversation thermostat (ADR 0021): only a sustained heavy streak cues a change of register.</summary>
public class ThermostatTest
{
    [Fact]
    public void Streak_GrowsOnHeavy_ResetsOnLight()
    {
        var s = 0;
        foreach (var p in new[] { 0.9, 0.7, 0.8 }) s = Thermostat.NextStreak(s, p);
        Assert.Equal(3, s);
        Assert.Equal(0, Thermostat.NextStreak(s, 0.3));
    }

    [Fact]
    public void Cue_OnlyWhenSustained_StrongerWhenCircling()
    {
        Assert.Null(Thermostat.Cue(Thermostat.SustainedStreak - 1, 0.5));
        Assert.Contains("circles", Thermostat.Cue(Thermostat.SustainedStreak, 0.8));
        Assert.Contains("intense", Thermostat.Cue(Thermostat.SustainedStreak, 0.1));
    }
}

public class WindingDownTest
{
    [Fact]
    public void Fizzling_WinsOverHeavy_AndNeedsNoStreak()
    {
        Assert.Contains("winding down", Thermostat.Cue(0, 0, fizzling: 0.8));
        Assert.Contains("winding down", Thermostat.Cue(Thermostat.SustainedStreak, 0.9, fizzling: 0.8));
        Assert.Null(Thermostat.Cue(0, 0, fizzling: 0.3));
    }
}

public class CirclingTest
{
    [Fact]
    public void Circling_AloneCuesMovingOn()
    {
        Assert.Contains("same thing", Thermostat.Cue(0, 0.8));
        Assert.Null(Thermostat.Cue(0, 0.6));
    }
}
