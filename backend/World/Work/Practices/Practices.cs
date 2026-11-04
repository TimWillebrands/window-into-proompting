using System.Globalization;

namespace PartyTown.World.Work.Practices;

/// <summary>What an Occupation sells when it sells Sessions (ADR 0030). A therapist and a career coach differ only in these words.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Practices.Practice")]
public sealed class Practice
{
    /// <summary>Who it's for: "careers in logistics".</summary>
    [Id(0)] public string Field { get; set; } = string.Empty;
    /// <summary>How they work, in their own plain words.</summary>
    [Id(1)] public string Approach { get; set; } = string.Empty;
    [Id(2)] public int Fee { get; set; }
}

[GenerateSerializer]
public enum SessionStatus
{
    Booked = 0,
    Running = 1,
    Done = 2,
    /// <summary>One of them never turned up or never spoke; no Fee.</summary>
    Missed = 3,
}

/// <summary>A paid booking between a Practitioner and a Client (ADR 0030). The talk itself is ordinary Place talk.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Practices.PracticeSession")]
public sealed class PracticeSession
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid PractitionerId { get; set; }
    [Id(2)] public string PractitionerName { get; set; } = string.Empty;
    [Id(3)] public Guid ClientId { get; set; }
    [Id(4)] public string ClientName { get; set; } = string.Empty;
    /// <summary>The Place talk they met in, once they were together.</summary>
    [Id(5)] public Guid? TalkRoomId { get; set; }
    /// <summary>Why the Client came, in their words.</summary>
    [Id(6)] public string Reason { get; set; } = string.Empty;
    [Id(7)] public long StartsAt { get; set; }
    [Id(8)] public long? StartedAt { get; set; }
    [Id(9)] public long? EndedAt { get; set; }
    [Id(10)] public SessionStatus Status { get; set; }
    /// <summary>Fixed at booking, so a later price change doesn't reach back.</summary>
    [Id(11)] public int Fee { get; set; }
    [Id(12)] public bool Paid { get; set; }
    /// <summary>Last message before they met: Place talk goes on before and after.</summary>
    [Id(13)] public int FromMessageId { get; set; } = -1;
    [Id(14)] public string Workplace { get; set; } = string.Empty;
    /// <summary>The two of them got together; until then it's only a time on the calendar.</summary>
    [Id(15)] public bool Opened { get; set; }
}

/// <summary>The one concrete thing a Session left a Client owing, held by the Practitioner.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Practices.Homework")]
public sealed class Homework
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid SessionId { get; set; }
    [Id(2)] public Guid ClientId { get; set; }
    [Id(3)] public Guid PractitionerId { get; set; }
    [Id(4)] public string Text { get; set; } = string.Empty;
    [Id(5)] public long DueAt { get; set; }
    /// <summary>Null until the next Session looks at it.</summary>
    [Id(6)] public bool? Kept { get; set; }
}

/// <summary>Pure rules of a Practice (ADR 0030): when Sessions happen, when they end, how they read.</summary>
public static class PracticeRules
{
    public static readonly TimeSpan Length = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MinLead = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(7);
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(5);
    /// <summary>Homework left undone stops being owed this long after it was due.</summary>
    public static readonly TimeSpan Lapses = TimeSpan.FromDays(7);
    public const int MaxLines = 16;
    public const int MinFee = 1;
    public const int MaxFee = 500;
    public const int DefaultHomeworkDays = 3;
    public const double MinFit = 0.35;
    public const double StrongFit = 0.6;
    private const int SlotMinutes = 15;

    /// <summary>The first free slot wholly inside their working hours; null within a week means they're full.</summary>
    public static long? NextSlot(Occupation job, IEnumerable<PracticeSession> taken, DateTimeOffset local)
    {
        var busy = taken.Where(s => s.Status is SessionStatus.Booked or SessionStatus.Running).Select(s => s.StartsAt).ToList();
        var length = (long)Length.TotalMilliseconds;
        var earliest = local.Add(MinLead);
        var slot = new DateTimeOffset(earliest.Year, earliest.Month, earliest.Day, earliest.Hour, earliest.Minute / SlotMinutes * SlotMinutes, 0, earliest.Offset);
        if (slot < earliest) slot = slot.AddMinutes(SlotMinutes);
        for (; slot < local.Add(MaxAhead); slot = slot.AddMinutes(SlotMinutes))
        {
            if (!Shifts.During(job, slot) || !Shifts.During(job, slot.Add(Length).AddMinutes(-1))) continue;
            var at = slot.ToUnixTimeMilliseconds();
            if (busy.All(b => b + length <= at || at + length <= b)) return at;
        }
        return null;
    }

