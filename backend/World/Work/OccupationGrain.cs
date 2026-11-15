using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Work;

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
    private bool _tailoring;

    public Task<OccupationView> GetAsync()
    {
        KickSeed();
        KickClothes();
        return Task.FromResult(View());
    }

    public async Task<OccupationView> SetAsync(Occupation? job)
    {
        var clothes = state.State.Job?.Clothes;
        if (job is not null)
        {
            WorkRules.Carry(job, state.State.Job);
            WorkRules.CarryClothes(job, state.State.Job);
            await PlaceAsync(job);
        }
        state.State.Job = job;
        state.State.Seeded = true;
        if (job is null && state.State.History.LastOrDefault() is { Status: ShiftStatus.On } on) on.Status = ShiftStatus.LeftEarly;
        await state.WriteStateAsync();
        await EmployAsync();
        await StaffAsync();
        if (job?.Clothes != clothes) await DressForShiftAsync();
        return View();
    }

    public async Task<OccupationView> SetClothesAsync(Places.WorkClothes? clothes)
    {
        if (state.State.Job is not { } job) return View();
        if (clothes is not null) (clothes.For, clothes.Made) = (job.Title, Places.WorkClothesRules.Made);
        job.Clothes = clothes;
        state.State.ClothesAttemptAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
        await DressForShiftAsync();
        return View();
    }

    public async Task ReseedAsync()
    {
        if (state.State.Job is not null) return;
        state.State.Seeded = false;
        state.State.SeedAttemptAt = 0;
        await state.WriteStateAsync();
    }

    public async Task<ShiftChange> StepAsync(DateTimeOffset local, bool asleep)
    {
        KickSeed();
        KickClothes();
        await StaffAsync();
        if (state.State.Job is not { } job) return ShiftChange.Nothing;
        var today = state.State.History.LastOrDefault();
        var step = Shifts.Step(job, today, local, asleep);
        var day = Shifts.DayKey(local);
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

    public Task<Guid?> CompanyAsync() => Task.FromResult(state.State.Job?.CompanyId);

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

    private async Task StaffAsync()
    {
        var where = state.State.Job is { } job ? job.PlaceId ?? job.LocaleId : null;
        if (state.State.StaffAt == where) return;
        try
        {
            var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
            await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).WorkAtAsync(participantId, where);
            state.State.StaffAt = where;
            await state.WriteStateAsync();
        }
        catch (Exception ex) { logger.LogDebug(ex, "Staffing the workplace failed"); }
    }

    private Task EmployAsync()
        => GrainFactory.GetGrain<Economy.IWalletGrain>(this.GetPrimaryKeyString()).SetEmployedAsync(state.State.Job is not null);

    private void Append(ShiftRecord record)
    {
        state.State.History.Add(record);
        if (state.State.History.Count > HistorySize) state.State.History.RemoveRange(0, state.State.History.Count - HistorySize);
    }

    private async Task DressForShiftAsync()
    {
        try
        {
            var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
            var places = GrainFactory.GetGrain<Places.IPlacesGrain>(partyId);
            if (View().OnShift && state.State.Job?.Clothes is { OwnClothes: false } clothes) await places.ChangeForWorkAsync(participantId, clothes);
            else await places.ChangeBackAsync(participantId);
        }
        catch (Exception ex) { logger.LogDebug(ex, "Changing for work failed"); }
    }

    private void KickClothes()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (state.State.Job is not { } job || Places.WorkClothesRules.Fits(job.Clothes, job.Title) || _tailoring || now - state.State.ClothesAttemptAt < SeedRetry.TotalMilliseconds) return;
        _tailoring = true;
        state.State.ClothesAttemptAt = now;
        _ = TailorInBackgroundAsync(job.Title);
    }

    public async Task OfferClothesAsync(Places.WorkClothes clothes)
    {
        if (state.State.Job is not { } job || Places.WorkClothesRules.Fits(job.Clothes, job.Title) || !Places.WorkClothesRules.Fits(clothes, job.Title)) return;
        job.Clothes = clothes;
        await state.WriteStateAsync();
        await DressForShiftAsync();
    }

    public async Task<OccupationView> TailorAsync(string wish)
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is null || state.State.Job is not { } job) return View();
        if (await MakeClothesAsync(who, job, wish) is { } clothes && Places.WorkClothesRules.Fits(clothes, state.State.Job?.Title))
        {
            state.State.Job!.Clothes = clothes;
            await state.WriteStateAsync();
            await DressForShiftAsync();
        }
        return View();
    }

    private async Task TailorInBackgroundAsync(string title)
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        try
        {
            var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
            if (who is null || who.DefaultDriver != DriverKind.LLM || state.State.Job is not { } job) return;
            if ((await GrainFactory.HumansAsync(partyId)).Contains(participantId)) return;
            if (await MakeClothesAsync(who, job, null) is not { } clothes) return;
            await this.AsReference<IOccupationGrain>().OfferClothesAsync(clothes);
            logger.LogInformation("Work clothes for {Name} ({Job}): {Clothes}", who.Name, title, clothes.OwnClothes ? "their own" : clothes.Summary);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Designing work clothes failed for {ParticipantId}", participantId);
        }
        finally
        {
            _tailoring = false;
        }
    }

    private async Task<Places.WorkClothes?> MakeClothesAsync(CastMember who, Occupation job, string? wish)
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var snapshot = await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync();
        var body = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == participantId);
        var own = body?.OwnLook ?? body?.Appearance ?? Places.AppearanceRules.Default(participantId);
        var place = snapshot.Places.FirstOrDefault(p => p.Id == (job.PlaceId ?? job.LocaleId));
        var company = job.CompanyId is { } companyId ? await GrainFactory.GetGrain<Companies.ICompaniesGrain>(partyId).GetAsync(companyId) : null;
        var user = $"Name: {who.Name}\nWho they are: {WorldStream.Clip(who.Bio, 800)}\nJob: {job.Title} at {job.Workplace}"
            + (company is { Mandate.Length: > 0 } ? $"\nThe company: {company.Name}, which {WorldStream.Clip(company.Mandate, 300)}" : string.Empty)
            + (place is { Description.Length: > 0 } ? $"\nThe place: {WorldStream.Clip(place.Description, 300)}" : string.Empty)
            + $"\nTheir own look: {(own.Summary is { Length: > 0 } s ? s + "; " : string.Empty)}{Places.AppearanceRules.Describe(own)}"
            + $"\nDrawn as: {WorldStream.Clip(Places.WorkClothesRules.Garments(own.Design) ?? "(palette only)", 3000)}"
            + (string.IsNullOrWhiteSpace(wish) ? string.Empty : $"\nWhat they want to wear for work: {WorldStream.Clip(wish, 300)}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var json = await llm.For(participantId).CompleteJsonAsync(JobComplexity.General, Places.FigureDesign.WorkPrompt, user, cts.Token, 0.7);
        if (json is null) return null;
        var clothes = Places.WorkClothesRules.Clean(json, job.Title);
        clothes.Design = await Places.FigureDesign.DrawPrintsAsync(llm.For(participantId), clothes.Design);
        return clothes;
    }

    private OccupationView View() => new(state.State.Job, state.State.History.LastOrDefault(), state.State.Seeded);

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
            if (who is null || who.DefaultDriver != DriverKind.LLM)
            {
                state.State.Seeded = true;
                await state.WriteStateAsync();
                return;
            }
            var places = (await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places
                .Where(p => p.Kind == Places.PlaceKinds.Public && !p.Private).ToList();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var json = await llm.For(participantId).CompleteJsonAsync(JobComplexity.General,
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
            await StaffAsync();
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
    [Alias("GetAsync")] Task<OccupationView> GetAsync();
    [Alias("SetAsync")] Task<OccupationView> SetAsync(Occupation? job);
    [Alias("ReseedAsync")] Task ReseedAsync();
    [Alias("StepAsync")] Task<ShiftChange> StepAsync(DateTimeOffset local, bool asleep);
    [Alias("LeaveEarlyAsync")] Task<ShiftRecord?> LeaveEarlyAsync(DateTimeOffset local);
    [Alias("SetClothesAsync")] Task<OccupationView> SetClothesAsync(Places.WorkClothes? clothes);
    [Alias("TailorAsync")] Task<OccupationView> TailorAsync(string wish);
    [Alias("OfferClothesAsync")] Task OfferClothesAsync(Places.WorkClothes clothes);
    [Alias("NotePaidAsync")] Task NotePaidAsync(string day, int amount);
    [Alias("FixedTodayAsync"), Orleans.Concurrency.AlwaysInterleave] Task<string?> FixedTodayAsync(DateTimeOffset local);
    [Alias("CompanyAsync"), Orleans.Concurrency.AlwaysInterleave] Task<Guid?> CompanyAsync();
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
    [Id(4)] public string? SawShiftDay { get; set; }
    [Id(5)] public Guid? StaffAt { get; set; }
    [Id(6)] public long ClothesAttemptAt { get; set; }
}
