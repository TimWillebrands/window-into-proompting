using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Work;

/// <summary>
/// A Participant's job in one Party (ADR 0026), key = <c>party:participant</c>. The one writer of who works
/// where and when; a state machine the World clock steps. Side effects (walking, pay) live in <see cref="Workplaces"/>.
/// </summary>
public sealed class OccupationGrain(
    [PersistentState(stateName: "occupation", storageName: "parties")]
    IPersistentState<OccupationState> state,
    IWorldLlm llm,
    ILogger<OccupationGrain> logger)
    : Grain, IOccupationGrain
{
    private const int HistorySize = 14;
    private static readonly TimeSpan SeedRetry = TimeSpan.FromMinutes(10);
    private bool _seeding;

    public Task<OccupationView> GetAsync()
    {
        KickSeed();
        return Task.FromResult(View());
    }

    public async Task<OccupationView> SetAsync(Occupation? job)
    {
        if (job is not null) await PlaceAsync(job);
        state.State.Job = job;
        state.State.Seeded = true;
        if (job is null && state.State.History.LastOrDefault() is { Status: ShiftStatus.On } on) on.Status = ShiftStatus.LeftEarly;
        await state.WriteStateAsync();
        await EmployAsync();
        return View();
    }

    public async Task<ShiftChange> StepAsync(DateTimeOffset local, bool asleep)
    {
        KickSeed();
        if (state.State.Job is not { } job) return ShiftChange.Nothing;
        var today = state.State.History.LastOrDefault();
        var step = Shifts.Step(job, today, local, asleep);
        var day = Shifts.DayKey(local);
        // Only a shift the running world saw come and go can be missed: not one before the job or a paused clock.
        if (step == ShiftStep.None && Shifts.During(job, local) && state.State.SawShiftDay != day)
        {
            state.State.SawShiftDay = day;
            await state.WriteStateAsync();
        }
        if (step == ShiftStep.Miss && state.State.SawShiftDay != day) return ShiftChange.Nothing;
        var now = local.ToUnixTimeMilliseconds();
        switch (step)
        {
            case ShiftStep.Start:
                await SyncPlaceAsync(job);
                Append(new ShiftRecord
                {
                    Day = Shifts.DayKey(local), Status = ShiftStatus.On, StartedAt = now,
                    LateMinutes = Math.Max(0, Shifts.Minute(local) - job.Start - Shifts.GraceMinutes),
                });
                break;
            case ShiftStep.End:
                await SyncPlaceAsync(job);
                today!.Status = ShiftStatus.Done;
                today.EndedAt = now;
                break;
            case ShiftStep.Miss:
                Append(new ShiftRecord { Day = Shifts.DayKey(local), Status = ShiftStatus.Missed });
                break;
            default:
                return ShiftChange.Nothing;
        }
        await state.WriteStateAsync();
        return new ShiftChange(step, job, state.State.History.Last());
    }

    public async Task<ShiftRecord?> LeaveEarlyAsync(DateTimeOffset local)
    {
        if (state.State.History.LastOrDefault() is not { Status: ShiftStatus.On } on) return null;
        on.Status = ShiftStatus.LeftEarly;
        on.EndedAt = local.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
        return on;
    }

    public async Task NotePaidAsync(string day, int amount)
    {
        if (state.State.History.LastOrDefault(r => r.Day == day) is not { } record) return;
        record.Paid = amount;
        await state.WriteStateAsync();
    }

    public Task<string?> FixedTodayAsync(DateTimeOffset local) => Task.FromResult(Shifts.FixedToday(state.State.Job, local));

    /// <summary>An offscreen workplace becomes a Places Locale (ADR 0027); a name that's a drawn Place puts the job onscreen.</summary>
    private async Task PlaceAsync(Occupation job)
    {
        if (job.PlaceId is not null || job.LocaleId is not null) return;
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var places = GrainFactory.GetGrain<Places.IPlacesGrain>(partyId);
        var id = await places.LocaleAsync(job.Workplace, participantId);
        var place = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == id);
        if (place?.Kind == Places.PlaceKinds.Public) job.PlaceId = id;
        else job.LocaleId = id;
        if (place is not null) job.Workplace = place.Name;
    }

    /// <summary>Openings and Closings keep the id (ADR 0027): follow the workplace's current kind and name at each shift edge.</summary>
    private async Task SyncPlaceAsync(Occupation job)
    {
        if ((job.PlaceId ?? job.LocaleId) is not { } id) return;
        try
        {
            var (partyId, _) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
            var place = (await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == id);
            if (place is null) return;
            WorkRules.Sync(job, place);
        }
        catch (Exception ex) { logger.LogDebug(ex, "Workplace sync failed"); }
    }

    /// <summary>Tells the Wallet whether pay comes per shift or daily.</summary>
    private Task EmployAsync()
        => GrainFactory.GetGrain<Economy.IWalletGrain>(this.GetPrimaryKeyString()).SetEmployedAsync(state.State.Job is not null);

    private void Append(ShiftRecord record)
    {
        state.State.History.Add(record);
        if (state.State.History.Count > HistorySize) state.State.History.RemoveRange(0, state.State.History.Count - HistorySize);
    }

    private OccupationView View() => new(state.State.Job, state.State.History.LastOrDefault(), state.State.Seeded);

    /// <summary>First use: read a job off the lived sheet, detached so the clock never waits on an LLM.</summary>
    private void KickSeed()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (state.State.Seeded || _seeding || now - state.State.SeedAttemptAt < SeedRetry.TotalMilliseconds) return;
        _seeding = true;
        state.State.SeedAttemptAt = now;
        _ = SeedAsync();
    }

    private async Task SeedAsync()
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        try
        {
            var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
            // Humans and the Narrator get a job only from an author.
            if (who is null || who.DefaultDriver != DriverKind.LLM)
            {
                state.State.Seeded = true;
                await state.WriteStateAsync();
                return;
            }
            var places = (await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places
                .Where(p => p.Kind == Places.PlaceKinds.Public).ToList();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var json = await llm.CompleteJsonAsync(JobComplexity.General,
                $"""
                {WorldLlm.Identity(who)}

                What does {who.Name} do for work right now? Read it off who they are; don't invent a dream job.
                Places in this world: {string.Join("; ", places.Select(p => p.Name))}.
                If they'd plausibly work at one of these, use its exact name as the workplace. Otherwise name their
                workplace in a few plain words ("the office in Zuid", "a bakery on the corner").
                If they don't have a job (retired, between jobs, studying full time), set job to "none".
                Output ONLY JSON with string fields:
                - job: a plain job title ("barista", "junior accountant") or "none"
                - workplace: where
                - days: which days, e.g. "Mon-Fri", "Tue-Sat", "weekends"
                - hours: e.g. "09:00-17:00"
                """,
                "Their job, as JSON.", cts.Token, 0.4, ["job", "workplace", "days", "hours"]);
            state.State.Job = Parse(json?["job"]?.ToString(), json?["workplace"]?.ToString(), json?["days"]?.ToString(), json?["hours"]?.ToString(), places);
            if (state.State.Job is { } seeded) await PlaceAsync(seeded);
            state.State.Seeded = json is not null;
            await state.WriteStateAsync();
            if (state.State.Seeded) await EmployAsync();
            logger.LogInformation("Occupation for {Name}: {Job}", who.Name, state.State.Job is { } j ? Shifts.Describe(j) : "none");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Occupation seeding failed for {ParticipantId}", participantId);
        }
        finally
        {
            _seeding = false;
        }
    }

    /// <summary>A seeded job, matched to a Place in the Party by name when there is one; null for "none".</summary>
    public static Occupation? Parse(string? title, string? workplace, string? days, string? hours, IReadOnlyList<Places.Place> places)
    {
        title = title?.Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        var where = string.IsNullOrWhiteSpace(workplace) ? "work" : workplace.Trim();
        var place = places.FirstOrDefault(p => Same(p.Name, where));
        var (start, end) = Shifts.ParseHours(hours) ?? (9 * 60, 17 * 60);
        return new Occupation
        {
            Title = title, Workplace = place?.Name ?? where, PlaceId = place?.Id,
            Days = Shifts.ParseDays(days), Start = start, End = end,
        };
    }

    private static bool Same(string a, string b)
    {
        static string Norm(string s) => s.Trim().ToLowerInvariant().Replace("the ", string.Empty);
        return Norm(a) == Norm(b);
    }
}

