using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>
/// Words become deeds (ADR 0021): when a persona says in a DM that they're coming over ("coming down in a bit",
/// "on my way"), their Body actually walks to the other person shortly after, and once there they get a Life turn
/// to say hello. Detached and non-fatal.
/// </summary>
public sealed class FollowThrough(ISystemOneJudge judge, IGrainFactory grains, ILogger<FollowThrough> logger)
{
    private const int Lines = 4;
    private static readonly TimeSpan ArrivalWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(20);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _considered = new();

    /// <summary>Start of a Life turn: a promise to come over that's still the last word in a DM gets kept (covers restarts).</summary>
    public async Task ConsiderPendingAsync(Guid partyId, Guid personaId)
    {
        try
        {
            var dms = (await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups())
                .Where(r => r.Kind == ChatGroupKinds.Direct && r.MemberIds?.Contains(personaId) == true);
            foreach (var dm in dms)
            {
                var last = (await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(1)).LastOrDefault();
                if (last?.SenderId == personaId && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0) < PendingFor)
                    Consider(partyId, dm.Id, personaId);
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "Pending follow-through check failed for {Persona}", personaId); }
    }

    /// <summary>Called after a persona's line lands in a Room or DM; fire-and-forget.</summary>
    public void Consider(Guid partyId, Guid roomId, Guid speakerId)
        => _ = Task.Run(async () =>
        {
            try { await ConsiderAsync(partyId, roomId, speakerId); }
            catch (Exception ex) { logger.LogDebug(ex, "Follow-through failed for {Speaker}", speakerId); }
        });

    private async Task ConsiderAsync(Guid partyId, Guid roomId, Guid speakerId)
    {
        if (!judge.Enabled) return;
        var room = (await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups()).FirstOrDefault(r => r.Id == roomId);
        if (room is not { Kind: ChatGroupKinds.Direct } || room.MemberIds?.FirstOrDefault(id => id != speakerId) is not { } otherId) return;

        var places = grains.GetGrain<Places.IPlacesGrain>(partyId);
        var snapshot = await places.GetSnapshotAsync();
        var me = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == speakerId);
        var them = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == otherId);
        if (me is null || them is null || me.Asleep || them.IsWalking) return;
        if (Coming.AlreadyThere(me, them)) return;

        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        string Name(Guid id) => cast.FirstOrDefault(c => c.Id == id)?.Name ?? "Someone";
        var recent = (await grains.GetGrain<IChatGroupGrain>(roomId).GetRecentMessagesAsync(Lines * 2))
            .Where(m => !string.IsNullOrWhiteSpace(m.Content)).TakeLast(Lines).ToList();
        if (recent.Count == 0 || recent[^1].SenderId != speakerId) return;
        // Each message is weighed once, whichever path saw it first.
        if (!_considered.TryAdd($"{roomId:N}:{recent[^1].MessageId}", 0)) return;
        var transcript = string.Join("\n", recent.Select(m => $"{Name(m.SenderId)}: {WorldStream.Clip(m.Content, 240)}"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var answers = await judge.AskAsync(transcript, new Dictionary<string, SystemOneQuestion>
        {
            ["coming"] = new NoulQuestion($"In their last message, {Name(speakerId)} says they will come over to where {Name(otherId)} is, now or very soon (for example 'on my way', 'coming down', 'be right there', 'see you in a sec')."),
        }, cts.Token);
        var p = answers.Noul("coming", 0);
        logger.LogInformation("Follow-through {Speaker} → {Other}: coming={P:F2}", Name(speakerId), Name(otherId), p);
        if (p < Coming.Threshold) return;

        await Task.Delay(Coming.Delay(recent[^1].Content));
        // Things may have changed while they finished up: the other left, or they're already there.
        snapshot = await places.GetSnapshotAsync();
        me = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == speakerId);
        them = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == otherId);
        if (me is null || them is null || me.Asleep || Coming.AlreadyThere(me, them)) return;
        if (await places.JoinAsync(speakerId, otherId) is null) return;

        var where = snapshot.Places.FirstOrDefault(pl => pl.Id == them.PlaceId) is { } place
            ? place.Spots.FirstOrDefault(s => s.Id == them.SpotId) is { } spot ? $"{spot.At} {spot.Name}" : $"{place.At} {place.Name}"
            : "where they are";
        var clock = grains.GetGrain<IWorldClockGrain>(partyId);
        await clock.RecordAsync(new WorldActivity
        {
            PersonaId = speakerId, PersonaName = Name(speakerId), App = "places", Action = "walk",
            Summary = $"heads over to {Name(otherId)} {where}, like they said they would", WithPersonaId = otherId,
        });

        // Once they've arrived, a Life turn to actually be there: say hello out loud, put the phone away.
        var until = DateTimeOffset.UtcNow + ArrivalWait;
        while (DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var body = (await places.GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == speakerId);
            if (body is { IsWalking: false }) break;
        }
        await clock.TickNowAsync(speakerId, $"You just got to {Name(otherId)} {where}, like you said you would in your messages. You're here in person now: say hi out loud.");
    }
}

/// <summary>Pure rules for coming over.</summary>
public static class Coming
{
    public const double Threshold = 0.6;

    public static bool AlreadyThere(Places.Body me, Places.Body them)
        => (me.DestPlaceId ?? me.PlaceId) == them.PlaceId && (me.DestSpotId ?? me.SpotId) == them.SpotId;

    /// <summary>"in a bit" is a minute or two of finishing up; "on my way" is now.</summary>
    public static TimeSpan Delay(string? said)
    {
        var t = (said ?? string.Empty).ToLowerInvariant();
        string[] later = { "in a bit", "in a minute", "in a sec", "in a moment", "few min", "after this", "once i", "when i" };
        return later.Any(t.Contains) ? TimeSpan.FromSeconds(45 + Random.Shared.Next(45)) : TimeSpan.FromSeconds(5 + Random.Shared.Next(10));
    }
}