    /// <summary>Time up, enough said, or the two of them have gone quiet.</summary>
    public static bool Over(PracticeSession session, int lines, long? lastAt, long now)
    {
        if (session.StartedAt is not { } started) return false;
        return now - started >= Length.TotalMilliseconds
            || lines >= MaxLines
            || now - (lastAt ?? started) >= Quiet.TotalMilliseconds;
    }

    /// <summary>It only took place if both of them said something.</summary>
    public static bool TookPlace(PracticeSession session, IEnumerable<Guid> speakers)
    {
        var said = speakers.ToHashSet();
        return said.Contains(session.PractitionerId) && said.Contains(session.ClientId);
    }

    /// <summary>Within Earshot of each other: settled at the same Spot.</summary>
    public static bool Together(IEnumerable<Places.Body> bodies, Guid a, Guid b)
        => bodies.FirstOrDefault(x => x.ParticipantId == a) is { IsWalking: false } first
            && bodies.FirstOrDefault(x => x.ParticipantId == b) is { IsWalking: false } second
            && first.PlaceId == second.PlaceId && first.SpotId == second.SpotId;

    public static string Opening(PracticeSession session)
        => $"Open the session with {session.ClientName}. They said they came because: {session.Reason}";

    public static string Advert(string name, Occupation job, Practice practice)
        => $"{name}, {job.Title} ({practice.Field}), {Shifts.DaysText(job.Days)} {Shifts.Clock(job.Start)}–{Shifts.Clock(job.End)}, {practice.Fee.ToString(CultureInfo.InvariantCulture)} credits a session";

    /// <summary>How hard a Practice pulls on someone; null when it doesn't speak to them at all.</summary>
    public static int? Pull(double? fit) => fit switch
    {
        null => 0,
        >= StrongFit => 2,
        >= MinFit => 1,
        _ => null,
    };

    /// <summary>Sessions on either side and Homework still open: facts for "# What you owe", the only way a Session reaches speech.</summary>
    /// <param name="approach">How this person works, when they're the Practitioner.</param>
    public static IEnumerable<Owed> Owed(IEnumerable<PracticeSession> sessions, IEnumerable<Homework> homework, Guid self, Func<Guid, string> nameOf, DateTimeOffset now, string? approach = null)
    {
        var ms = now.ToUnixTimeMilliseconds();
        var open = homework.Where(h => h.Kept is null).ToList();
        foreach (var s in sessions.Where(s => s.Status == SessionStatus.Running))
        {
            if (s.ClientId == self) yield return new Owed($"You're seeing {s.PractitionerName} now; you came because: {s.Reason}", s.StartsAt, s.PractitionerId);
            else if (s.PractitionerId == self)
            {
                var last = open.LastOrDefault(h => h.ClientId == s.ClientId && h.PractitionerId == self);
                var how = string.IsNullOrWhiteSpace(approach) ? string.Empty : $" How you work: {approach}";
                var since = last is null ? string.Empty : $" Last time they were going to: {last.Text}";
                yield return new Owed($"You're seeing {s.ClientName} now; they came because: {s.Reason}.{how}{since}", s.StartsAt, s.ClientId);
            }
        }
        foreach (var s in sessions.Where(s => s.Status == SessionStatus.Booked && s.StartsAt >= ms && s.StartsAt - ms <= ResponsibilityRules.Ahead.TotalMilliseconds))
        {
            var at = DateTimeOffset.FromUnixTimeMilliseconds(s.StartsAt).ToLocalTime().ToString("ddd HH:mm", CultureInfo.InvariantCulture);
            if (s.ClientId == self) yield return new Owed($"You have a session with {s.PractitionerName} at {s.Workplace}, {at}.", s.StartsAt, s.PractitionerId);
            else if (s.PractitionerId == self) yield return new Owed($"You're seeing {s.ClientName} for a session, {at}.", s.StartsAt, s.ClientId);
        }
        foreach (var h in open.Where(h => h.ClientId == self && ms - h.DueAt < Lapses.TotalMilliseconds))
            yield return new Owed($"{nameOf(h.PractitionerId)} is expecting you to: {h.Text}", h.DueAt, h.PractitionerId);
    }
}

