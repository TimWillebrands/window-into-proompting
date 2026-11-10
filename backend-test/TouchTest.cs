using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Pair poses follow the Tie both ways (ADR 0021).</summary>
public class TouchTest
{
    private static readonly string[] All = Poses.Pair;
    private static readonly TieRead Stranger = new(0.5, 0);
    private static readonly TieRead Acquaintance = new(4, 0);
    private static readonly TieRead Friend = new(20, 0.2);
    private static readonly TieRead Close = new(20, 0.5);

    [Fact]
    public void Strangers_GetNothing() => Assert.Empty(Touch.Offered(All, Stranger, Friend, false, false));

    [Fact]
    public void Acquaintances_HighFive() => Assert.Equal(new[] { Poses.HighFive }, Touch.Offered(All, Acquaintance, Friend, false, false));

    [Fact]
    public void Friends_Hug() => Assert.Equal(new[] { Poses.Hug, Poses.HighFive }, Touch.Offered(All, Friend, Friend, false, false));

    [Fact]
    public void Close_BothWays_HoldHandsAndSlowDance()
    {
        Assert.Contains(Poses.HoldHands, Touch.Offered(All, Close, Close, false, mutualClosest: true));
        Assert.Contains(Poses.SlowDance, Touch.Offered(All, Close, Close, false, mutualClosest: true));
        Assert.DoesNotContain(Poses.HoldHands, Touch.Offered(All, Close, Friend, false, mutualClosest: true));
        Assert.DoesNotContain(Poses.HoldHands, Touch.Offered(All, Close, Close, false, mutualClosest: false));
    }

    [Fact]
    public void Intimacy_IsNeverInitiatedTowardAHuman()
    {
        var offered = Touch.Offered(All, Close, Close, otherIsHuman: true, mutualClosest: true);
        Assert.DoesNotContain(Poses.HoldHands, offered);
        Assert.DoesNotContain(Poses.SlowDance, offered);
        Assert.Contains(Poses.Hug, offered);
    }

    [Fact]
    public void AHighFive_NeedsItsMoment()
    {
        const long now = 10_000_000;
        var fresh = now - 60_000;
        var old = now - (long)Touch.Fresh.TotalMilliseconds - 1;

        Assert.True(Touch.Moment(now, old, fresh, 0));
        Assert.True(Touch.Moment(now, old, old, fresh));
        Assert.False(Touch.Moment(now, old, old, old));
        Assert.False(Touch.Fits(Poses.HighFive, moment: false, null, now));
        Assert.True(Touch.Fits(Poses.HighFive, moment: true, null, now));
        Assert.True(Touch.Fits(Poses.Hug, moment: false, null, now));
    }

    [Fact]
    public void APassingGesture_IsNotRepeatedWithinTheHour_HoldingHandsIs()
    {
        const long now = 10_000_000;
        var other = Guid.NewGuid();
        var hug = new PartyTown.World.Apps.Affordance("together", "Hug Anne", 0, other, Poses.Hug);
        var done = new PartyTown.World.WorldActivity { App = "together", Action = "pose", Affordance = hug.Key, Ts = now - 600_000 };
        var refused = done with { App = "life", Action = "rest", Ts = now - 60_000 };

        Assert.Equal(done.Ts, Touch.LastDone(hug.Key, new[] { refused, done }));
        Assert.Null(Touch.LastDone(hug.Key, new[] { refused }));
        Assert.False(Touch.Fits(Poses.Hug, moment: true, done.Ts, now));
        Assert.True(Touch.Fits(Poses.Hug, moment: true, now - (long)Touch.Again.TotalMilliseconds, now));
        Assert.True(Touch.Fits(Poses.HoldHands, moment: false, done.Ts, now));
    }

    [Fact]
    public void Strain_BlocksEvenAHighFive() => Assert.Empty(Touch.Offered(All, new TieRead(30, -0.3), Friend, false, false));
}
