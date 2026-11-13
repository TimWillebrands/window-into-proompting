using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Places;
using PartyTown.World.Relations;
using PartyTown.World.Work;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace PartyTown.World;

/// <summary>Everyone moves to a fresh World: Nature travels, Nurture stays behind but for the Relations picked and the CDU's staff.</summary>
public sealed class WorldMove(IGrainFactory grains, PlayerAuto auto, Desks desks, Cytheurgy cdu, ILogger<WorldMove> logger)
{
    /// <param name="participantIds">Who comes along; null is everyone.</param>
    /// <returns>The new World's id; null when there's no World to move from.</returns>
    public async Task<Guid?> MoveAsync(Guid fromId, string name, IReadOnlyCollection<Guid>? participantIds, IReadOnlyCollection<Guid> relationIds)
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        if (!await root.HasPartyId(fromId)) return null;

        var from = await grains.GetGrain<IPartyGrain>(fromId).GetParty();
        var cast = from.Participants
            .Where(p => p.Id != Narrator.PersonaId && (participantIds is null || participantIds.Contains(p.Id)))
            .ToList();
        var carried = cast.Select(p => p.Id).ToHashSet();
        var oldClock = grains.GetGrain<IWorldClockGrain>(fromId);
        var players = (await oldClock.GetPlayersAsync())
            .Where(kv => carried.Contains(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var toId = Guid.NewGuid();
        await root.AddParty(new PartyInfo { Id = toId, Name = name });
        await grains.GetGrain<IPartyGrain>(toId).SetParticipants(cast);
        var clock = grains.GetGrain<IWorldClockGrain>(toId);
        await clock.AdoptPlayersAsync(players);

        var looks = (await grains.GetGrain<IPlacesGrain>(fromId).GetSnapshotAsync()).Bodies
            .Where(b => carried.Contains(b.ParticipantId) && b.Appearance is not null)
            .ToDictionary(b => b.ParticipantId, b => b.Appearance!);
        await grains.GetGrain<IPlacesGrain>(toId).AdoptLooksAsync(looks);

        var picked = relationIds.ToHashSet();
        var relations = (await grains.GetGrain<IRelationsGrain>(fromId).GetAsync()).Relations
            .Where(r => picked.Contains(r.Id) && carried.Contains(r.AId) && carried.Contains(r.BId))
            .ToList();
        await grains.GetGrain<IRelationsGrain>(toId).AdoptAsync(relations);

        await CarryCduAsync(fromId, toId, carried);

        foreach (var player in cast.Where(p => p.Driver == DriverKind.LLM && players.ContainsValue(p.Id)))
            await auto.SetAsync(toId, player.Id, true, await auto.MissionAsync(fromId, player.Id));

        var was = await oldClock.GetStatusAsync();
        await oldClock.UpdateAsync(false, null, null);
        await root.SetStoppedAsync(fromId, true);
        await clock.UpdateAsync(was.Running, was.TickSeconds, was.MaxActionsPerHour, was.AmbientMinutes);
        await root.SetCurrentAsync(toId);

        logger.LogInformation("World {From} moved to {To}: {Cast} people, {Players} players, {Relations} relations",
            fromId, toId, cast.Count, players.Count, relations.Count);
        return toId;
    }

    /// <summary>The CDU is behind every World, not in one: its Company and staff come along, jobs and the secret too.</summary>
    private async Task CarryCduAsync(Guid fromId, Guid toId, HashSet<Guid> carried)
    {
        foreach (var company in (await grains.GetGrain<ICompaniesGrain>(fromId).GetAllAsync()).Where(c => CompanyKinds.IsCdu(c.Kind)))
        {
            if (await cdu.FoundAsync(toId, company.Name, company.Mandate) is not { } moved) continue;
            foreach (var staff in (await desks.StaffAsync(fromId, company.Id)).Where(s => carried.Contains(s.Id)))
            {
                if ((await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(fromId, staff.Id)).GetAsync()).Job is not { } job) continue;
                await desks.HireAsync(toId, moved.Id, staff.Id, job.Title, job.Days, job.Start, job.End);
                await cdu.KnowAsync(toId, moved, staff.Id, CancellationToken.None);
            }
        }
    }
}
