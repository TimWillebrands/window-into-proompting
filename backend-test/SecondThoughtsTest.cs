using PartyTown.World;
using PartyTown.World.Apps;

namespace BackendTest;

/// <summary>The veto teaches (ADR 0050): what was thought better of pulls less for a while.</summary>
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
