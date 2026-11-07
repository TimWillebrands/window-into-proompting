using PartyTown.World;
using PartyTown.World.Places;
using PartyTown.World.Work;

namespace BackendTest;

/// <summary>Workplace doors (ADR 0041): pure rules for who's behind one.</summary>
public class PlaceAccessTest
{
    private static readonly Guid Denise = Guid.NewGuid();
    private static readonly Guid Pieter = Guid.NewGuid();

    private static Place Office(string? access = PlaceAccess.Staff, params Guid[] staff) => new()
    {
        Kind = PlaceKinds.Public, Archetype = "office", Access = access, Staff = staff.ToList(),
    };

    [Fact]
    public void AStaffedOffice_IsClosed_AndItsStaffAnswerTheDoor()
    {
        var office = Office(PlaceAccess.Staff, Denise);
        Assert.True(office.IsClosed());
        Assert.True(office.IsWorkplaceDoor());
        Assert.Equal(new[] { Denise }, office.Hosts());
        Assert.False(office.Keyed());
    }

    [Fact]
    public void NobodyWorkingThere_OrAnOpenDoor_LeavesItOpen()
    {
        Assert.False(Office(PlaceAccess.Staff).IsClosed());
        Assert.False(Office(PlaceAccess.Open, Denise).IsClosed());
        Assert.False(Office(null, Denise).IsClosed());
        Assert.Empty(Office(PlaceAccess.Open, Denise).Hosts());
    }

    [Fact]
    public void ByAppointment_IsADoorToo()
    {
        Assert.True(Office(PlaceAccess.Appointment, Denise).IsWorkplaceDoor());
        Assert.Equal("by appointment", PlaceAccess.Words(PlaceAccess.Appointment));
    }

    [Fact]
    public void AClaimedPrivatePlace_StaysItsMakersToKey_EvenWithStaff()
    {
        var studio = Office(PlaceAccess.Staff, Pieter);
        studio.Private = true;
        studio.MadeBy = Denise;
        Assert.True(studio.Keyed());
        Assert.False(studio.IsWorkplaceDoor());
        Assert.Equal(new[] { Denise }, studio.Hosts());
    }

    [Fact]
    public void Kits_GiveOfficesAndStudiosAStaffDoor_ClinicsAppointments_TheRestNone()
    {
        Assert.Equal(PlaceAccess.Staff, Kits.Of("office").Access);
        Assert.Equal(PlaceAccess.Staff, Kits.Of("studio").Access);
        Assert.Equal(PlaceAccess.Appointment, Kits.Of("clinic").Access);
        Assert.Null(Kits.Of("restaurant").Access);
        Assert.Null(Kits.Of("cafe").Access);
        Assert.Null(Kits.Place.Access);
    }

    [Fact]
    public void APractice_OpensAsAClinic() => Assert.Equal("clinic", Kits.FromName("a counselling practice on the Herengracht"));
}

