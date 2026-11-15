using System.Globalization;
using PartyTown.Grains;
using PartyTown.World.Places;

namespace PartyTown.World.Work;

public sealed record Owed(string Text, long DueAt, Guid? HolderId = null);

public static class ResponsibilityRules
{
    public const int MaxLines = 3;
    public static readonly TimeSpan Ahead = TimeSpan.FromHours(24);

    public static IEnumerable<Owed> FromGatherings(IEnumerable<Gathering> gatherings, Guid self, DateTimeOffset now)
        => gatherings
            .Where(g => g.Status == GatheringStatus.Planned && g.AttendeeIds.Contains(self))
            .Where(g => g.StartsAt >= now.ToUnixTimeMilliseconds() && g.StartsAt - now.ToUnixTimeMilliseconds() <= Ahead.TotalMilliseconds)
            .Select(g => g.HostId == self
                ? new Owed($"You're hosting \"{g.Title}\" {g.Where}, {When(g.StartsAt, now)} ({Going(g.AttendeeIds.Count - 1)}).", g.StartsAt)
                : new Owed($"You said you'd be at \"{g.Title}\" {g.Where}, {When(g.StartsAt, now)}; {g.HostName} is hosting.", g.StartsAt, g.HostId));

    public static IEnumerable<Owed> FromGuests(PlacesSnapshot places, Guid self, Func<Guid, string> nameOf, DateTimeOffset now)
    {
        var home = places.Places.HomeOf(self);
        if (home is null || places.Bodies.FirstOrDefault(b => b.ParticipantId == self) is not { IsWalking: false } me || me.PlaceId != home.Id) yield break;
        foreach (var guest in places.Bodies.Where(b => !home.LivesIn(b.ParticipantId) && b.PlaceId == home.Id && !b.IsWalking))
            yield return new Owed($"{nameOf(guest.ParticipantId)} is over at your place; you're the host.", now.ToUnixTimeMilliseconds(), guest.ParticipantId);
    }

    public static string Render(string? work, IEnumerable<Owed> owed)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(work)) lines.Add(work.Trim());
        lines.AddRange(owed.OrderBy(o => o.DueAt).Select(o => o.Text).Take(MaxLines - lines.Count));
        return lines.Count == 0 ? string.Empty : $"# What you owe\n{string.Join("\n", lines.Select(l => $"- {l}"))}\n";
    }

    public static double? Weighs(ShiftChange change) => change switch
    {
        { Step: ShiftStep.Miss } => 0.25,
        { Step: ShiftStep.Start, Record.LateMinutes: > 15 } => 0.4,
        _ => null,
    };

    public const double ShowedUp = 0.65;
    public const double NoShow = 0.3;

    private static string When(long at, DateTimeOffset now)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime();
        var day = local.Date == now.ToLocalTime().Date ? "today" : local.ToString("dddd", CultureInfo.InvariantCulture);
        return $"{day} {local:HH:mm}";
    }

    private static string Going(int others) => others switch
    {
        0 => "nobody else going yet",
        1 => "1 other going",
        _ => $"{others} others going",
    };
}

public sealed class Responsibilities(IGrainFactory grains, ILogger<Responsibilities> logger)
{
    public async Task<string> RenderAsync(Guid partyId, Guid personaId, DateTimeOffset now)
    {
        var work = string.Empty;
        var owed = new List<Owed>();
        try
        {
            var job = await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, personaId)).GetAsync();
            var snapshot = await grains.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync();
            var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
            string NameOf(Guid id) => cast.FirstOrDefault(c => c.Id == id)?.Name ?? "Someone";
            if (job.Job is { } occupation)
            {
                if (snapshot.Places.FirstOrDefault(p => p.Id == (occupation.PlaceId ?? occupation.LocaleId)) is { } workplace) WorkRules.Sync(occupation, workplace);
                var at = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == personaId) is { IsWalking: false } me ? me.PlaceId : (Guid?)null;
                work = Shifts.Line(occupation, job.Today, now.ToLocalTime(), at is { } place && place == (occupation.PlaceId ?? occupation.LocaleId)) ?? string.Empty;
            }
            owed.AddRange(ResponsibilityRules.FromGuests(snapshot, personaId, NameOf, now));
            owed.AddRange(ResponsibilityRules.FromGatherings(await grains.GetGrain<ICalendarGrain>(partyId).GetGatheringsAsync(), personaId, now));
            var practice = grains.GetGrain<Practices.ISessionsGrain>(partyId);
            owed.AddRange(Practices.PracticeRules.Owed(await practice.GetSessionsAsync(), await practice.GetHomeworkAsync(), personaId, NameOf, now, job.Job?.Practice?.Approach));
            owed.AddRange(Self.PromiseRules.Owed((await grains.GetGrain<Desires.IDesiresGrain>(WorldKeys.Life(partyId, personaId)).GetAsync()).Acquired, NameOf, now.ToLocalTime()));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Responsibilities failed for {Persona}", personaId);
        }
        return ResponsibilityRules.Render(work, owed);
    }

    public async Task<string> RoleAsync(Guid partyId, Guid personaId)
    {
        try
        {
            var places = grains.GetGrain<IPlacesGrain>(partyId);
            var snapshot = await places.GetSnapshotAsync();
            if (snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == personaId) is not { IsWalking: false } me
                || snapshot.Places.FirstOrDefault(p => p.Id == me.PlaceId) is not { } here) return string.Empty;
            var job = (await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, personaId)).GetAsync()).Job;
            var line = Roles.Line(here, Roles.Of(here, personaId, job, await places.GetVisitDaysAsync(personaId)));
            return line is null ? string.Empty : $"# Where you stand\n{line}\n";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Role failed for {Persona}", personaId);
            return string.Empty;
        }
    }

    public async Task WeighShiftAsync(Guid partyId, Guid personaId, ShiftChange change)
    {
        if (ResponsibilityRules.Weighs(change) is not { } better) return;
        try
        {
            await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(partyId, personaId))
                .ShiftValenceAsync(better, change.Step == ShiftStep.Miss ? "missing work" : "being late for work");
        }
        catch (Exception ex) { logger.LogDebug(ex, "Shift weighing failed for {Persona}", personaId); }
    }

    public async Task KeptAsync(Guid partyId, Gathering gathering, IReadOnlyCollection<Guid> came)
    {
        foreach (var id in gathering.AttendeeIds.Where(id => id != gathering.HostId))
        {
            var closer = came.Contains(id) ? ResponsibilityRules.ShowedUp : ResponsibilityRules.NoShow;
            try
            {
                await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(partyId, gathering.HostId)).ShiftWarmthAsync(id, closer);
                if (!came.Contains(id))
                    await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(partyId, id)).ShiftWarmthAsync(gathering.HostId, ResponsibilityRules.NoShow);
            }
            catch (Exception ex) { logger.LogDebug(ex, "Gathering warmth failed for {Persona}", id); }
        }
    }
}
