using System.Text;
using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.World.Places;

/// <summary>
/// The Places registry and body index of one Party (key = Party id). Single writer of "which Place
/// is Body X in", so one-body-one-place has one owner. Plain persisted state, not event-sourced.
/// </summary>
public sealed class PlacesGrain(
    [PersistentState(stateName: "places", storageName: "parties")]
    IPersistentState<PlacesState> state,
    TimeProvider time,
    ILogger<PlacesGrain> logger)
    : Grain, IPlacesGrain
{
    private static readonly TimeSpan WithinPlace = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan BetweenPlaces = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ActivityWearsOff = TimeSpan.FromMinutes(4);
    private IGrainTimer? _arrivals;

    private Guid PartyId => this.GetPrimaryKey();

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        if (state.State.Places.Count == 0)
        {
            state.State.Places = PlaceSeeds.Public();
            await state.WriteStateAsync(cancellationToken);
        }
        BackfillPrepositions();
        foreach (var b in state.State.Bodies.Values.Where(b => b.ArrivedAt == 0))
            b.ArrivedAt = Now() - (long)TimeSpan.FromHours(1).TotalMilliseconds;
        if (state.State.Bodies.Values.Any(b => b.IsWalking)) WatchArrivals();
    }

    public async Task<PlacesSnapshot> GetSnapshotAsync()
    {
        await SyncCastAsync();
        await SettleAsync();
        return new PlacesSnapshot
        {
            Places = state.State.Places.ToList(),
            Bodies = state.State.Bodies.Values.OrderBy(b => b.Name).ToList(),
            Now = Now(),
        };
    }

    public async Task<string?> GetSituationAsync(Guid participantId)
    {
        await SyncCastAsync();
        await SettleAsync();
        return state.State.Bodies.TryGetValue(participantId, out var body) ? Situation(body) : null;
    }

    public async Task<Body?> MoveAsync(Guid participantId, Guid placeId, string? spotId)
    {
        await SyncCastAsync();
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(participantId, out var body)) return null;
        var place = state.State.Places.FirstOrDefault(p => p.Id == placeId);
        if (place is null || !MayEnter(place, participantId)) return null;
        var spot = PickSpot(place, spotId, participantId);
        if (spot is null) return null;
        if (body.PlaceId == placeId && body.SpotId == spot.Id && !body.IsWalking) return body;

        var leaving = body.IsWalking ? body.DestPlaceId : body.PlaceId;
        var now = Now();
        body.DestPlaceId = placeId;
        body.DestSpotId = spot.Id;
        body.DepartedAt = now;
        body.ArriveAt = now + (long)(body.PlaceId == placeId ? WithinPlace : BetweenPlaces).TotalMilliseconds;
        body.Posture = Posture.Walking;
        await state.WriteStateAsync();

        if (leaving is { } left && left != placeId) await SyncTalkRoomAsync(left);
        WatchArrivals();
        await PublishAsync(body);
        return body;
    }

    public async Task<Body?> JoinAsync(Guid participantId, Guid otherId)
    {
        await SyncCastAsync();
        await SettleAsync();
        if (!state.State.Bodies.TryGetValue(otherId, out var other)) return null;
        return await MoveAsync(participantId, other.DestPlaceId ?? other.PlaceId, other.DestSpotId ?? other.SpotId);
    }

    public Task<Guid?> GetPlaceOfAsync(Guid participantId)
        => Task.FromResult(state.State.Bodies.TryGetValue(participantId, out var b) && !b.IsWalking ? b.PlaceId : (Guid?)null);

    public Task<Guid?> GetPlaceOfTalkRoomAsync(Guid talkRoomId)
        => Task.FromResult(state.State.Places.FirstOrDefault(p => p.TalkRoomId == talkRoomId)?.Id);

    public async Task NoteActivityAsync(Guid participantId, string app)
    {
        if (!state.State.Bodies.TryGetValue(participantId, out var body)) return;
        var atHome = Find(body.PlaceId)?.OwnerId == participantId && !body.IsWalking;
        var activity = DeviceUse(app, atHome);
        if (activity is null) return;
        body.Activity = activity;
        body.ActivityUntil = Now() + (long)ActivityWearsOff.TotalMilliseconds;
        await state.WriteStateAsync();
        await PublishAsync(body);
    }

    /// <summary>Online Apps are used on a device, and that shows on the Body. Phone unless at home.</summary>
    internal static string? DeviceUse(string app, bool atHome) => app switch
    {
        "messenger" => "on their phone, texting",
        "rooms" => "on their phone, in a group chat",
        "board" => atHome ? "at their laptop, on the Board" : "on their phone, scrolling the Board",
        "calendar" => "on their phone, checking the calendar",
        "diary" => "writing in their diary",
        _ => null,
    };

    /// <summary>Spots stored before <see cref="Spot.At"/> existed get it from the seed templates.</summary>
    private void BackfillPrepositions()
    {
        var template = PlaceSeeds.Public().Concat(new[] { PlaceSeeds.Home(Guid.Empty, "x") })
            .SelectMany(p => p.Spots).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().At);
        foreach (var spot in state.State.Places.SelectMany(p => p.Spots))
            if (spot.At == "at" && template.TryGetValue(spot.Id, out var at)) spot.At = at;
    }

    /// <summary>One Body per Participant (Narrator excluded); a Home for each, created on demand.</summary>
    private async Task SyncCastAsync()
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetCastAsync();
        var present = cast.Where(c => c.Id != Narrator.PersonaId).ToList();
        var changed = false;
        foreach (var c in present)
        {
            var home = state.State.Places.FirstOrDefault(p => p.Kind == PlaceKinds.Home && p.OwnerId == c.Id);
            if (home is null)
            {
                home = PlaceSeeds.Home(c.Id, c.Name);
                state.State.Places.Add(home);
                changed = true;
            }
            if (state.State.Bodies.TryGetValue(c.Id, out var body))
            {
                if (body.Name != c.Name || body.IsHuman != (c.DefaultDriver == DriverKind.User))
                {
                    body.Name = c.Name;
                    body.IsHuman = c.DefaultDriver == DriverKind.User;
                    changed = true;
                }
                continue;
            }
            var sofa = home.Spots[0];
            state.State.Bodies[c.Id] = new Body
            {
                ParticipantId = c.Id, Name = c.Name, PlaceId = home.Id, SpotId = sofa.Id,
                Posture = sofa.Seat ? Posture.Sitting : Posture.Standing,
                IsHuman = c.DefaultDriver == DriverKind.User,
                ArrivedAt = Now(),
            };
            changed = true;
        }
        var gone = state.State.Bodies.Keys.Where(id => present.All(c => c.Id != id)).ToList();
        foreach (var id in gone) state.State.Bodies.Remove(id);
        if (!changed && gone.Count == 0) return;
        await state.WriteStateAsync();
        foreach (var placeId in state.State.Places.Where(p => p.TalkRoomId is not null).Select(p => p.Id).ToList())
            await SyncTalkRoomAsync(placeId);
    }

    /// <summary>Bodies whose walk is over arrive: posture from the spot, Place talk cast follows.</summary>
    private async Task SettleAsync()
    {
        var now = Now();
        foreach (var b in state.State.Bodies.Values.Where(b => b.ActivityUntil <= now))
        {
            b.Activity = null;
            b.ActivityUntil = null;
        }
        var arrived = state.State.Bodies.Values.Where(b => b.IsWalking && b.ArriveAt <= now).ToList();
        if (arrived.Count == 0) return;
        foreach (var body in arrived)
        {
            var changedPlace = body.DestPlaceId != body.PlaceId;
            body.PlaceId = body.DestPlaceId!.Value;
            body.SpotId = body.DestSpotId ?? body.SpotId;
            var spot = Find(body.PlaceId)?.Spots.FirstOrDefault(s => s.Id == body.SpotId);
            body.Posture = spot?.Seat == true ? Posture.Sitting : Posture.Standing;
            if (changedPlace) body.ArrivedAt = now;
            body.DestPlaceId = null;
            body.DestSpotId = null;
            body.DepartedAt = null;
            body.ArriveAt = null;
        }
        await state.WriteStateAsync();
        foreach (var placeId in arrived.Select(b => b.PlaceId).Distinct())
            await SyncTalkRoomAsync(placeId);
        foreach (var body in arrived) await PublishAsync(body);
    }

    private void WatchArrivals()
    {
        _arrivals ??= this.RegisterGrainTimer(async () =>
        {
            try { await SettleAsync(); }
            catch (Exception ex) { logger.LogWarning(ex, "Places arrival settle failed"); }
            if (!state.State.Bodies.Values.Any(b => b.IsWalking))
            {
                _arrivals?.Dispose();
                _arrivals = null;
            }
        }, new GrainTimerCreationOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)) { KeepAlive = true });
    }

    /// <summary>The Place talk Room's cast is whoever is there (not on their way out).</summary>
    private async Task SyncTalkRoomAsync(Guid placeId)
    {
        var place = Find(placeId);
        if (place is null) return;
        var occupants = Occupants(placeId).Select(b => b.ParticipantId).ToHashSet();
        if (place.TalkRoomId is null)
        {
            if (occupants.Count == 0) return;
            var room = await GrainFactory.GetGrain<IPartyGrain>(PartyId).CreatePlaceRoomAsync(TalkName(place));
            place.TalkRoomId = room.Id;
            await state.WriteStateAsync();
        }
        var grain = GrainFactory.GetGrain<IChatGroupGrain>(place.TalkRoomId.Value);
        var current = await grain.GetParticipantIdsAsync();
        if (!current.SetEquals(occupants)) await grain.SetParticipantIdsAsync(occupants);
    }

    private IEnumerable<Body> Occupants(Guid placeId)
        => state.State.Bodies.Values.Where(b => b.PlaceId == placeId && (b.DestPlaceId is null || b.DestPlaceId == placeId));

    private static bool MayEnter(Place place, Guid participantId)
        => place.Kind != PlaceKinds.Home || place.OwnerId == participantId;

    private Spot? PickSpot(Place place, string? spotId, Guid participantId)
    {
        int Taken(Spot s) => state.State.Bodies.Values.Count(b => b.ParticipantId != participantId
            && (b.DestPlaceId ?? b.PlaceId) == place.Id && (b.DestSpotId ?? b.SpotId) == s.Id);
        var wanted = place.Spots.FirstOrDefault(s => s.Id == spotId);
        if (wanted is not null && Taken(wanted) < wanted.Capacity) return wanted;
        return place.Spots.Where(s => Taken(s) < s.Capacity).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
    }

    private string Situation(Body body)
    {
        if (body.IsWalking)
        {
            var dest = Find(body.DestPlaceId!.Value);
            var mins = Math.Max(0, (body.ArriveAt!.Value - Now()) / 60_000.0);
            return body.DestPlaceId == body.PlaceId
                ? $"You're walking over to {SpotName(dest, body.DestSpotId)} at {WhereName(dest, body.ParticipantId)}."
                : $"You're on your way to {WhereName(dest, body.ParticipantId)}, {(mins < 0.5 ? "almost there" : $"about {Math.Ceiling(mins)} min away")}.";
        }
        var place = Find(body.PlaceId);
        var sb = new StringBuilder($"You're {AtName(place, body.ParticipantId)}, {PostureAt(place, body)}");
        if (body.Activity is { Length: > 0 } activity) sb.Append($", {Yours(activity)}");
        sb.Append('.');
        var others = Occupants(body.PlaceId).Where(b => b.ParticipantId != body.ParticipantId).ToList();
        if (others.Count == 0)
            sb.Append(" Nobody else is here.");
        else
            sb.Append(" Here: ").Append(string.Join(", ", others.Select(o => o.SpotId == body.SpotId
                ? $"{o.Name} (right next to you{Doing(o)})"
                : $"{o.Name} ({PostureAt(place, o)}{Doing(o)})"))).Append('.');
        return sb.ToString();
    }

    private static string Yours(string activity) => activity.Replace("their", "your");

    private static string Doing(Body b) => b.Activity is { Length: > 0 } a ? $", {a}" : string.Empty;

    private Place? Find(Guid placeId) => state.State.Places.FirstOrDefault(p => p.Id == placeId);

    private static string SpotName(Place? place, string? spotId)
        => place?.Spots.FirstOrDefault(s => s.Id == spotId)?.Name ?? "somewhere";

    private static string PostureAt(Place? place, Body body)
    {
        var spot = place?.Spots.FirstOrDefault(s => s.Id == body.SpotId);
        var name = spot?.Name ?? "somewhere";
        return body.Posture switch
        {
            Posture.Sitting => $"sitting {spot?.At ?? "at"} {name}",
            _ when spot?.At == "in" => $"in {name}",
            _ => $"standing by {name}",
        };
    }

    internal static string WhereName(Place? place, Guid viewerId)
        => place is null ? "somewhere" : place.OwnerId == viewerId ? "your place" : place.Name;

    internal static string AtName(Place? place, Guid viewerId)
        => place is null ? "somewhere" : place.OwnerId == viewerId ? "at home" : $"at {place.Name}";

    private static string TalkName(Place place)
        => place.Name.StartsWith("the ", StringComparison.Ordinal) ? place.Name[4..] : place.Name;

    private Task PublishAsync(Body body)
        => this.GetStreamProvider(Services.Streaming.PartyStreamIds.Provider).PublishWorldAsync(PartyId, "body", body);

    private long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();
}

