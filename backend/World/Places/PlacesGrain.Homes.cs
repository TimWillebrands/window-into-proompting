namespace PartyTown.World.Places;

/// <summary>Sharing a Home (ADR 0035): moving in, moving back, and what's left when someone leaves the World.</summary>
public sealed partial class PlacesGrain
{
    private static readonly string[] HomeSpots = { "sofa", "desk", "kitchen", "bed" };

    public async Task<Place?> BuildHomeAsync(Guid hostId, Guid mateId, string description)
    {
        await SyncCastAsync();
        await SettleAsync();
        if (string.IsNullOrWhiteSpace(description) || hostId == mateId) return null;
        if (!state.State.Bodies.ContainsKey(hostId) || !state.State.Bodies.ContainsKey(mateId)) return null;
        var pair = new[] { hostId, mateId };
        var left = state.State.Places.Where(p => p.Kind == PlaceKinds.Home && (pair.Contains(p.OwnerId ?? Guid.Empty) || p.Living().Any(pair.Contains))).ToList();
        if (left.Any(p => p.Living().Except(pair).Any())) return null;

        var brief = $"{description.Trim()}\nIt's a home two people live in. Spots must include these ids, plus up to two more: {string.Join(", ", HomeSpots)}. Leave out the bed, laptop, coffee maker, dishes and plant: they get placed next to their spots.";
        if (await DesignAsync(brief, false, MissingHomeSpots) is not { } draft) return null;
        var bed = draft.Spots.Single(s => s.Id == "bed");
        bed.Capacity = Math.Max(bed.Capacity, pair.Length);
        bed.At = "on";
        bed.Seat = true;
        var home = new Place
        {
            Id = Guid.NewGuid(), Name = WorldStream.Clip(description.Trim(), 40), Kind = PlaceKinds.Home, OwnerId = hostId,
            Housemates = new List<Guid> { mateId }, Scene = "custom", CustomScene = PlaceDesign.Store(draft.Scene), Spots = draft.Spots,
            Description = WorldStream.Clip(draft.Json["description"]?.ToString() ?? description, 200), Purpose = PlaceSeeds.HomePurpose,
            DistrictId = left.FirstOrDefault(p => p.OwnerId == hostId)?.DistrictId ?? Districts.Centre,
        };
        EnsureFurni(home);
        state.State.Places.Add(home);
        foreach (var body in state.State.Bodies.Values.Where(b => left.Any(p => p.Id == (b.DestPlaceId ?? b.PlaceId))).ToList())
            await RelocateAsync(body, pair.Contains(body.ParticipantId) ? home : state.State.Places.HomeOf(body.ParticipantId) ?? home);
        state.State.Places.RemoveAll(p => left.Contains(p));
        await SaveAsync();
        await PublishPlaceAsync(home);
        await SyncTalkRoomAsync(home.Id);
        return home;
    }

    public async Task<bool> GiveKeyAsync(Guid hostId, Guid toId, Guid placeId)
    {
        if (Find(placeId) is not { } place || !place.IsClosed() || !place.Hosts().Contains(hostId) || place.Hosts().Contains(toId)) return false;
        if (!state.State.Bodies.ContainsKey(toId) || place.Keyholders.Contains(toId)) return false;
        place.Keyholders.Add(toId);
        place.Doorsteps.Remove(toId);
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }

    public async Task<bool> DropKeyAsync(Guid holderId, Guid placeId)
    {
        if (Find(placeId) is not { } place || !place.Keyholders.Remove(holderId)) return false;
        await SaveAsync();
        await PublishPlaceAsync(place);
        return true;
    }

    public Task<string> GetKeyLinesAsync(Guid who)
    {
        var lines = new List<string>();
        foreach (var place in state.State.Places.Where(p => p.Keyholders.Count > 0))
        {
            if (place.Keyholders.Contains(who)) lines.Add($"You have a key to {place.Name}.");
            if (!place.Hosts().Contains(who)) continue;
            foreach (var holder in place.Keyholders)
                lines.Add($"{state.State.Bodies.GetValueOrDefault(holder)?.Name.Split(' ')[0] ?? "Someone"} has a key to {WhereName(place, who)}.");
        }
        return Task.FromResult(lines.Count == 0 ? string.Empty : $"# Keys\n{string.Join("\n", lines.Select(l => $"- {l}"))}\n");
    }

