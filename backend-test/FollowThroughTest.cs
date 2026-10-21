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
