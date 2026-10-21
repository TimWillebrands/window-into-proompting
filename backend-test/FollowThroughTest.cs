using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Saying you're coming over means walking over (ADR 0021).</summary>
public class FollowThroughTest
{
    [Fact]
    public void Delay_InABitIsAMinuteOrTwo_OnMyWayIsNow()
    {
        Assert.InRange(Coming.Delay("cart's fine, coming down in a bit").TotalSeconds, 45, 90);
        Assert.InRange(Coming.Delay("on my way!").TotalSeconds, 5, 15);
    }

    [Fact]
    public void AlreadyThere_CountsWhereYoureWalkingTo()
    {
        var place = Guid.NewGuid();
        var them = new Body { PlaceId = place, SpotId = "steps" };
        Assert.True(Coming.AlreadyThere(new Body { PlaceId = place, SpotId = "steps" }, them));
        Assert.True(Coming.AlreadyThere(new Body { PlaceId = place, SpotId = "cart", DestPlaceId = place, DestSpotId = "steps" }, them));
        Assert.False(Coming.AlreadyThere(new Body { PlaceId = place, SpotId = "cart" }, them));
    }
}

/// <summary>People settle where they've just arrived (ADR 0021).</summary>
public class SettlingTest
{
    private static PartyTown.World.Apps.Affordance Go(int priority = 1, string verb = "go") => new("places", "Head somewhere", priority, null, verb);

    [Fact]
    public void JustArrived_LeavingAgainPullsLess()
    {
        Assert.Equal(2, PartyTown.World.Settling.Penalty(Go(), TimeSpan.FromMinutes(3)));
        Assert.Equal(1, PartyTown.World.Settling.Penalty(Go(0, "spot:bridge"), TimeSpan.FromMinutes(3)));
        Assert.Equal(1, PartyTown.World.Settling.Penalty(Go(0, "join"), TimeSpan.FromMinutes(3)));
    }

    [Fact]
    public void StrongReasonsAndTime_LiftIt()
    {
        Assert.Equal(0, PartyTown.World.Settling.Penalty(Go(5), TimeSpan.FromMinutes(1)));
        Assert.Equal(0, PartyTown.World.Settling.Penalty(Go(), TimeSpan.FromMinutes(12)));
        Assert.Equal(0, PartyTown.World.Settling.Penalty(Go(), null));
        Assert.Equal(0, PartyTown.World.Settling.Penalty(Go(1, "say"), TimeSpan.FromMinutes(1)));
    }
}