[Alias("PartyTown.World.Work.IOccupationGrain")]
public interface IOccupationGrain : IGrainWithStringKey
{
    /// <summary>The job and today's shift; kicks off seeding from the sheet on first use.</summary>
    [Alias("GetAsync")] Task<OccupationView> GetAsync();
    /// <summary>Author edit; null = no job. Ends a running shift.</summary>
    [Alias("SetAsync")] Task<OccupationView> SetAsync(Occupation? job);
    /// <summary>One clock step: start, end or miss today's shift when due. Pure state; the caller walks and pays.</summary>
    [Alias("StepAsync")] Task<ShiftChange> StepAsync(DateTimeOffset local, bool asleep);
    /// <summary>Walking out mid-shift; null when not on shift.</summary>
    [Alias("LeaveEarlyAsync")] Task<ShiftRecord?> LeaveEarlyAsync(DateTimeOffset local);
    [Alias("NotePaidAsync")] Task NotePaidAsync(string day, int amount);
    /// <summary>Today's shift as a fixed Day beat, or null. Interleaves: Places → Self → here while this grain awaits Places.</summary>
    [Alias("FixedTodayAsync"), Orleans.Concurrency.AlwaysInterleave] Task<string?> FixedTodayAsync(DateTimeOffset local);
}

[GenerateSerializer, Alias("PartyTown.World.Work.OccupationView")]
public sealed record OccupationView(
    [property: Id(0)] Occupation? Job,
    [property: Id(1)] ShiftRecord? Today,
    [property: Id(2)] bool Seeded)
{
    public bool OnShift => Today is { Status: ShiftStatus.On };
}

[GenerateSerializer, Alias("PartyTown.World.Work.ShiftChange")]
public sealed record ShiftChange(
    [property: Id(0)] ShiftStep Step,
    [property: Id(1)] Occupation? Job,
    [property: Id(2)] ShiftRecord? Record)
{
    public static readonly ShiftChange Nothing = new(ShiftStep.None, null, null);
}

[GenerateSerializer, Alias("PartyTown.World.Work.OccupationState")]
public sealed class OccupationState
{
    [Id(0)] public Occupation? Job { get; set; }
    [Id(1)] public bool Seeded { get; set; }
    [Id(2)] public long SeedAttemptAt { get; set; }
    [Id(3)] public List<ShiftRecord> History { get; set; } = new();
    /// <summary>Day the clock last saw a shift in progress without it starting (asleep): a miss is earned only then.</summary>
    [Id(4)] public string? SawShiftDay { get; set; }
}