    /// <summary>Having to deal with the door is when wanting a key comes up.</summary>
    private void NoteDoorstep(Place place, Guid visitor)
    {
        var now = Now();
        place.NoteDoorstep(visitor, now);
        if (keyWants is not null && place.Hosts().FirstOrDefault() is var host && host != Guid.Empty)
            _ = keyWants.ConsiderAsync(PartyId, new Relations.Door(place.Id, place.Name, host, visitor, place.DoorstepsLately(visitor, now)));
    }

    private static string? MissingHomeSpots(IReadOnlyList<Spot> spots)
        => HomeSpots.Except(spots.Select(s => s.Id)).ToList() is { Count: > 0 } missing ? $"spots must include: {string.Join(", ", missing)}" : null;

    /// <summary>Straight there, no walk: the Place they were in is going.</summary>
    private async Task RelocateAsync(Body body, Place to)
    {
        body.PlaceId = to.Id;
        body.SpotId = to.Spots[0].Id;
        body.DestPlaceId = null;
        body.DestSpotId = null;
        body.ArriveAt = null;
        body.DepartedAt = null;
        EndPose(body);
        await PublishAsync(body);
    }

    public async Task<bool> MoveInAsync(Guid moverId, Guid hostId)
    {
        await SyncCastAsync();
        await SettleAsync();
        var places = state.State.Places;
        if (places.HomeOf(hostId) is not { } home || places.HomeOf(moverId) is not { } left) return false;
        if (home.Id == left.Id) return true;
        if (left.Living().Any(id => id != moverId)) return false;
        left.Vacated = true;
        left.Guests.Clear();
        left.Knocks.Clear();
        home.Housemates.Add(moverId);
        RenameHome(home);
        await SaveAsync();
        await PublishPlaceAsync(left);
        await PublishPlaceAsync(home);
        await WalkOverAsync(moverId, left, home);
        return true;
    }

    public async Task<bool> SeparateAsync(Guid aId, Guid bId)
    {
        await SettleAsync();
        if (state.State.Places.FirstOrDefault(p => p.LivesIn(aId) && p.LivesIn(bId)) is not { } shared) return false;
        var mover = shared.Housemates.Contains(aId) ? aId : bId;
        shared.Housemates.Remove(mover);
        RenameHome(shared);
        var own = OwnHome(mover) ?? NewHome(mover);
        if (own is not null) own.Vacated = false;
        await SaveAsync();
        await PublishPlaceAsync(shared);
        if (own is null) return true;
        await PublishPlaceAsync(own);
        await WalkOverAsync(mover, shared, own);
        return true;
    }

    /// <summary>Someone who was there goes; someone away at a Locale finds out on the way back.</summary>
    private async Task WalkOverAsync(Guid who, Place from, Place to)
    {
        if (state.State.Bodies.TryGetValue(who, out var body) && (body.DestPlaceId ?? body.PlaceId) == from.Id)
            await MoveAsync(who, to.Id, null);
    }

    /// <summary>Housemates drop off when they've left the World, or the owner has; whoever remains gets their own Home back.</summary>
    private bool ReleaseHousemates(IReadOnlySet<Guid> present)
    {
        var changed = false;
        foreach (var home in state.State.Places.Where(p => p.Housemates.Count > 0).ToList())
        {
            var ownerGone = home.OwnerId is not { } owner || !present.Contains(owner);
            foreach (var mate in home.Housemates.Where(id => ownerGone || !present.Contains(id)).ToList())
            {
                home.Housemates.Remove(mate);
                changed = true;
                if (present.Contains(mate) && (OwnHome(mate) ?? NewHome(mate)) is { } own) own.Vacated = false;
            }
            RenameHome(home);
        }
        return changed;
    }

    private Place? OwnHome(Guid ownerId)
        => state.State.Places.FirstOrDefault(p => p.Kind == PlaceKinds.Home && p.OwnerId == ownerId);

    /// <summary>They gave theirs up for a built Home: a plain one again, as for anyone new.</summary>
    private Place? NewHome(Guid who)
    {
        if (!state.State.Bodies.TryGetValue(who, out var body)) return null;
        var home = PlaceSeeds.Home(who, body.Name);
        EnsureFurni(home);
        state.State.Places.Add(home);
        return home;
    }

    /// <summary>A built Home keeps the name it was given.</summary>
    private void RenameHome(Place home)
    {
        if (home.CustomScene is not null) return;
        var names = home.Living().Select(id => state.State.Bodies.GetValueOrDefault(id)?.Name).OfType<string>().ToList();
        if (names.Count > 0) home.Name = Homes.Named(names);
    }
}
