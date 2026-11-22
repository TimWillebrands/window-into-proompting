using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work;

public sealed record WorkDay(string Title, string Workplace)
{
    public IReadOnlyList<string> Beats { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Did { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> News { get; init; } = Array.Empty<string>();
    public string? Desk { get; init; }
    public string? Frame { get; init; }
    public string? Lens { get; init; }

    public bool Empty => Beats.Count == 0 && Did.Count == 0 && News.Count == 0 && Desk is null;
}

public static class WorkDayRules
{
    public const string Shape = """{"memory": "<one or two sentences, or none>"}""";
    public const int MaxLines = 8;
    private const int LineMax = 200;
    private const int MemoryMax = 400;

    private static readonly HashSet<string> Apps = new(StringComparer.Ordinal) { WorkApp.AppId, DeskApp.AppId, CduScreen.AppId };
    private static readonly HashSet<string> Routine = new(StringComparer.Ordinal) { "start", "finish", "work", "return", "look", "error", "fizzle" };
    private static readonly HashSet<string> OwnNews = new(StringComparer.Ordinal) { "replied", "failed" };

    public static List<string> Did(IEnumerable<WorldActivity> activities, Guid who, long from, long to)
        => activities.Where(a => a.PersonaId == who && a.Ts >= from && a.Ts <= to && Apps.Contains(a.App) && !Routine.Contains(a.Action))
            .Select(a => WorldStream.Clip(a.Summary.StartsWith("You ", StringComparison.Ordinal) ? a.Summary : $"You {a.Summary}", LineMax))
            .Distinct().TakeLast(MaxLines).ToList();

    public static List<string> News(IEnumerable<WorldActivity> activities, Guid? company, long from, long to)
        => company is not { } id ? new List<string>()
            : activities.Where(a => a.PersonaId == Guid.Empty && a.TargetId == id && a.Ts >= from && a.Ts <= to && !OwnNews.Contains(a.Action))
                .Select(a => WorldStream.Clip(a.Summary, LineMax)).Distinct().TakeLast(MaxLines).ToList();

    public static List<string> Beats(IEnumerable<Self.DayBeat> beats, DateTimeOffset from, DateTimeOffset to)
    {
        if (from.Date != to.Date) return new List<string>();
        var (first, last) = (Shifts.Minute(from), Shifts.Minute(to));
        return beats.Where(b => b.Minute >= first && b.Minute <= last).Select(b => WorldStream.Clip(b.Text, LineMax)).ToList();
    }

    public static string Brief(string identity, WorkDay day) => $"""
{identity}

{(day.Frame is null ? string.Empty : $"{day.Frame.TrimEnd()}\n")}Your shift as {day.Title} at {day.Workplace} just ended. Below is what was in it. Say what stays with you from it tonight, in your own plain words: what you did and what it was for, or the one thing that went wrong or right. One or two short sentences, forty words at most, first person, past tense. It's a memory, not a log: no lists, no clock times, no numbers unless one of them is the point.
{(day.Lens is null ? string.Empty : $"{day.Lens}\n")}Most shifts are like the one before. If nothing in this one will stay with you, the memory is "none".
Output ONLY JSON: {Shape}
""";

    public static string Material(WorkDay day)
    {
        var parts = new List<string>();
        if (day.Beats.Count > 0) parts.Add($"Your day while you were at work:\n{Bullets(day.Beats)}");
        if (day.Did.Count > 0) parts.Add($"What you did there:\n{Bullets(day.Did)}");
        if (day.News.Count > 0) parts.Add($"What came in at {day.Workplace}:\n{Bullets(day.News)}");
        if (day.Desk is not null) parts.Add(day.Desk);
        return string.Join("\n\n", parts);
    }

    public static string? Memory(string? raw)
    {
        var text = raw?.Trim().Trim('"').Trim();
        return string.IsNullOrWhiteSpace(text) || text.Length < 12 || text.StartsWith("none", StringComparison.OrdinalIgnoreCase) ? null : WorldStream.Clip(text, MemoryMax);
    }

    public static string Line(WorkDay day, string memory) => $"[Looking back on today's shift as {day.Title} at {day.Workplace}] {memory}";

    private static string Bullets(IEnumerable<string> lines) => string.Join("\n", lines.Select(l => $"- {l}"));
}

public sealed class WorkDays(IGrainFactory grains, IWorldLlm llm, WorldMemory memory, ILogger<WorkDays> logger)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    private const int Window = 300;

    public void Close(Guid partyId, Guid who, Occupation job, ShiftRecord record)
    {
        if (record.StartedAt is not { } from) return;
        var to = record.EndedAt ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _ = Task.Run(async () =>
        {
            try { await RememberAsync(partyId, who, job, record.Day, from, to); }
            catch (Exception ex) { logger.LogWarning(ex, "Remembering a shift failed for {Who}", who); }
        });
    }

    public async Task<string?> RememberAsync(Guid partyId, Guid who, Occupation job, string dayKey, long from, long to)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        if (cast.FirstOrDefault(c => c.Id == who) is not { DefaultDriver: DriverKind.LLM } self) return null;

        var day = await GatherAsync(partyId, self, job, dayKey, from, to);
        if (day.Empty) return null;

        using var cts = new CancellationTokenSource(Budget);
        var json = await llm.For(who).CompleteJsonAsync(JobComplexity.General,
            WorkDayRules.Brief(WorldLlm.Identity(self), day), WorkDayRules.Material(day), cts.Token, 0.8, new[] { "memory" });
        if (WorkDayRules.Memory(json?["memory"]?.ToString()) is not { } kept) return null;

        var line = new MomentLine(self.Id, WorkDayRules.Line(day, kept), to);
        var present = new List<ParticipantView> { new(self.Id, self.Name, self.DefaultDriver) };
        await memory.CaptureAsync(
            new Moment(partyId, MomentSources.Work, job.CompanyId ?? job.PlaceId ?? job.LocaleId ?? who, new List<MomentLine> { line }, 0, present, job.PlaceId),
            CancellationToken.None);
        logger.LogInformation("Shift memory for {Name} ({Job}): {Memory}", self.Name, job.Title, kept);
        return kept;
    }

    private async Task<WorkDay> GatherAsync(Guid partyId, CastMember self, Occupation job, string dayKey, long from, long to)
    {
        var activities = await grains.GetGrain<IWorldClockGrain>(partyId).GetActivitiesAsync(Window, null);
        var company = job.CompanyId is { } id ? await grains.GetGrain<ICompaniesGrain>(partyId).GetAsync(id) : null;
        var lived = await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(partyId, self.Id)).GetStateAsync();
        var day = new WorkDay(job.Title, job.Workplace)
        {
            Beats = lived.DayKey == dayKey
                ? WorkDayRules.Beats(lived.Beats, DateTimeOffset.FromUnixTimeMilliseconds(from).ToLocalTime(), DateTimeOffset.FromUnixTimeMilliseconds(to).ToLocalTime())
                : Array.Empty<string>(),
            Did = WorkDayRules.Did(activities, self.Id, from, to),
            News = WorkDayRules.News(activities, company?.Id, from, to),
            Frame = company is { Mandate.Length: > 0 } ? $"{company.Name} {company.Mandate}." : null,
        };
        if (company is null || !CompanyKinds.IsCdu(company.Kind)) return day;
        var cdu = await grains.GetGrain<ICduGrain>(partyId).GetAsync();
        return day with
        {
            Frame = CduScreen.Calling(company),
            Lens = CduScreen.Lens,
            Desk = CduScreen.ShiftFacts(cdu.Exchanges, company.Id, self.Id, from, to),
        };
    }
}
