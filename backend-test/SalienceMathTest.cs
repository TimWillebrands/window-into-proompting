using PartyTown.Services.Memory;

namespace BackendTest;

/// <summary>
/// Pure-arithmetic checks for the ADR 0015 salience formula:
/// <c>weight × decay(now − ts) + use_bonus(recall_count, last_recalled)</c>.
/// The ranking-relevant properties live here so the integration test only needs to
/// prove the substrate round-trips through AGE, not re-derive the math.
/// </summary>
public sealed class SalienceMathTest
{
    private static readonly DateTimeOffset Now = new(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FreshMemory_ScoresItsWeight()
    {
        var score = SalienceMath.Score(0.8, Now, recallCount: 0, lastRecalled: null, Now);
        Assert.Equal(0.8, score, precision: 6);
    }

    [Fact]
    public void Decay_HalvesAtHalfLife()
    {
        Assert.Equal(0.5, SalienceMath.Decay(TimeSpan.FromDays(SalienceMath.DecayHalfLifeDays)), precision: 6);
        Assert.Equal(1.0, SalienceMath.Decay(TimeSpan.Zero), precision: 6);
        // Clock skew (capture timestamp marginally in the future) must not inflate salience.
        Assert.Equal(1.0, SalienceMath.Decay(TimeSpan.FromDays(-1)), precision: 6);
    }

    [Fact]
    public void OldLowWeightMemory_RanksBelowFreshOne()
    {
        var oldHigh = SalienceMath.Score(0.9, Now.AddDays(-30), 0, null, Now);
        var freshLow = SalienceMath.Score(0.3, Now, 0, null, Now);
        Assert.True(oldHigh < freshLow,
            $"30-day-old weight-0.9 ({oldHigh:F4}) should rank below fresh weight-0.3 ({freshLow:F4})");
    }

    [Fact]
    public void OftRecalledOldMemory_OutranksFreshLowWeightOne()
    {
        // Same old memory, but the persona kept picking it — the use bonus keeps it warm.
        var oldButUsed = SalienceMath.Score(0.9, Now.AddDays(-30), recallCount: 5, lastRecalled: Now.AddHours(-1), Now);
        var freshLow = SalienceMath.Score(0.3, Now, 0, null, Now);
        Assert.True(oldButUsed > freshLow,
            $"oft-recalled old memory ({oldButUsed:F4}) should outrank fresh weight-0.3 ({freshLow:F4})");
    }

    [Fact]
    public void UseBonus_GrowsWithCount_AndIsCapped()
    {
        var one = SalienceMath.UseBonus(1, Now, Now);
        var five = SalienceMath.UseBonus(5, Now, Now);
        var thousand = SalienceMath.UseBonus(1000, Now, Now);

        Assert.True(one > 0);
        Assert.True(five > one);
        Assert.True(thousand <= 0.5, "use bonus must stay capped — recall spam can't dominate weight");
    }

    [Fact]
    public void UseBonus_FadesSinceLastRecall()
    {
        var recent = SalienceMath.UseBonus(3, Now.AddDays(-1), Now);
        var stale = SalienceMath.UseBonus(3, Now.AddDays(-60), Now);
        Assert.True(recent > stale);
    }

    [Fact]
    public void UnrecalledMemory_GetsNoBonus()
    {
        Assert.Equal(0, SalienceMath.UseBonus(0, null, Now));
        Assert.Equal(0, SalienceMath.UseBonus(0, Now, Now));
    }
}
