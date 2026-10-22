using PartyTown.World;
using PartyTown.World.Places;
using PartyTown.World.Work;

namespace BackendTest;

/// <summary>What a Persona owes (ADR 0026): who holds them to it, when, and how it reads.</summary>
public class ResponsibilityTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    private static Gathering Movie(Guid host, long inHours, params Guid[] going) => new()
    {
        Id = Guid.NewGuid(), HostId = host, HostName = host == Ana ? "Ana" : "Me", Title = "Movie night", RoomName = "the Café",
        PlaceId = Guid.NewGuid(), StartsAt = Now.AddHours(inHours).ToUnixTimeMilliseconds(), AttendeeIds = going.ToList(),
    };

    [Fact]
    public void Gatherings_YouSaidYoudComeTo_AreOwedToTheHost()
    {
        var owed = ResponsibilityRules.FromGatherings(new[] { Movie(Ana, 3, Ana, Me) }, Me, Now).Single();
        Assert.Equal(Ana, owed.HolderId);
        Assert.Contains("You said you'd be at \"Movie night\" at the Café", owed.Text);
        Assert.Contains("Ana is hosting", owed.Text);
    }

    [Fact]
    public void Gatherings_YoureHosting_CountWhosGoing()
        => Assert.Contains("1 other going", ResponsibilityRules.FromGatherings(new[] { Movie(Me, 3, Me, Ana) }, Me, Now).Single().Text);

    [Fact]
    public void Gatherings_FarOffPastOrNotYours_AreNotOwed()
    {
        var gatherings = new[] { Movie(Ana, 30, Ana, Me), Movie(Ana, -1, Ana, Me), Movie(Ana, 2, Ana) };
        Assert.Empty(ResponsibilityRules.FromGatherings(gatherings, Me, Now));
    }

    [Fact]
    public void Guests_AtYourHomeWhileYoureThere_MakeYouTheHost()
    {
        var home = new Place { Id = Guid.NewGuid(), Kind = PlaceKinds.Home, OwnerId = Me };
        var snapshot = new PlacesSnapshot
        {
            Places = new() { home },
            Bodies = new() { new Body { ParticipantId = Me, PlaceId = home.Id }, new Body { ParticipantId = Ana, PlaceId = home.Id } },
        };
        var owed = ResponsibilityRules.FromGuests(snapshot, Me, _ => "Ana", Now).Single();
        Assert.Equal("Ana is over at your place; you're the host.", owed.Text);

        snapshot.Bodies[0].PlaceId = Guid.NewGuid();
        Assert.Empty(ResponsibilityRules.FromGuests(snapshot, Me, _ => "Ana", Now));
    }

    [Fact]
    public void Render_PutsWorkFirstThenSoonestAndCapsAtThree()
    {
        var owed = new[] { new Owed("later", 300), new Owed("sooner", 100), new Owed("latest", 400) };
        var text = ResponsibilityRules.Render("Work at the Café starts at 09:00.", owed);
        Assert.Equal("# What you owe\n- Work at the Café starts at 09:00.\n- sooner\n- later\n", text);
        Assert.Equal(string.Empty, ResponsibilityRules.Render(null, Array.Empty<Owed>()));
    }

    [Fact]
    public void Weighs_OnlyMissedOrReallyLateWork()
    {
        Assert.Equal(0.25, ResponsibilityRules.Weighs(new ShiftChange(ShiftStep.Miss, new Occupation(), new ShiftRecord())));
        Assert.Equal(0.4, ResponsibilityRules.Weighs(new ShiftChange(ShiftStep.Start, new Occupation(), new ShiftRecord { LateMinutes = 20 })));
        Assert.Null(ResponsibilityRules.Weighs(new ShiftChange(ShiftStep.Start, new Occupation(), new ShiftRecord { LateMinutes = 5 })));
        Assert.Null(ResponsibilityRules.Weighs(new ShiftChange(ShiftStep.End, new Occupation(), new ShiftRecord())));
    }
}
