using PartyTown.World.Self;

namespace BackendTest;

public class BelongingTest
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    private static PlaceTie Tie(double warmth, DateTimeOffset at) => new() { Warmth = warmth, At = at.ToUnixTimeMilliseconds() };

    private static Reserves Body(double fed = 1, double rested = 1, double battery = 1, bool ailing = false)
        => new(fed, rested, 0, 0, false, true, false, ailing, battery);

    [Fact]
    public void Warmth_FadesTowardNothing_WithATenDayHalfLife()
    {
        Assert.Equal(0.8, Belonging.Warmth(Tie(0.8, Now), Now), 6);
        Assert.Equal(0.4, Belonging.Warmth(Tie(0.8, Now.AddDays(-10)), Now), 6);
        Assert.Equal(-0.2, Belonging.Warmth(Tie(-0.8, Now.AddDays(-20)), Now), 6);
        Assert.Equal(0, Belonging.Warmth(null, Now));
    }

    [Fact]
    public void Feeling_Saturates_FromBothSides()
    {
        Assert.Equal(0.15, Belonging.Step(0, 1), 6);
        Assert.Equal(0.915, Belonging.Step(0.9, 1), 6);
        Assert.Equal(-0.915, Belonging.Step(-0.9, 0), 6);
        var warm = 0.0;
        var cold = 0.0;
        for (var i = 0; i < 200; i++)
        {
            warm = Belonging.Step(warm, 1);
            cold = Belonging.Step(cold, 0);
        }
        Assert.InRange(warm, 0.99, 1);
        Assert.InRange(cold, -1, -0.99);
        Assert.True(Belonging.Step(0.9, 1) - 0.9 < Belonging.Step(0.1, 1) - 0.1);
    }

    [Fact]
    public void ANeutralTime_ChangesNothing()
    {
        var places = new Dictionary<Guid, PlaceTie>();
        var cafe = Guid.NewGuid();

        Belonging.Feel(places, cafe, 0.5, Now);
        Assert.Empty(places);

        Belonging.Feel(places, cafe, 0.9, Now);
        var after = (places[cafe].Warmth, places[cafe].At);
        Belonging.Feel(places, cafe, 0.5, Now.AddDays(1));
        Assert.Equal(after, (places[cafe].Warmth, places[cafe].At));
    }

    [Fact]
    public void AtMostTwelvePlacesAreKept_TheWeakestGoes()
    {
        var places = new Dictionary<Guid, PlaceTie>();
        var ids = Enumerable.Range(0, Belonging.Kept).Select(_ => Guid.NewGuid()).ToList();
        foreach (var (id, i) in ids.Select((id, i) => (id, i))) places[id] = Tie(i % 2 == 0 ? 0.2 + i * 0.05 : -0.2 - i * 0.05, Now);
        places[ids[0]].Warmth = 0.05;

        var newcomer = Guid.NewGuid();
        Belonging.Feel(places, newcomer, 1, Now);

        Assert.Equal(Belonging.Kept, places.Count);
        Assert.DoesNotContain(ids[0], places.Keys);
        Assert.Contains(newcomer, places.Keys);
    }

    [Fact]
    public void AHaunt_IsFromPointFour_AndPutOffFromMinusPointFour()
    {
        Assert.True(Belonging.IsHaunt(0.4));
        Assert.False(Belonging.IsHaunt(0.39));
        Assert.True(Belonging.IsPutOff(-0.4));
        Assert.False(Belonging.IsPutOff(-0.39));
        Assert.Equal(Belonging.HauntLine, Belonging.Line(0.5, home: false));
        Assert.Equal(Belonging.PutOffLine, Belonging.Line(-0.5, home: false));
        Assert.Null(Belonging.Line(0.2, home: false));
        Assert.Null(Belonging.Line(0.9, home: true));
    }

    [Fact]
    public void RunDown_AndWell_AreReadOffMoodAndBody()
    {
        Assert.False(Belonging.RunDown(0, Body()));
        Assert.True(Belonging.RunDown(-0.2, Body()));
        Assert.True(Belonging.RunDown(0, Body(battery: 0.4)));
        Assert.True(Belonging.RunDown(0, Body(rested: 0.3)));
        Assert.True(Belonging.RunDown(0, Body(ailing: true)));

        Assert.True(Belonging.Well(0.2, Body(fed: 0.5)));
        Assert.False(Belonging.Well(0.1, Body()));
        Assert.False(Belonging.Well(0.5, Body(fed: 0.4)));
        Assert.False(Belonging.Well(0.5, Body(rested: 0.3)));
        Assert.False(Belonging.Well(0.5, Body(ailing: true)));
    }

    [Fact]
    public void AtAHaunt_RestlessnessComesLater_UpToTwiceAsLong()
    {
        var usual = TimeSpan.FromMinutes(45);
        Assert.Equal(usual, Belonging.Stay(usual, 0));
        Assert.Equal(usual, Belonging.Stay(usual, -0.6));
        Assert.Equal(TimeSpan.FromMinutes(67.5), Belonging.Stay(usual, 0.5));
        Assert.Equal(TimeSpan.FromMinutes(90), Belonging.Stay(usual, 1));
    }

    [Fact]
    public void Haunts_AreTheStrongestThree_NeverHome()
    {
        var home = Guid.NewGuid();
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var places = new Dictionary<Guid, PlaceTie> { [home] = Tie(0.95, Now) };
        foreach (var (id, i) in ids.Select((id, i) => (id, i))) places[id] = Tie(0.3 + i * 0.1, Now);

        Assert.Equal(new[] { ids[4], ids[3], ids[2] }, Belonging.Haunts(places, Now, id => id == home));
    }

    private static SelfState Day(Guid placeId, double warmth) => new()
    {
        Beats = SelfMath.ParseBeats("09:00 at the office"),
        Places = new Dictionary<Guid, PlaceTie> { [placeId] = Tie(warmth, Now) },
    };

    private static string[] PresenceAt(SelfState day, Guid placeId, bool home)
        => SelfRender.Presence(day, 1, Now, Array.Empty<string>(), "You're at the Café.", new SelfRender.At(placeId, home)).Split('\n');

    [Fact]
    public void Presence_AtAHaunt_SaysItRightAfterWhereYouAre()
    {
        var cafe = Guid.NewGuid();
        var lines = PresenceAt(Day(cafe, 0.6), cafe, home: false);
        Assert.Equal("Where you are: You're at the Café.", lines[2]);
        Assert.Equal("This has become one of your places.", lines[3]);
    }

    [Fact]
    public void Presence_WhereTheyArePutOff_SaysIt()
    {
        var cafe = Guid.NewGuid();
        Assert.Equal("You haven't had good times here lately.", PresenceAt(Day(cafe, -0.6), cafe, home: false)[3]);
    }

    [Fact]
    public void Presence_InBetween_AtHome_OrWithoutADay_SaysNothing()
    {
        var cafe = Guid.NewGuid();
        var lines = new[]
        {
            string.Join("\n", PresenceAt(Day(cafe, 0.3), cafe, home: false)),
            string.Join("\n", PresenceAt(Day(cafe, 0.9), cafe, home: true)),
            string.Join("\n", PresenceAt(Day(cafe, -0.9), cafe, home: true)),
            SelfRender.Presence(null, 1, Now, Array.Empty<string>(), "You're at the Café.", new SelfRender.At(cafe, false)),
        };
        Assert.All(lines, text =>
        {
            Assert.DoesNotContain(Belonging.HauntLine, text);
            Assert.DoesNotContain(Belonging.PutOffLine, text);
        });
    }
}
