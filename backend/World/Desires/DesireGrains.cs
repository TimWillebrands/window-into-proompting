using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Desires;

public sealed class IntrinsicDesiresGrain(
    [PersistentState(stateName: "intrinsicDesires", storageName: "parties")]
    IPersistentState<IntrinsicDesiresState> state,
    IWorldLlm llm,
    ILogger<IntrinsicDesiresGrain> logger)
    : Grain, IIntrinsicDesiresGrain
{
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);
    private const int DistillVersion = 2;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public Task<List<Desire>> GetAsync() => Task.FromResult(state.State.Desires.ToList());

    public Task EnsureAsync(string name, string sheet)
    {
        var hash = Self.LivedSheets.Hash($"desires v{DistillVersion}\n{sheet}");
        if (state.State.Authored || state.State.SourceHash == hash || DateTimeOffset.UtcNow - _lastAttempt < Retry) return Task.CompletedTask;
        _lastAttempt = DateTimeOffset.UtcNow;
        _ = DistillAsync(name, sheet, hash);
        return Task.CompletedTask;
    }

    public async Task<List<Desire>> SetAsync(List<Desire> desires)
    {
        state.State.Desires = desires.Select(d => { d.Source = DesireSource.Authored; return d; }).ToList();
        state.State.Authored = true;
        await state.WriteStateAsync();
        return state.State.Desires.ToList();
    }

    public async Task<List<Desire>> AddAsync(Desire desire)
    {
        state.State.Desires.Add(desire);
        state.State.Authored = true;
        await state.WriteStateAsync();
        return state.State.Desires.ToList();
    }

    private async Task DistillAsync(string name, string sheet, string hash)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var json = await llm.For(this.GetPrimaryKey()).CompleteJsonAsync(JobComplexity.General,
                $"""
                You read who someone is and say what they want in life right now. Life-sized and concrete, no grand themes,
                nothing poetic. Read it off who they are, in the terms of their own life: their trade, home, people and money.
                Don't invent a new life for them, and skip anything anybody at all might want.
                Output ONLY JSON with string fields:
                - having: one thing they want to get or have: a specific object, money for something, someone's attention. Or "none".
                - being: one way they want to change or grow: get better at something, patch things up with someone, finish something. Or "none".
                Each one a short plain phrase in second person that names the thing itself.
                """,
                $"Name: {name}\n\nWho they are:\n{sheet}", cts.Token, 0.6, ["having", "being"]);
            if (json is null) return;
            var now = DateTimeOffset.UtcNow;
            state.State.Desires = new[]
                {
                    DesireRules.Parse(json["having"]?.ToString(), DesireKind.Having, DesireSource.Sheet, now),
                    DesireRules.Parse(json["being"]?.ToString(), DesireKind.Being, DesireSource.Sheet, now),
                }
                .OfType<Desire>().ToList();
            state.State.SourceHash = hash;
            await state.WriteStateAsync();
            logger.LogInformation("Intrinsic desires for {Name}: {Desires}", name, string.Join(" | ", state.State.Desires.Select(d => d.Text)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Desire distillation failed for {Name}", name);
        }
    }
}

[Alias("PartyTown.World.Desires.IIntrinsicDesiresGrain")]
public interface IIntrinsicDesiresGrain : IGrainWithGuidKey
{
    [Alias("GetAsync")] Task<List<Desire>> GetAsync();
    [Alias("EnsureAsync")] Task EnsureAsync(string name, string sheet);
    [Alias("SetAsync")] Task<List<Desire>> SetAsync(List<Desire> desires);
    [Alias("AddAsync")] Task<List<Desire>> AddAsync(Desire desire);
}

