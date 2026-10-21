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
    public void Strain_BlocksEvenAHighFive() => Assert.Empty(Touch.Offered(All, new TieRead(30, -0.3), Friend, false, false));
}
