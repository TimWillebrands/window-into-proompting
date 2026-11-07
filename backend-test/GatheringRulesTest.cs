using PartyTown.World;

namespace BackendTest;

/// <summary>Gatherings keep feast time: they start on time or not at all, end, and fit the host's waking hours.</summary>
public class GatheringRulesTest
{
    private const long Min = 60_000;

    private static Gathering G(GatheringStatus status, long startsAt) => new() { Status = status, StartsAt = startsAt };

    [Fact]
    public void Next_StartsOnTime_MissesWhatTheWorldSleptThrough_AndEnds()
    {
        var now = 1_000 * Min;
        Assert.Equal(GatheringStatus.Planned, GatheringRules.Next(G(GatheringStatus.Planned, now + 5 * Min), now));
        Assert.Equal(GatheringStatus.Started, GatheringRules.Next(G(GatheringStatus.Planned, now - 10 * Min), now));
        Assert.Equal(GatheringStatus.Missed, GatheringRules.Next(G(GatheringStatus.Planned, now - 9 * 60 * Min), now));
        Assert.Equal(GatheringStatus.Started, GatheringRules.Next(G(GatheringStatus.Started, now - 60 * Min), now));
        Assert.Equal(GatheringStatus.Over, GatheringRules.Next(G(GatheringStatus.Started, now - 4 * 60 * Min), now));
    }

    [Theory]
    [InlineData(18 * 60, 23 * 60 + 30, 7 * 60, true)]
    [InlineData(23 * 60 + 19, 23 * 60 + 30, 7 * 60, false)]
    [InlineData(30, 23 * 60 + 30, 7 * 60, false)]
    [InlineData(23 * 60, 60, 8 * 60, true)]
    public void MayPlan_OnlyWithAnEveningLeft(int minute, int bed, int wake, bool expected)
        => Assert.Equal(expected, GatheringRules.MayPlan(minute, bed, wake));

    [Fact]
    public void StartIn_EndsWellBeforeBed()
    {
        Assert.Equal(90, GatheringRules.StartIn(90, minuteNow: 18 * 60, bed: 23 * 60 + 30, wake: 7 * 60));
        Assert.Equal(150, GatheringRules.StartIn(240, minuteNow: 20 * 60, bed: 23 * 60 + 30, wake: 7 * 60));
        Assert.Equal(15, GatheringRules.StartIn(5, minuteNow: 12 * 60, bed: 23 * 60 + 30, wake: 7 * 60));
    }
}
