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
        => _fx.Service<WorldMove>().MoveAsync(from, "Fresh", who, relations);

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