/// <summary>The Party's Sessions and Homework (key = Party id): a diary of bookings, like the Calendar but by appointment.</summary>
public sealed class SessionsGrain(
    [PersistentState(stateName: "sessions", storageName: "parties")]
    IPersistentState<SessionsState> state)
    : Grain, ISessionsGrain
{
    private const int MaxKept = 200;

    public Task<List<PracticeSession>> GetSessionsAsync() => Task.FromResult(state.State.Sessions.ToList());

    public Task<List<Homework>> GetHomeworkAsync() => Task.FromResult(state.State.Homework.ToList());

    public async Task<PracticeSession> BookAsync(PracticeSession session)
    {
        state.State.Sessions.Add(session);
        if (state.State.Sessions.Count > MaxKept)
            state.State.Sessions = state.State.Sessions.OrderByDescending(s => s.StartsAt).Take(MaxKept).ToList();
        await state.WriteStateAsync();
        return session;
    }

    public async Task<bool> StartAsync(Guid id, long now)
    {
        if (Find(id) is not { Status: SessionStatus.Booked } session) return false;
        session.Status = SessionStatus.Running;
        session.StartedAt = now;
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> OpenAsync(Guid id, Guid talkRoomId, int fromMessageId)
    {
        if (Find(id) is not { Status: SessionStatus.Running, Opened: false } session) return false;
        session.Opened = true;
        session.TalkRoomId = talkRoomId;
        session.FromMessageId = fromMessageId;
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> EndAsync(Guid id, SessionStatus status, long now)
    {
        if (Find(id) is not { Status: SessionStatus.Booked or SessionStatus.Running } session) return false;
        session.Status = status;
        session.EndedAt = now;
        await state.WriteStateAsync();
        return true;
    }

    public async Task NotePaidAsync(Guid id)
    {
        if (Find(id) is not { } session) return;
        session.Paid = true;
        await state.WriteStateAsync();
    }

    public async Task SetHomeworkAsync(Homework? next, Guid clientId, Guid practitionerId, bool? lastKept)
    {
        foreach (var open in state.State.Homework.Where(h => h.ClientId == clientId && h.PractitionerId == practitionerId && h.Kept is null))
            open.Kept = lastKept ?? false;
        if (next is not null) state.State.Homework.Add(next);
        if (state.State.Homework.Count > MaxKept) state.State.Homework.RemoveRange(0, state.State.Homework.Count - MaxKept);
        await state.WriteStateAsync();
    }

    public Task<double?> GetFitAsync(Guid clientId, Guid practitionerId, string day)
        => Task.FromResult(state.State.Fits.TryGetValue(FitKey(clientId, practitionerId, day), out var fit) ? fit : (double?)null);

    public async Task SetFitAsync(Guid clientId, Guid practitionerId, string day, double fit)
    {
        // Only today's reads matter; yesterday's are noise in storage.
        foreach (var stale in state.State.Fits.Keys.Where(k => !k.EndsWith(day, StringComparison.Ordinal)).ToList()) state.State.Fits.Remove(stale);
        state.State.Fits[FitKey(clientId, practitionerId, day)] = fit;
        await state.WriteStateAsync();
    }

    private PracticeSession? Find(Guid id) => state.State.Sessions.FirstOrDefault(s => s.Id == id);

    private static string FitKey(Guid client, Guid practitioner, string day) => $"{client:N}:{practitioner:N}:{day}";
}

[Alias("PartyTown.World.Work.Practices.ISessionsGrain")]
public interface ISessionsGrain : IGrainWithGuidKey
{
    [Alias("GetSessionsAsync")] Task<List<PracticeSession>> GetSessionsAsync();
    [Alias("GetHomeworkAsync")] Task<List<Homework>> GetHomeworkAsync();
    [Alias("BookAsync")] Task<PracticeSession> BookAsync(PracticeSession session);
    /// <summary>False when it isn't waiting to start, so only one caller ever starts it.</summary>
    [Alias("StartAsync")] Task<bool> StartAsync(Guid id, long now);
    /// <summary>They're together in this Place talk; false once it's already opened, so it opens once.</summary>
    [Alias("OpenAsync")] Task<bool> OpenAsync(Guid id, Guid talkRoomId, int fromMessageId);
    /// <summary>False when it already ended, so a Fee is only ever charged once.</summary>
    [Alias("EndAsync")] Task<bool> EndAsync(Guid id, SessionStatus status, long now);
    [Alias("NotePaidAsync")] Task NotePaidAsync(Guid id);
    /// <summary>Closes the pair's open Homework, then opens the new one.</summary>
    [Alias("SetHomeworkAsync")] Task SetHomeworkAsync(Homework? next, Guid clientId, Guid practitionerId, bool? lastKept);
    [Alias("GetFitAsync")] Task<double?> GetFitAsync(Guid clientId, Guid practitionerId, string day);
    [Alias("SetFitAsync")] Task SetFitAsync(Guid clientId, Guid practitionerId, string day, double fit);
}

[GenerateSerializer, Alias("PartyTown.World.Work.Practices.SessionsState")]
public sealed class SessionsState
{
    [Id(0)] public List<PracticeSession> Sessions { get; set; } = new();
    [Id(1)] public List<Homework> Homework { get; set; } = new();
    /// <summary>Today's System One reads of who a Practice speaks to, so it's asked once a day.</summary>
    [Id(2)] public Dictionary<string, double> Fits { get; set; } = new();
}
