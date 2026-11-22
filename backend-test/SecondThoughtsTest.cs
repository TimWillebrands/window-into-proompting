using PartyTown.World;
using PartyTown.World.Apps;

namespace BackendTest;

public class SecondThoughtsTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(10_000_000_000);
    private static readonly Affordance Tram = new("places", "Take the tram to the centre (the Lobby, Anne there)", 1, Guid.NewGuid(), "go");

    private static WorldActivity Refused(Affordance a, double minutesAgo)
        => new() { App = "life", Action = "rest", Summary = $"thought better of it ({a.Label})", Affordance = a.Key, Ts = Now.AddMinutes(-minutesAgo).ToUnixTimeMilliseconds() };

    [Fact]
    public void Key_IsWhatTheOptionIs_NotWhatItsLabelSaysThisTurn()
        => Assert.Equal(Tram.Key, (Tram with { Label = "Take the tram to the centre (the Lobby, Denise, Henkie there)", Priority = 0 }).Key);

    [Fact]
    public void Key_TellsAFoodTripFromAVisit()
    {
        var forFood = Tram with { Label = "Take the tram to the centre (to get something to eat)", Meets = PartyTown.World.Self.Bio.Food };

        Assert.NotEqual(Tram.Key, forFood.Key);
        Assert.Equal(forFood.Key, (forFood with { Label = "Take the tram to the centre", Priority = 4 }).Key);
    }

    [Fact]
    public void ANoSaidWhenTheyCouldAffordIt_DoesNotHoldOnceItIsDistress()
    {
        var forFood = Tram with { Meets = PartyTown.World.Self.Bio.Food };
        var own = new[] { Refused(forFood, 5), Refused(Tram, 5) };

        Assert.True(SecondThoughts.Left(forFood, null, own, Now) < 0.2);
        Assert.Equal(1, SecondThoughts.Left(forFood, PartyTown.World.Self.Bio.Food, own, Now));
        Assert.True(SecondThoughts.Left(forFood, PartyTown.World.Self.Bio.Rest, own, Now) < 0.2);
        Assert.True(SecondThoughts.Left(Tram, PartyTown.World.Self.Bio.Food, own, Now) < 0.2);
    }

    [Fact]
    public void ABreakToEat_IsNotDampedByTheShift()
    {
        var forFood = Tram with { Meets = PartyTown.World.Self.Bio.Food };

        Assert.Equal(1, PartyTown.World.Work.Shifts.Damping(Tram, onShift: true));
        Assert.Equal(0, PartyTown.World.Work.Shifts.Damping(forFood, onShift: true));
        Assert.Equal(0, PartyTown.World.Work.Shifts.Damping(Tram, onShift: false));
    }

    [Fact]
    public void NothingRefused_LeavesThePull()
    {
        var went = new WorldActivity { App = "places", Action = "walk", Affordance = Tram.Key, Ts = Now.AddMinutes(-5).ToUnixTimeMilliseconds() };
        var rested = new WorldActivity { App = "life", Action = "rest", Summary = "is taking it easy", Ts = Now.AddMinutes(-2).ToUnixTimeMilliseconds() };

        Assert.Equal(1, SecondThoughts.Left(Tram.Key, new[] { went, rested }, Now));
        Assert.False(SecondThoughts.Is(went));
        Assert.False(SecondThoughts.Is(rested));
    }

    [Fact]
    public void ARefusal_StaysRefused_AndComesBackSlowly()
    {
        Assert.Equal(SecondThoughts.Floor, SecondThoughts.Left(Tram.Key, new[] { Refused(Tram, 0) }, Now), 3);
        Assert.Equal(0.325, SecondThoughts.Left(Tram.Key, new[] { Refused(Tram, 30) }, Now), 3);
        Assert.Equal(1, SecondThoughts.Left(Tram.Key, new[] { Refused(Tram, 60) }, Now), 3);
    }

    [Fact]
    public void RefusedAgain_StaysRefusedLonger()
    {
        var twice = new[] { Refused(Tram, 60), Refused(Tram, 100) };
        var often = Enumerable.Range(0, 8).Select(i => Refused(Tram, 239 + i)).ToArray();

        Assert.True(SecondThoughts.Left(Tram.Key, twice, Now) < 0.4);
        Assert.True(SecondThoughts.Left(Tram.Key, often, Now) < 1);
        Assert.Equal(1, SecondThoughts.Left(Tram.Key, new[] { Refused(Tram, 240), Refused(Tram, 300) }, Now), 3);
    }

    [Fact]
    public void OnlyThatOption_IsRefused()
    {
        var home = Tram with { Label = "Head home", TargetId = Guid.NewGuid() };
        var knock = Tram with { Label = "Knock at the Lobby", Verb = "knock" };

        Assert.Equal(1, SecondThoughts.Left(home.Key, new[] { Refused(Tram, 1) }, Now));
        Assert.Equal(1, SecondThoughts.Left(knock.Key, new[] { Refused(Tram, 1) }, Now));
    }

    [Theory]
    [InlineData(0.1, 2)]
    [InlineData(0.7, 1)]
    [InlineData(1.0, 0)]
    public void MostlyRefused_GivesUpItsSeatOnTheMenu(double left, int seats) => Assert.Equal(seats, SecondThoughts.Seats(left));
}