public sealed class DesiresGrain(
    [PersistentState(stateName: "desires", storageName: "parties")]
    IPersistentState<DesiresState> state)
    : Grain, IDesiresGrain
{
    private const int MaxAcquired = 3;
    private const int MaxSpentPromises = 10;

    public async Task<DesiresView> GetAsync()
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var intrinsic = GrainFactory.GetGrain<IIntrinsicDesiresGrain>(participantId);
        var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is { DefaultDriver: DriverKind.LLM } && await Self.LivedSheets.SourceAsync(GrainFactory, who) is { Length: > 0 } sheet)
            await intrinsic.EnsureAsync(who.Name, sheet);

        var now = DateTimeOffset.UtcNow;
        var faded = state.State.Acquired.Where(d => DesireRules.Fades(d, intrinsic: false, now)).ToList();
        foreach (var d in faded) d.Status = DesireStatus.Faded;
        if (faded.Count > 0) await state.WriteStateAsync();
        foreach (var d in faded.Where(d => Self.PromiseRules.LetDown(d, now.ToUnixTimeMilliseconds())))
            _ = CoolAsync(partyId, d.TargetId!.Value, participantId);

        var mine = (await intrinsic.GetAsync()).Select(d =>
        {
            if (state.State.Marks.TryGetValue(d.Id, out var mark))
            {
                d.Status = mark.Status;
                d.TouchedAt = mark.TouchedAt;
            }
            return d;
        }).ToList();
        return new DesiresView(mine, state.State.Acquired.ToList(), state.State.ReviewedAt);
    }

    private async Task CoolAsync(Guid partyId, Guid holder, Guid promiser)
    {
        try { await GrainFactory.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(partyId, holder)).ShiftWarmthAsync(promiser, Work.ResponsibilityRules.NoShow); }
        catch (Exception) { }
    }

    public async Task<Desire> AddAsync(Desire desire)
    {
        if (DesireRules.IsPromise(desire) && Self.PromiseRules.Restate(state.State.Acquired, desire, DateTimeOffset.UtcNow))
        {
            await state.WriteStateAsync();
            return Self.PromiseRules.Open(state.State.Acquired, desire.TargetId!.Value, desire.Coming, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())!;
        }
        state.State.Acquired.Add(desire);
        var active = state.State.Acquired.Where(d => d.Status == DesireStatus.Active && !DesireRules.IsPromise(d)).OrderBy(d => Math.Max(d.TouchedAt, d.Since)).ToList();
        foreach (var old in active.Take(Math.Max(0, active.Count - MaxAcquired))) old.Status = DesireStatus.Faded;
        var spent = state.State.Acquired.Where(d => DesireRules.IsPromise(d) && d.Status != DesireStatus.Active).OrderByDescending(d => d.TouchedAt).Skip(MaxSpentPromises).ToHashSet();
        state.State.Acquired.RemoveAll(spent.Contains);
        await state.WriteStateAsync();
        return desire;
    }

    public async Task<bool> MarkAsync(Guid desireId, DesireStatus status)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        Desire? desire;
        if (state.State.Acquired.FirstOrDefault(d => d.Id == desireId) is { } acquired)
        {
            desire = acquired;
            acquired.Status = status;
            acquired.TouchedAt = now;
        }
        else
        {
            desire = (await GrainFactory.GetGrain<IIntrinsicDesiresGrain>(participantId).GetAsync()).FirstOrDefault(d => d.Id == desireId);
            if (desire is null) return false;
            state.State.Marks[desireId] = new DesireMark { Status = status, TouchedAt = now };
        }
        await state.WriteStateAsync();
        if (status == DesireStatus.Fulfilled && desire is { Kind: DesireKind.Being } && !DesireRules.IsPromise(desire))
            await Skills.Practising.NoteAsync(GrainFactory, partyId, participantId, Skills.Practising.Desire(desire.Id, desire.Text));
        return true;
    }

    public async Task TouchAsync(Guid desireId)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (state.State.Acquired.FirstOrDefault(d => d.Id == desireId) is { } acquired) acquired.TouchedAt = now;
        else state.State.Marks[desireId] = new DesireMark { Status = state.State.Marks.GetValueOrDefault(desireId)?.Status ?? DesireStatus.Active, TouchedAt = now };
        await state.WriteStateAsync();
    }

    public async Task<bool> TryReviewAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - state.State.ReviewedAt < DesireRules.ReviewEvery.TotalMilliseconds) return false;
        state.State.ReviewedAt = now;
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> RemoveAsync(Guid desireId)
    {
        var removed = state.State.Acquired.RemoveAll(d => d.Id == desireId) > 0;
        if (removed) await state.WriteStateAsync();
        return removed;
    }
}

[Alias("PartyTown.World.Desires.IDesiresGrain")]
public interface IDesiresGrain : IGrainWithStringKey
{
    [Alias("GetAsync")] Task<DesiresView> GetAsync();
    [Alias("AddAsync")] Task<Desire> AddAsync(Desire desire);
    [Alias("MarkAsync")] Task<bool> MarkAsync(Guid desireId, DesireStatus status);
    [Alias("TouchAsync")] Task TouchAsync(Guid desireId);
    [Alias("TryReviewAsync")] Task<bool> TryReviewAsync();
    [Alias("RemoveAsync")] Task<bool> RemoveAsync(Guid desireId);
}

[GenerateSerializer, Alias("PartyTown.World.Desires.DesiresView")]
public sealed record DesiresView(
    [property: Id(0)] List<Desire> Intrinsic,
    [property: Id(1)] List<Desire> Acquired,
    [property: Id(2)] long ReviewedAt)
{
    public IEnumerable<Desire> All => Intrinsic.Concat(Acquired);
}

[GenerateSerializer, Alias("PartyTown.World.Desires.IntrinsicDesiresState")]
public sealed class IntrinsicDesiresState
{
    [Id(0)] public List<Desire> Desires { get; set; } = new();
    [Id(1)] public string? SourceHash { get; set; }
    [Id(2)] public bool Authored { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Desires.DesiresState")]
public sealed class DesiresState
{
    [Id(0)] public List<Desire> Acquired { get; set; } = new();
    [Id(1)] public Dictionary<Guid, DesireMark> Marks { get; set; } = new();
    [Id(2)] public long ReviewedAt { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Desires.DesireMark")]
public sealed class DesireMark
{
    [Id(0)] public DesireStatus Status { get; set; }
    [Id(1)] public long TouchedAt { get; set; }
}
