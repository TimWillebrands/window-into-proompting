using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Desires;

/// <summary>
/// A Persona's Intrinsic Desires (ADR 0026), key = persona id: part of who they are, so they travel into every Party.
/// Authored, or distilled once from the lived sheet; a new sheet re-distils unless an author has set them.
/// </summary>
public sealed class IntrinsicDesiresGrain(
    [PersistentState(stateName: "intrinsicDesires", storageName: "parties")]
    IPersistentState<IntrinsicDesiresState> state,
    IWorldLlm llm,
    ILogger<IntrinsicDesiresGrain> logger)
    : Grain, IIntrinsicDesiresGrain
{
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public Task<List<Desire>> GetAsync() => Task.FromResult(state.State.Desires.ToList());

    public Task EnsureAsync(string name, string sheet)
    {
        var hash = Self.LivedSheets.Hash(sheet);
        if (state.State.Authored || state.State.SourceHash == hash || DateTimeOffset.UtcNow - _lastAttempt < Retry) return Task.CompletedTask;
        _lastAttempt = DateTimeOffset.UtcNow;
        // Detached: callers never wait on the LLM.
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
            var json = await llm.CompleteJsonAsync(JobComplexity.General,
                $"""
                You read who someone is and say what they want in life right now. Life-sized and concrete, no grand themes,
                nothing poetic. Read it off who they are; don't invent a new life for them.
                Output ONLY JSON with string fields:
                - having: one thing they want to get or have: a specific object, money for something, someone's attention. Or "none".
                - being: one way they want to change or grow: get better at something, patch things up with someone, finish something. Or "none".
                Each one short plain sentence in second person ("a proper winter coat", "get your sourdough right").
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
    /// <summary>Distil from the sheet in the background when it's new and nobody authored them.</summary>
    [Alias("EnsureAsync")] Task EnsureAsync(string name, string sheet);
    /// <summary>Author edit: replaces them, and the sheet no longer overrides.</summary>
    [Alias("SetAsync")] Task<List<Desire>> SetAsync(List<Desire> desires);
    /// <summary>Promotion: an Acquired Desire becomes part of who they are.</summary>
    [Alias("AddAsync")] Task<List<Desire>> AddAsync(Desire desire);
}

/// <summary>
/// A Participant's desires in one Party (ADR 0026), key = <c>party:participant</c>: their Acquired Desires, plus how
/// their Intrinsic ones are going here (fulfilled, last touched).
/// </summary>
public sealed class DesiresGrain(
    [PersistentState(stateName: "desires", storageName: "parties")]
    IPersistentState<DesiresState> state)
    : Grain, IDesiresGrain
{
    /// <summary>Acquired ones crowd out who you are past this; the oldest active one fades.</summary>
    private const int MaxAcquired = 3;

    public async Task<DesiresView> GetAsync()
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var intrinsic = GrainFactory.GetGrain<IIntrinsicDesiresGrain>(participantId);
        var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is { DefaultDriver: DriverKind.LLM } && (Self.LivedSheets.For(who.Id, who.SystemPrompt) ?? who.Bio) is { Length: > 0 } sheet)
            await intrinsic.EnsureAsync(who.Name, sheet);

        var now = DateTimeOffset.UtcNow;
        var faded = false;
        foreach (var d in state.State.Acquired.Where(d => DesireRules.Fades(d, intrinsic: false, now)))
        {
            d.Status = DesireStatus.Faded;
            faded = true;
        }
        if (faded) await state.WriteStateAsync();

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

    public async Task<Desire> AddAsync(Desire desire)
    {
        state.State.Acquired.Add(desire);
        var active = state.State.Acquired.Where(d => d.Status == DesireStatus.Active).OrderBy(d => Math.Max(d.TouchedAt, d.Since)).ToList();
        foreach (var old in active.Take(Math.Max(0, active.Count - MaxAcquired))) old.Status = DesireStatus.Faded;
        await state.WriteStateAsync();
        return desire;
    }

    public async Task<bool> MarkAsync(Guid desireId, DesireStatus status)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (state.State.Acquired.FirstOrDefault(d => d.Id == desireId) is { } acquired)
        {
            acquired.Status = status;
            acquired.TouchedAt = now;
        }
        else
        {
            var (_, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
            if ((await GrainFactory.GetGrain<IIntrinsicDesiresGrain>(participantId).GetAsync()).All(d => d.Id != desireId)) return false;
            state.State.Marks[desireId] = new DesireMark { Status = status, TouchedAt = now };
        }
        await state.WriteStateAsync();
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
    /// <summary>Intrinsic (as they're going here) and Acquired desires; kicks off distilling the intrinsic ones.</summary>
    [Alias("GetAsync")] Task<DesiresView> GetAsync();
    [Alias("AddAsync")] Task<Desire> AddAsync(Desire desire);
    /// <summary>Fulfilled, faded or abandoned; false when no such desire.</summary>
    [Alias("MarkAsync")] Task<bool> MarkAsync(Guid desireId, DesireStatus status);
    /// <summary>Progress: keeps an Acquired Desire from fading and puts it back on top of the mind.</summary>
    [Alias("TouchAsync")] Task TouchAsync(Guid desireId);
    /// <summary>True (and the clock reset) when a review is due.</summary>
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
