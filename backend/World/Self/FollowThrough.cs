using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>
/// Words become deeds (ADR 0021 #29, ADR 0043): every persona line in a chat is read once for what it commits them to.
/// "On my way" walks the Body over now; a meet-up on a set day goes on the Calendar; anything else they said they'd do
/// becomes a Promise, a Desire of theirs that presses on later turns until it's kept, called off, settled off-screen or lapses. Detached and non-fatal.
/// </summary>
public sealed class FollowThrough(ISystemOneJudge judge, IGrainFactory grains, ILogger<FollowThrough> logger, OffStage? offStage = null)
{
    private const int Lines = 4;
    private static readonly TimeSpan ArrivalWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PendingFor = TimeSpan.FromMinutes(20);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _considered = new();

    /// <summary>Start of a Life turn: a last word in a DM not read yet gets read (covers restarts), and promises are kept up.</summary>
    public async Task ConsiderPendingAsync(Guid partyId, Guid personaId)
    {
        try
        {
            var dms = (await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups())
                .Where(r => r.Kind == ChatGroupKinds.Direct && r.MemberIds?.Contains(personaId) == true);
            foreach (var dm in dms)
            {
                var last = (await grains.GetGrain<IChatGroupGrain>(dm.Id).GetRecentMessagesAsync(1)).LastOrDefault();
                if (last?.SenderId == personaId && !string.IsNullOrWhiteSpace(last.Content) && DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0) < PendingFor)
                    Consider(partyId, dm.Id, personaId);
            }
            await KeepUpAsync(partyId, personaId);
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

    private Desires.IDesiresGrain DesiresOf(Guid partyId, Guid personaId) => grains.GetGrain<Desires.IDesiresGrain>(WorldKeys.Life(partyId, personaId));

    private async Task ConsiderAsync(Guid partyId, Guid roomId, Guid speakerId)
    {
        var weigh = judge.For(speakerId);
        var room = (await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups()).FirstOrDefault(r => r.Id == roomId);
        if (room is null) return;
        var recent = (await grains.GetGrain<IChatGroupGrain>(roomId).GetRecentMessagesAsync(Lines * 2))
            .Where(m => !string.IsNullOrWhiteSpace(m.Content)).TakeLast(Lines).ToList();
        if (recent.Count == 0 || recent[^1].SenderId != speakerId) return;
        var direct = room.Kind == ChatGroupKinds.Direct;
        var otherId = direct ? room.MemberIds?.FirstOrDefault(id => id != speakerId) : PromiseRules.Addressee(recent, speakerId);
        if (otherId is not { } other || other == Narrator.PersonaId) return;
        // Each message is weighed once, whichever path saw it first.
        if (!_considered.TryAdd($"{roomId:N}:{recent[^1].MessageId}", 0)) return;

        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        string Name(Guid id) => cast.FirstOrDefault(c => c.Id == id)?.Name ?? "Someone";
        var transcript = string.Join("\n", recent.Select(m => $"{Name(m.SenderId)}: {WorldStream.Clip(m.Content, 240)}"));
        var desires = DesiresOf(partyId, speakerId);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var open = (await desires.GetAsync()).Acquired
            .Where(d => Desires.DesireRules.IsPromise(d) && d.Status == Desires.DesireStatus.Active && d.TargetId == other && !Desires.DesireRules.Lapsed(d, nowMs)).ToList();
        var local = DateTimeOffset.Now;
        var questions = PromiseRules.Questions(Name(speakerId), Name(other), open, local);
        if (direct)
            questions["coming"] = new NoulQuestion($"In their last message, {Name(speakerId)} says they will come over to where {Name(other)} is, now or very soon (for example 'on my way', 'coming down', 'be right there', 'see you in a sec').");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var answers = await weigh.AskAsync(transcript, questions, cts.Token);
        var promised = answers.Noul("promise", 0);
        var coming = answers.Noul("coming", 0);
        logger.LogInformation("Follow-through {Speaker} → {Other}: promise={Promise:F2} coming={Coming:F2} open={Open}", Name(speakerId), Name(other), promised, coming, open.Count);

        for (var i = 0; i < open.Count; i++)
        {
            if (answers.Noul($"kept{i}", 0) >= PromiseRules.Settled) await desires.MarkAsync(open[i].Id, Desires.DesireStatus.Fulfilled);
            else if (answers.Noul($"off{i}", 0) >= PromiseRules.Settled) await desires.MarkAsync(open[i].Id, Desires.DesireStatus.Abandoned);
        }
        var said = recent[^1].Content ?? string.Empty;
        if (promised >= PromiseRules.Made)
        {
            var when = PromiseRules.Top(answers, "when", PromiseRules.Soon);
            var come = coming >= Coming.Threshold || PromiseRules.Top(answers, "kind", PromiseRules.Do) == PromiseRules.Come;
            var meetup = come && when == PromiseRules.SetDay
                ? PromiseRules.Meetup(PromiseRules.TopShare(answers, "day", string.Empty), PromiseRules.TopShare(answers, "part", PromiseRules.Evening),
                    PromiseRules.TopShare(answers, "where", PromiseRules.Elsewhere), local)
                : null;
            if (meetup is not { } plan || !await PlanMeetupAsync(partyId, speakerId, other, plan.StartsAt, plan.Where, Name))
            {
                var asked = recent.LastOrDefault(m => m.SenderId == other)?.Content;
                await desires.AddAsync(PromiseRules.Make(other, come, said, asked, when, local));
            }
        }
        if (direct && coming >= Coming.Threshold) await ComeOverAsync(partyId, speakerId, other, said, Name);
    }

    /// <summary>A meet-up on a set day at one of their Homes is a Gathering both go to (ADR 0043); false when there's no Home to hold it.</summary>
    public async Task<bool> PlanMeetupAsync(Guid partyId, Guid speakerId, Guid otherId, long startsAt, string where, Func<Guid, string> name)
    {
        var hostId = where == PromiseRules.TheirHome ? speakerId : otherId;
        var guestId = hostId == speakerId ? otherId : speakerId;
        var places = (await grains.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places;
        if (PromiseRules.HomeOf(places, hostId) is not { } home) return false;
        var calendar = grains.GetGrain<ICalendarGrain>(partyId);
        if (PromiseRules.Already(await calendar.GetGatheringsAsync(), speakerId, otherId, startsAt) is { } already)
        {
            logger.LogInformation("Follow-through {Speaker} → {Other}: already planned as \"{Title}\"", name(speakerId), name(otherId), already.Title);
            return true;
        }
        var planned = await calendar.PlanAsync(hostId, name(hostId), PromiseRules.Title(name(guestId), name(hostId)), Guid.Empty, home.Name, startsAt, home.Id);
        await calendar.RsvpAsync(planned.Id, guestId, true);
        await grains.GetGrain<IWorldClockGrain>(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = speakerId, PersonaName = name(speakerId), App = "calendar", Action = "plan",
            Summary = $"agreed with {name(otherId)} on \"{planned.Title}\" {home.At} {home.Name}", WithPersonaId = otherId,
        });
        logger.LogInformation("Follow-through {Speaker} → {Other}: planned \"{Title}\" at {Place}", name(speakerId), name(otherId), planned.Title, home.Name);
        return true;
    }

    /// <summary>A come-over due is kept by being there, one to do something is settled off-screen in time (ADR 0055); lapsing is the Desires grain's to notice.</summary>
    private async Task KeepUpAsync(Guid partyId, Guid personaId)
    {
        var desires = DesiresOf(partyId, personaId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var acquired = (await desires.GetAsync()).Acquired;
        offStage?.SettleDue(partyId, personaId, acquired);
        var comings = acquired.Where(d => Desires.DesireRules.IsPromise(d) && d.Coming && Desires.DesireRules.Pressing(d, now)).ToList();
        if (comings.Count == 0) return;
        var bodies = (await grains.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Bodies;
        var me = bodies.FirstOrDefault(b => b.ParticipantId == personaId);
        foreach (var c in comings.Where(c => PromiseRules.Arrived(me, bodies.FirstOrDefault(b => b.ParticipantId == c.TargetId))))
            await desires.MarkAsync(c.Id, Desires.DesireStatus.Fulfilled);
    }

    /// <summary>Kept by being there: the open come-over to them, if any, is fulfilled.</summary>
    private async Task ArrivedAsync(Guid partyId, Guid speakerId, Guid otherId)
    {
        var desires = DesiresOf(partyId, speakerId);
        if (PromiseRules.Open((await desires.GetAsync()).Acquired, otherId, coming: true, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) is { } kept)
            await desires.MarkAsync(kept.Id, Desires.DesireStatus.Fulfilled);
    }

    /// <summary>"On my way": after a natural delay the Body walks over, and once there they get a turn to say hi.</summary>
    private async Task ComeOverAsync(Guid partyId, Guid speakerId, Guid otherId, string said, Func<Guid, string> name)
    {
        var places = grains.GetGrain<Places.IPlacesGrain>(partyId);
        var snapshot = await places.GetSnapshotAsync();
        var me = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == speakerId);
        var them = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == otherId);
        if (me is null || them is null || me.Asleep || them.IsWalking || Coming.AlreadyThere(me, them)) return;

        await Task.Delay(Coming.Delay(said));
        // Things may have changed while they finished up: the other left, or they're already there.
        snapshot = await places.GetSnapshotAsync();
        me = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == speakerId);
        them = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == otherId);
        if (me is null || them is null || me.Asleep || Coming.AlreadyThere(me, them)) return;
        if (await places.JoinAsync(speakerId, otherId) is null) return;
        await ArrivedAsync(partyId, speakerId, otherId);

        var where = snapshot.Places.FirstOrDefault(pl => pl.Id == them.PlaceId) is { } place
            ? place.Spots.FirstOrDefault(s => s.Id == them.SpotId) is { } spot ? $"{spot.At} {spot.Name}" : $"{place.At} {place.Name}"
            : "where they are";
        var clock = grains.GetGrain<IWorldClockGrain>(partyId);
        await clock.RecordAsync(new WorldActivity
        {
            PersonaId = speakerId, PersonaName = name(speakerId), App = "places", Action = "walk",
            Summary = $"heads over to {name(otherId)} {where}, like they said they would", WithPersonaId = otherId,
        });

        // Once they've arrived, a Life turn to actually be there: say hello out loud, put the phone away.
        var until = DateTimeOffset.UtcNow + ArrivalWait;
        while (DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
            var body = (await places.GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == speakerId);
            if (body is { IsWalking: false }) break;
        }
        await clock.TickNowAsync(speakerId, $"You just got to {name(otherId)} {where}, like you said you would in your messages. You're here in person now: say hi out loud.");
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
