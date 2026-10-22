using PartyTown.World.Places;
using PartyTown.World.Work;

namespace BackendTest;

/// <summary>Standing in a Place (ADR 0026): owner and staff at the centre, regulars, newcomers at the margin.</summary>
public class RolesTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Place Cafe = new() { Id = Guid.NewGuid(), Name = "the Café", Kind = PlaceKinds.Public };

    [Fact]
    public void Of_RanksOwnerStaffRegularNewcomer()
    {
        var none = new Dictionary<Guid, int>();
        Assert.Equal(PlaceRole.Owner, Roles.Of(new Place { Id = Cafe.Id, MadeBy = Me }, Me, null, none));
        Assert.Equal(PlaceRole.Staff, Roles.Of(Cafe, Me, new Occupation { PlaceId = Cafe.Id }, none));
        Assert.Equal(PlaceRole.Regular, Roles.Of(Cafe, Me, null, new Dictionary<Guid, int> { [Cafe.Id] = 3 }));
        Assert.Equal(PlaceRole.Newcomer, Roles.Of(Cafe, Me, null, new Dictionary<Guid, int> { [Cafe.Id] = 2 }));
    }

    [Fact]
    public void Line_IsSilentAtHomeAndForNewcomers()
    {
        Assert.Equal("You work at the Café.", Roles.Line(Cafe, PlaceRole.Staff));
        Assert.Equal("You're a regular at the Café.", Roles.Line(Cafe, PlaceRole.Regular));
        Assert.Null(Roles.Line(Cafe, PlaceRole.Newcomer));
        Assert.Null(Roles.Line(new Place { Kind = PlaceKinds.Home }, PlaceRole.Owner));
    }

    [Fact]
    public void NewArrivals_AreRecentAndNotYou()
    {
        var now = 10_000_000L;
        var snapshot = new PlacesSnapshot
        {
            Bodies = new()
            {
                new Body { ParticipantId = Me, PlaceId = Cafe.Id, ArrivedAt = now },
                new Body { ParticipantId = Guid.NewGuid(), Name = "fresh", PlaceId = Cafe.Id, ArrivedAt = now - 60_000 },
                new Body { ParticipantId = Guid.NewGuid(), Name = "settled", PlaceId = Cafe.Id, ArrivedAt = now - 3_600_000 },
            },
        };
        Assert.Equal("fresh", Roles.NewArrivals(snapshot, Cafe.Id, Me, now).Single().Name);
    }
}
