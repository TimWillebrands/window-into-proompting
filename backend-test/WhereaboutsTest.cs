using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>ADR 0027: the Day's stretches away from home, and when a Body follows them.</summary>
public class WhereaboutsTest
{
    [Fact]
    public void Parse_KeepsStretchesAwayFromHome_WithAnOptionalDistrict()
    {
        var parsed = SelfMath.ParseWhereabouts("""
            09:00-17:30 | the office in Zuid | Zuid
            - 18:15–19:45 | new: the climbing gym
            20:00-23:00 | home
            23:00-01:00 | a bar | De Pijp
            nonsense line
            """);

        Assert.Equal(2, parsed.Count);
        Assert.Equal((540, 1050, "the office in Zuid", "Zuid"), (parsed[0].Start, parsed[0].End, parsed[0].Place, parsed[0].District));
        Assert.Equal((1095, 1185, "the climbing gym", (string?)null), (parsed[1].Start, parsed[1].End, parsed[1].Place, parsed[1].District));
    }

    [Fact]
    public void OnlyLongResolvedStretches_AreErrands()
    {
        var plan = new List<Whereabout>
        {
            new() { Start = 540, End = 1050, LocaleId = Guid.NewGuid() },
            new() { Start = 1095, End = 1185, LocaleId = Guid.NewGuid() },
            new() { Start = 600, End = 900 },
        };
        Assert.Single(PlacesGrain.Errands(plan));
        Assert.Equal("at work", PlacesGrain.ErrandAs("the office in Zuid"));
        Assert.Equal("out", PlacesGrain.ErrandAs("Mum's house"));
    }

    [Fact]
    public void ABody_GoesOncePerStretch_NotFromCompanyOrAtNight()
    {
        var locale = Guid.NewGuid();
        var stretch = new Whereabout { Start = 540, End = 1050, LocaleId = locale };
        var idle = new Body { PlaceId = Guid.NewGuid() };

        Assert.Equal(PlacesGrain.ErrandMove.Go, PlacesGrain.ErrandStep(idle, stretch, "2026-10-22", company: false, night: false));
        Assert.Equal(PlacesGrain.ErrandMove.Stay, PlacesGrain.ErrandStep(idle, stretch, "2026-10-22", company: true, night: false));
        Assert.Equal(PlacesGrain.ErrandMove.Stay, PlacesGrain.ErrandStep(idle, stretch, "2026-10-22", company: false, night: true));
        Assert.Equal(PlacesGrain.ErrandMove.Stay, PlacesGrain.ErrandStep(idle, null, "2026-10-22", company: false, night: false));

        var cameBack = new Body { PlaceId = Guid.NewGuid(), AwayStretch = "2026-10-22:540" };
        Assert.Equal(PlacesGrain.ErrandMove.Stay, PlacesGrain.ErrandStep(cameBack, stretch, "2026-10-22", company: false, night: false));

        var onShift = new Body { PlaceId = Guid.NewGuid(), AwayAs = "working" };
        Assert.Equal(PlacesGrain.ErrandMove.Stay, PlacesGrain.ErrandStep(onShift, stretch, "2026-10-22", company: false, night: false));
    }

    [Fact]
    public void ABody_ComesBack_OnlyFromWhereTheDaySentIt()
    {
        var locale = Guid.NewGuid();
        var stretch = new Whereabout { Start = 540, End = 1050, LocaleId = locale };
        var away = new Body { PlaceId = locale, AwayAs = "out", AwayByDay = true };

        Assert.Equal(PlacesGrain.ErrandMove.Stay, PlacesGrain.ErrandStep(away, stretch, "d", false, false));
        Assert.Equal(PlacesGrain.ErrandMove.Return, PlacesGrain.ErrandStep(away, null, "d", false, false));

        // A shift moved it on (GoAwayAsync clears AwayByDay), so the Day leaves it alone.
        var atWork = new Body { PlaceId = Guid.NewGuid(), AwayAs = "working", AwayByDay = false };
        Assert.Equal(PlacesGrain.ErrandMove.Stay, PlacesGrain.ErrandStep(atWork, null, "d", false, false));
    }
}
