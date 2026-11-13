using System.Text.Json.Serialization;
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
    /// <returns>The new World's id; null when there's no World to move from.</returns>
    public async Task<Guid?> MoveAsync(Guid fromId, MovePlan plan)
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        if (!await root.HasPartyId(fromId)) return null;

        var from = await grains.GetGrain<IPartyGrain>(fromId).GetParty();
        var cast = from.Participants
            .Where(p => p.Id != Narrator.PersonaId && (plan.ParticipantIds is null || plan.ParticipantIds.Contains(p.Id)))
            .ToList();
        var carried = cast.Select(p => p.Id).ToHashSet();
        var oldClock = grains.GetGrain<IWorldClockGrain>(fromId);
        var players = (await oldClock.GetPlayersAsync())
            .Where(kv => carried.Contains(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var town = await grains.GetGrain<IPlacesGrain>(fromId).GetSnapshotAsync();

        var toId = Guid.NewGuid();
        await root.AddParty(new PartyInfo { Id = toId, Name = plan.Name });
        await grains.GetGrain<IPartyGrain>(toId).SetParticipants(cast);
        var clock = grains.GetGrain<IWorldClockGrain>(toId);
        await clock.AdoptPlayersAsync(players);

        // Places before looks: bringing someone's body in makes them a Home unless theirs is already there.
        var placeIds = plan.PlaceIds?.ToHashSet()
            ?? Movable(town.Places, await grains.HumansAsync(fromId), Names(from)).Where(m => m.Suggested).Select(m => m.Id).ToHashSet();
        var places = town.Places
            .Where(p => placeIds.Contains(p.Id) && p.Kind != PlaceKinds.Locale && (p.Kind != PlaceKinds.Home || carried.Contains(p.OwnerId ?? Guid.Empty)))
            .Select(p => Fresh(p, carried))
            .ToList();
        await grains.GetGrain<IPlacesGrain>(toId).AdoptPlacesAsync(places, town.Districts);
        var looks = town.Bodies
            .Where(b => carried.Contains(b.ParticipantId) && b.Appearance is not null)
            .ToDictionary(b => b.ParticipantId, b => b.Appearance!);
        await grains.GetGrain<IPlacesGrain>(toId).AdoptLooksAsync(looks);

        var picked = plan.RelationIds.ToHashSet();
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

        logger.LogInformation("World {From} moved to {To}: {Cast} people, {Players} players, {Relations} relations, {Places} places",
            fromId, toId, cast.Count, players.Count, relations.Count, places.Count);
        return toId;
    }

    /// <summary>The Places in this World a Move can bring along.</summary>
    public async Task<List<MovablePlace>> MovablePlacesAsync(Guid fromId)
    {
        var town = await grains.GetGrain<IPlacesGrain>(fromId).GetSnapshotAsync();
        return Movable(town.Places, await grains.HumansAsync(fromId), Names(await grains.GetGrain<IPartyGrain>(fromId).GetParty()));
    }

    private static readonly HashSet<string> Seeded = PlaceSeeds.Public().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Places someone made, rather than the town seeding or drawing them; only humans' Homes, Personas furnish theirs again.</summary>
    public static List<MovablePlace> Movable(IEnumerable<Place> places, IReadOnlySet<Guid> humans, IReadOnlyDictionary<Guid, string> names)
    {
        var movable = new List<MovablePlace>();
        foreach (var p in places)
        {
            if (p.Kind == PlaceKinds.Home)
            {
                if (p.OwnerId is { } owner && humans.Contains(owner))
                    movable.Add(new MovablePlace(p.Id, p.Name, PlaceOrigin.Home, owner, names.GetValueOrDefault(owner), true));
                continue;
            }
            if (p.Kind != PlaceKinds.Public || Seeded.Contains(p.Name) || p.Scene == "district") continue;
            var origin = p.OpenedAt is not null ? PlaceOrigin.Opened
                : p.MadeBy is not { } by ? PlaceOrigin.Author
                : humans.Contains(by) ? PlaceOrigin.Player
                : PlaceOrigin.Persona;
            var maker = p.MadeBy is { } m ? names.GetValueOrDefault(m) : null;
            movable.Add(new MovablePlace(p.Id, p.Name, origin, p.MadeBy, maker, origin is PlaceOrigin.Author or PlaceOrigin.Player));
        }
        return movable;
    }

    /// <summary>The Place as it was made: who was in it, knocked, held keys, worked or lived along there stays behind.</summary>
    public static Place Fresh(Place p, IReadOnlySet<Guid> carried)
    {
        p.TalkRoomId = null;
        p.Guests.Clear();
        p.Knocks.Clear();
        p.Keyholders.Clear();
        p.Doorsteps.Clear();
        p.Staff.Clear();
        p.Allowed.Clear();
        p.Housemates.Clear();
        p.Vacated = false;
        p.Lately = null;
        p.LatelyAt = 0;
        p.MentionedAt = null;
        p.Dormant = false;
        if (p.MadeBy is { } by && !carried.Contains(by)) p.MadeBy = null;
        p.Furni.RemoveAll(f => f.OwnerId is { } owner && !carried.Contains(owner));
        foreach (var f in p.Furni)
        {
            f.Broken = false;
            f.Dirty = 0;
            f.OnUntil = null;
            f.Playing = null;
            f.PlayedBy = null;
        }
        return p;
    }

    private static Dictionary<Guid, string> Names(PartyInfo party) => party.Participants.ToDictionary(p => p.Id, p => p.Name);

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

/// <summary>Who comes along, and which of the old World's Relations and Places.</summary>
/// <param name="ParticipantIds">Null is everyone.</param>
/// <param name="PlaceIds">Null is the ones someone authored, see <see cref="WorldMove.Movable"/>.</param>
public sealed record MovePlan(string Name, IReadOnlyCollection<Guid>? ParticipantIds, IReadOnlyCollection<Guid> RelationIds, IReadOnlyCollection<Guid>? PlaceIds);

/// <summary>Where a Place came from: the author, a Player, a Persona, the town growing, or a Player's Home.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlaceOrigin>))]
public enum PlaceOrigin { Author, Player, Persona, Opened, Home }

/// <summary>A Place a Move can bring; <paramref name="Suggested"/> when someone authored it.</summary>
/// <param name="ById">Who made it, or whose Home it is.</param>
public sealed record MovablePlace(Guid Id, string Name, PlaceOrigin Origin, Guid? ById, string? By, bool Suggested);