[Alias("PartyTown.World.Places.IPlacesGrain")]
public interface IPlacesGrain : IGrainWithGuidKey
{
    [Alias("GetSnapshotAsync")] Task<PlacesSnapshot> GetSnapshotAsync();

    /// <summary>One line: where the Participant is and who's around. Null when they have no Body.</summary>
    [Alias("GetSituationAsync")] Task<string?> GetSituationAsync(Guid participantId);

    /// <summary>Start walking to a spot (null = any free one). Null when not allowed or full.</summary>
    [Alias("MoveAsync")] Task<Body?> MoveAsync(Guid participantId, Guid placeId, string? spotId);

    /// <summary>Walk over to another Body's spot.</summary>
    [Alias("JoinAsync")] Task<Body?> JoinAsync(Guid participantId, Guid otherId);

    /// <summary>The Place a Body is settled in; null while walking or without a Body.</summary>
    [Alias("GetPlaceOfAsync")] Task<Guid?> GetPlaceOfAsync(Guid participantId);

    /// <summary>Which Place a Place talk Room belongs to; null for any other Room.</summary>
    [Alias("GetPlaceOfTalkRoomAsync")] Task<Guid?> GetPlaceOfTalkRoomAsync(Guid talkRoomId);

    /// <summary>Someone used an online App; their Body shows the device for a few minutes.</summary>
    [Alias("NoteActivityAsync")] Task NoteActivityAsync(Guid participantId, string app);
}
