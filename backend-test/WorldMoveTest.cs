using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Places;
using PartyTown.World.Relations;
using PartyTown.World.Work;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace BackendTest;

/// <summary>Moving everyone to a fresh World: Nature, Players, looks, picked Relations and the CDU's staff come along; the old World stops.</summary>
public partial class CduWorldTest
{
    private IPartyRootGrain Root => _fx.GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);

    private async Task<(Guid From, Guid Denise, Guid Pieter, Guid Tim, Relation Married, Relation Friends)> LivedInAsync()
    {
        var (from, denise, pieter) = await _fx.CreateWorldAsync();
        var tim = (await _fx.GrainFactory.GetGrain<IWorldClockGrain>(from).ClaimPlayerAsync($"clerk_{Guid.NewGuid():N}", "Tim"))!.Value;
        var relations = _fx.GrainFactory.GetGrain<IRelationsGrain>(from);
        var married = (await relations.DeclareAsync(denise, pieter, RelationKind.Married, "you met at the harbour", false))!;
        var friends = (await relations.DeclareAsync(denise, tim, RelationKind.Friends, null, false))!;
        await HireAsync(from, denise);
        await _fx.GrainFactory.GetGrain<IWorldClockGrain>(from).UpdateAsync(true, 30, 12);
        return (from, denise, pieter, tim, married, friends);
    }

    private Task<Guid?> MoveAsync(Guid from, IReadOnlyCollection<Guid>? who, params Guid[] relations)
        => _fx.Service<WorldMove>().MoveAsync(from, new MovePlan("Fresh", who, relations, null));

    private IPlacesGrain Places(Guid partyId) => _fx.GrainFactory.GetGrain<IPlacesGrain>(partyId);

    private async Task<Place> HomeOf(Guid partyId, Guid owner)
        => (await Places(partyId).GetSnapshotAsync()).Places.Single(p => p.Kind == PlaceKinds.Home && p.OwnerId == owner);

    [Fact]
    public async Task Move_StopsTheOldWorld_AndTheNewOneIsCurrentAndRunsAsItDid()
    {
        var (from, _, _, _, _, _) = await LivedInAsync();

        var to = (await MoveAsync(from, null))!.Value;

        var worlds = await Root.GetWorldsAsync();
        Assert.True(worlds.Single(w => w.Id == from).Stopped);
        Assert.Equal((false, true, "Fresh"), (worlds.Single(w => w.Id == to).Stopped, worlds.Single(w => w.Id == to).Current, worlds.Single(w => w.Id == to).Name));
        Assert.False((await _fx.GrainFactory.GetGrain<IWorldClockGrain>(from).GetStatusAsync()).Running);
        var status = await _fx.GrainFactory.GetGrain<IWorldClockGrain>(to).GetStatusAsync();
        Assert.Equal((true, 30, 12), (status.Running, status.TickSeconds, status.MaxActionsPerHour));
    }

    [Fact]
    public async Task Move_BringsTheCastAndPlayers_ButOnlyThePickedRelations()
    {
        var (from, denise, pieter, tim, married, _) = await LivedInAsync();

        var to = (await MoveAsync(from, null, married.Id))!.Value;

        var cast = (await _fx.GrainFactory.GetGrain<IPartyGrain>(to).GetParty()).Participants;
        Assert.Equal(new[] { denise, pieter, tim }.Order(), cast.Where(p => p.Id != Narrator.PersonaId).Select(p => p.Id).Order());
        Assert.Equal(DriverKind.User, cast.Single(p => p.Id == tim).Driver);
        Assert.Equal(tim, Assert.Single(await _fx.GrainFactory.GetGrain<IWorldClockGrain>(to).GetClaimedAsync()));
        var relation = Assert.Single((await _fx.GrainFactory.GetGrain<IRelationsGrain>(to).GetAsync()).Relations);
        Assert.Equal((RelationKind.Married, "you met at the harbour", married.Since), (relation.Kind, relation.Story, relation.Since));
        Assert.NotEqual(married.Id, relation.Id);
    }

    [Fact]
    public async Task Move_LeavesBehindWhoIsntPicked_AndTheirRelations()
    {
        var (from, denise, pieter, _, married, friends) = await LivedInAsync();

        var to = (await MoveAsync(from, new[] { denise, pieter }, married.Id, friends.Id))!.Value;

        Assert.DoesNotContain((await _fx.GrainFactory.GetGrain<IPartyGrain>(to).GetParty()).Participants, p => p.Name == "Tim");
        Assert.Empty(await _fx.GrainFactory.GetGrain<IWorldClockGrain>(to).GetClaimedAsync());
        Assert.Equal(married.Kind, Assert.Single((await _fx.GrainFactory.GetGrain<IRelationsGrain>(to).GetAsync()).Relations).Kind);
    }

    [Fact]
    public async Task Move_KeepsLooks_SoNobodyIsDrawnAnew()
    {
        var (from, _, _, tim, _, _) = await LivedInAsync();
        await _fx.GrainFactory.GetGrain<IPlacesGrain>(from).GetSnapshotAsync();
        var hair = AppearanceRules.Fields.Single(f => f.Key == "hair").Options.Last();
        await _fx.GrainFactory.GetGrain<IPlacesGrain>(from).SetAppearanceAsync(tim, new Dictionary<string, string?> { ["hair"] = hair });

        var to = (await MoveAsync(from, null))!.Value;

        var body = (await _fx.GrainFactory.GetGrain<IPlacesGrain>(to).GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == tim);
        Assert.Equal(hair, body.Appearance!.Hair);
    }

    [Fact]
    public async Task Move_CarriesTheCduCompany_WithItsStaffOnTheSameHours()
    {
        var (from, denise, _, _, _, _) = await LivedInAsync();

        var to = (await MoveAsync(from, null))!.Value;

        var company = Assert.Single(await _fx.GrainFactory.GetGrain<ICompaniesGrain>(to).GetAllAsync());
        Assert.True(CompanyKinds.IsCdu(company.Kind));
        Assert.Equal(denise, Assert.Single(await _fx.Service<Desks>().StaffAsync(to, company.Id)).Id);
        var job = (await _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(to, denise)).GetAsync()).Job!;
        Assert.Equal(("Cytheurgist", Shifts.EveryDay, company.Id), (job.Title, job.Days, job.CompanyId));
    }

    [Fact]
    public async Task Move_BringsAHumansHomeAsMade_ButPersonasGetNewOnes()
    {
        var (from, denise, _, tim, _, _) = await LivedInAsync();
        var timsHome = await HomeOf(from, tim);
        await Places(from).SetPlaceStyleAsync(tim, timsHome.Id, "#336699", null);
        await Places(from).MoveHomeAsync(timsHome.Id, "Tim's loft", "Noord");
        var denisesHome = await HomeOf(from, denise);

        var to = (await MoveAsync(from, null))!.Value;

        var home = await HomeOf(to, tim);
        Assert.Equal((timsHome.Id, "Tim's loft", "#336699"), (home.Id, home.Name, home.WallColour));
        Assert.Contains((await Places(to).GetSnapshotAsync()).Districts, d => d.Name == "Noord" && d.Id == home.DistrictId);
        Assert.NotEqual(denisesHome.Id, (await HomeOf(to, denise)).Id);
        Assert.Equal(home.Id, (await Places(to).GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == tim).PlaceId);
    }

    [Fact]
    public async Task Move_LeavesAPlaceBehind_WhenItIsntPicked()
    {
        var (from, _, _, tim, _, _) = await LivedInAsync();
        var timsHome = await HomeOf(from, tim);

        var to = (await _fx.Service<WorldMove>().MoveAsync(from, new MovePlan("Fresh", null, Array.Empty<Guid>(), Array.Empty<Guid>())))!.Value;

        Assert.NotEqual(timsHome.Id, (await HomeOf(to, tim)).Id);
    }

    [Fact]
    public void Movable_OffersWhatSomeoneMade_AndSuggestsWhatHumansAuthored()
    {
        var (human, persona) = (Guid.NewGuid(), Guid.NewGuid());
        var names = new Dictionary<Guid, string> { [human] = "Tim", [persona] = "Denise" };
        Place Public(string name, Guid? by = null, long? opened = null, string? scene = null)
            => new() { Id = Guid.NewGuid(), Name = name, Kind = PlaceKinds.Public, MadeBy = by, OpenedAt = opened, Scene = scene };
        var places = new List<Place>
        {
            PlaceSeeds.Public()[0],
            Public("the Observatory"),
            Public("Tim's shop", human),
            Public("Denise's bar", persona),
            Public("the Noord office", opened: 1),
            Public("Noord streets", scene: "district"),
            PlaceSeeds.Home(human, "Tim"),
            PlaceSeeds.Home(persona, "Denise"),
            new() { Id = Guid.NewGuid(), Name = "an office", Kind = PlaceKinds.Locale },
        };

        var movable = WorldMove.Movable(places, new HashSet<Guid> { human }, names);

        Assert.Equal(
            new[]
            {
                ("the Observatory", PlaceOrigin.Author, (string?)null, true),
                ("Tim's shop", PlaceOrigin.Player, "Tim", true),
                ("Denise's bar", PlaceOrigin.Persona, "Denise", false),
                ("the Noord office", PlaceOrigin.Opened, null, false),
                (places[6].Name, PlaceOrigin.Home, "Tim", true),
            },
            movable.Select(m => (m.Name, m.Origin, m.By, m.Suggested)));
    }

    [Fact]
    public void Fresh_KeepsThePlaceAsMade_WithoutWhoWasThereOrWhatTheyLeft()
    {
        var (stays, goes) = (Guid.NewGuid(), Guid.NewGuid());
        var place = new Place
        {
            Name = "the Observatory", Kind = PlaceKinds.Public, MadeBy = goes, WallColour = "#112233", TalkRoomId = Guid.NewGuid(),
            Keyholders = new List<Guid> { stays }, Staff = new List<Guid> { stays }, Lately = "the roof leaked",
            Furni = new List<Furni>
            {
                new() { Id = "telescope", Broken = true, Dirty = 3 },
                new() { Id = "mine", OwnerId = stays },
                new() { Id = "theirs", OwnerId = goes },
            },
        };

        var fresh = WorldMove.Fresh(place, new HashSet<Guid> { stays });

        Assert.Equal(("#112233", (Guid?)null, (Guid?)null, (string?)null), (fresh.WallColour, fresh.MadeBy, fresh.TalkRoomId, fresh.Lately));
        Assert.Empty(fresh.Keyholders);
        Assert.Empty(fresh.Staff);
        Assert.Equal(new[] { "telescope", "mine" }, fresh.Furni.Select(f => f.Id));
        Assert.Equal((false, 0), (fresh.Furni[0].Broken, fresh.Furni[0].Dirty));
    }

    [Fact]
    public async Task Stopping_IsKeptPerWorld_AndStartingAgainClearsIt()
    {
        var (from, _, _) = await _fx.CreateWorldAsync();

        Assert.True(await Root.SetStoppedAsync(from, true));
        Assert.True(await Root.IsStoppedAsync(from));
        Assert.True(await Root.SetStoppedAsync(from, false));
        Assert.False(await Root.IsStoppedAsync(from));
        Assert.False(await Root.SetStoppedAsync(Guid.NewGuid(), true));
    }
}