/// <summary>Workplace doors in a running World (ADR 0041).</summary>
public partial class PlacesTest
{
    /// <summary>An office Denise works at (noon, on shift); Pieter's at home.</summary>
    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Place Office)> OfficeAsync(string archetype = "office", string name = "the Works office")
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var office = (await places.OpenAsync(await places.LocaleAsync(name, denise), archetype, "author"))!;
        await _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise))
            .SetAsync(new Occupation { Title = "engineer", Workplace = office.Name, PlaceId = office.Id, Days = 127, Start = 0, End = 1439 });
        await places.MoveAsync(denise, office.Id, null);
        await BodyOf(partyId, denise);
        return (partyId, denise, pieter, await Fresh(partyId, office.Id));
    }

    [Fact]
    public async Task AnOffice_TakesItsStaffFromOccupations_AndItsKitsDoor()
    {
        var (_, denise, _, office) = await OfficeAsync();

        Assert.Equal(new[] { denise }, office.Staff);
        Assert.Equal(PlaceAccess.Staff, office.Access);
        Assert.True(office.IsClosed());
    }

    [Fact]
    public async Task AStranger_CantWalkIntoAnOffice_ButStaffCan()
    {
        var (partyId, denise, pieter, office) = await OfficeAsync();
        var places = _fx.Places(partyId);

        Assert.Null(await places.MoveAsync(pieter, office.Id, null));
        Assert.NotNull(await places.MoveAsync(denise, office.Id, "coffee"));
    }

    [Fact]
    public async Task StaffingAnOffice_SendsAStrangerAlreadyThereHome()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var office = (await places.OpenAsync(await places.LocaleAsync("the Works office", denise), "office", "author"))!;
        await places.MoveAsync(pieter, office.Id, null);
        await BodyOf(partyId, pieter);

        await places.WorkAtAsync(denise, office.Id);

        Assert.Equal((await HomeOwnedBy(partyId, pieter)).Id, (await BodyOf(partyId, pieter)).PlaceId);
    }

    [Fact]
    public async Task LosingTheJob_TakesThemOffTheStaff_AndTheDoorOpensWithNobodyLeft()
    {
        var (partyId, denise, pieter, office) = await OfficeAsync();

        await _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise)).SetAsync(null);

        var after = await Fresh(partyId, office.Id);
        Assert.Empty(after.Staff);
        Assert.False(after.IsClosed());
        Assert.NotNull(await _fx.Places(partyId).MoveAsync(pieter, office.Id, null));
    }

    [Fact]
    public async Task TheAccessList_LetsSomeoneComeAndGo_AndTakingThemOffShowsThemOut()
    {
        var (partyId, _, pieter, office) = await OfficeAsync();
        var places = _fx.Places(partyId);

        await places.SetAccessAsync(office.Id, PlaceAccess.Staff, new List<Guid> { pieter });
        Assert.NotNull(await places.MoveAsync(pieter, office.Id, null));
        await BodyOf(partyId, pieter);

        await places.SetAccessAsync(office.Id, PlaceAccess.Staff, new List<Guid>());
        Assert.Equal((await HomeOwnedBy(partyId, pieter)).Id, (await BodyOf(partyId, pieter)).PlaceId);
    }

    [Fact]
    public async Task TheAuthor_CanOpenTheDoorToAnyone()
    {
        var (partyId, _, pieter, office) = await OfficeAsync();
        var places = _fx.Places(partyId);

        Assert.NotNull(await places.SetAccessAsync(office.Id, PlaceAccess.Open, new List<Guid>()));
        Assert.NotNull(await places.MoveAsync(pieter, office.Id, null));
        Assert.Null(await places.SetAccessAsync(office.Id, "members", new List<Guid>()));
    }

    [Fact]
    public async Task AKnockAtTheOffice_ReachesStaff_WhoCanLetThemIn()
    {
        var (partyId, denise, pieter, office) = await OfficeAsync();
        var places = _fx.Places(partyId);

        Assert.True(await places.KnockAsync(pieter, office.Id));
        Assert.Contains(pieter, await places.GetKnocksAsync(denise));
        Assert.True(await places.LetInAsync(denise, pieter));

        Assert.Equal(office.Id, (await BodyOf(partyId, pieter)).PlaceId);
    }

    [Fact]
    public async Task InsideAnOffice_StaffSeeWhoDoesntWorkThere_AndVisitorsThatTheyreVisiting()
    {
        var (partyId, denise, pieter, office) = await OfficeAsync();
        var places = _fx.Places(partyId);
        await places.KnockAsync(pieter, office.Id);
        await places.LetInAsync(denise, pieter);
        await BodyOf(partyId, pieter);

        Assert.Contains("The Works office is staff only; Pieter (let in) doesn't work here.", await places.GetSituationAsync(denise));
        Assert.Contains("You don't work here: The Works office is staff only, and you were let in.", await places.GetSituationAsync(pieter));
    }

    [Fact]
    public async Task OfficeKeys_ArentStaffsToGive()
    {
        var (partyId, denise, pieter, office) = await OfficeAsync();
        Assert.False(await _fx.Places(partyId).GiveKeyAsync(denise, pieter, office.Id));
    }

    [Fact]
    public async Task AnAppointment_LetsTheClientIn_ButOnlyWhenStaffMadeIt()
    {
        var (partyId, denise, pieter, clinic) = await OfficeAsync("clinic", "Denise's practice");
        var places = _fx.Places(partyId);
        Assert.Equal(PlaceAccess.Appointment, clinic.Access);

        await places.ExpectAsync(clinic.Id, pieter, new List<Guid> { pieter });
        Assert.Null(await places.MoveAsync(pieter, clinic.Id, null));

        await places.ExpectAsync(clinic.Id, denise, new List<Guid> { pieter });
        Assert.NotNull(await places.MoveAsync(pieter, clinic.Id, null));
    }

    [Fact]
    public async Task AStaffedCafe_StaysOpen()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");

        await places.WorkAtAsync(denise, cafe.Id);

        Assert.Equal(PlaceAccess.Open, (await Fresh(partyId, cafe.Id)).Access);
        Assert.NotNull(await places.MoveAsync(pieter, cafe.Id, null));
    }

    [Fact]
    public async Task AStranger_IsOfferedAKnock_NotAWalkIn_AndStaffInviteNobodyOver()
    {
        var (partyId, denise, pieter, _) = await OfficeAsync();
        var app = new PlacesApp(_fx.GrainFactory);

        var stranger = (await app.PerceiveAsync(Life(partyId, pieter, (denise, "Denise"), (pieter, "Pieter")), default))!;
        Assert.DoesNotContain(stranger.Affordances, a => a.Verb == "go" && a.Label.Contains("Works"));
        Assert.Contains(stranger.Affordances, a => a.Verb == "knock" && a.Label == "Knock at the Works office, staff only (Denise's in)");

        var staff = (await app.PerceiveAsync(Life(partyId, denise, (denise, "Denise"), (pieter, "Pieter")), default))!;
        Assert.DoesNotContain(staff.Affordances, a => a.Verb == "invite");
    }
}

/// <summary>System Two's say on System One's pick (ADR 0034).</summary>
public class LifeChoiceTest
{
    [Theory]
    [InlineData("""{"thought": "not tonight", "intent": "stay in", "decline": true}""", true)]
    [InlineData("""{"thought": "not tonight", "intent": "stay in", "decline": "true"}""", true)]
    [InlineData("""{"thought": "go", "intent": "head over", "decline": false}""", false)]
    [InlineData("""{"thought": "go", "intent": "head over"}""", false)]
    public void Declined_ReadsSystemTwosNo(string json, bool declined)
        => Assert.Equal(declined, PartyTown.World.LifeChoice.Declined(System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject()));

    [Fact]
    public void Declined_WithNothingVoiced_IsNoDecline() => Assert.False(PartyTown.World.LifeChoice.Declined(null));
}
