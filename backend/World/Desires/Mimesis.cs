using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Desires;

/// <summary>
/// Mimetic desire (ADR 0026, after Girard): people come to want what people they know have or want. Asked inside
/// the Moment appraisal's own System One call, voiced only on a hit, and capped hard; a little goes a long way.
/// </summary>
public sealed class Mimesis(IGrainFactory grains, IWorldLlm llm, ILogger<Mimesis> logger)
{
    private const string Shape = """{"kind": "having or being", "want": "<one short plain line, second person>", "about": "<name of the person it's about, or nobody>"}""";

    /// <summary>Pairs (who might catch, from whom) that clear the caps; the appraisal asks one noul each.</summary>
    public async Task<List<(ParticipantView Who, ParticipantView From)>> EligibleAsync(Guid partyId, IEnumerable<(ParticipantView Who, ParticipantView From)> pairs, DateTimeOffset now)
    {
        var eligible = new List<(ParticipantView, ParticipantView)>();
        var caught = await grains.GetGrain<IMimesisGrain>(partyId).GetCaughtAtAsync();
        foreach (var (who, from) in pairs)
        {
            if (!MimesisRules.MayCatch(caught.GetValueOrDefault(who.Id), now)) continue;
            var key = WorldKeys.Life(partyId, who.Id);
            var tie = await grains.GetGrain<Self.IPersonaTiesGrain>(key).GetTieAsync(from.Id);
            var desires = await grains.GetGrain<IDesiresGrain>(key).GetAsync();
            if (MimesisRules.Knows(tie.Exchanges) && !desires.Acquired.Any(MimesisRules.IsMimetic)) eligible.Add((who, from));
        }
        return eligible;
    }

    public static string Key(ParticipantView who, ParticipantView from) => $"mim-{who.Id:N}-{from.Id:N}";

    public static SystemOneQuestion Question(ParticipantView who, ParticipantView from)
        => new NoulQuestion($"{who.Name} came away from this wanting something {from.Name} has or wants.");

    /// <summary>The strongest hit (if any) becomes an Acquired Desire; one per appraisal at most.</summary>
    public async Task CatchAsync(Guid partyId, string transcript, IReadOnlyList<(ParticipantView Who, ParticipantView From)> eligible, SystemOneAnswers answers, IReadOnlyList<ParticipantView> present)
    {
        var hits = eligible.Select(e => (e.Who, e.From, P: answers.Noul(Key(e.Who, e.From), 0)))
            .Where(e => e.P >= MimesisRules.Threshold).OrderByDescending(e => e.P).ToList();
        if (hits.Count == 0) return;
        var hit = hits[0];
        var mimesis = grains.GetGrain<IMimesisGrain>(partyId);
        if (!await mimesis.TryCatchAsync(hit.Who.Id)) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var json = await llm.CompleteJsonAsync(JobComplexity.General,
                $"You read a conversation and say plainly what {hit.Who.Name} came away wanting, caught from {hit.From.Name}: something {hit.From.Name} has, or something {hit.From.Name} wants. Life-sized and concrete, nothing poetic.\nOutput ONLY JSON: {Shape}",
                transcript, cts.Token, 0.6, ["kind", "want", "about"]);
            var kind = json?["kind"]?.ToString()?.Contains("being", StringComparison.OrdinalIgnoreCase) == true ? DesireKind.Being : DesireKind.Having;
            if (DesireRules.Parse(json?["want"]?.ToString(), kind, DesireSource.Mimesis, DateTimeOffset.UtcNow) is not { } desire) return;
            desire.FromId = hit.From.Id;
            desire.TargetId = present.Where(p => p.Id != hit.Who.Id && MimesisRules.Names(json?["about"]?.ToString(), p.Name)).Select(p => (Guid?)p.Id).FirstOrDefault();
            await grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, hit.Who.Id)).AddAsync(desire);
            logger.LogInformation("Mimesis: {Who} caught \"{Want}\" from {From} (p={P:F2})", hit.Who.Name, desire.Text, hit.From.Name, hit.P);
            await RivalryAsync(partyId, hit.Who, hit.From, desire);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Mimesis failed for {Who}", hit.Who.Name);
        }
    }

    /// <summary>Wanting the same person's attention as the one you caught it from: friction both ways, one rivalry per World.</summary>
    private async Task RivalryAsync(Guid partyId, ParticipantView who, ParticipantView from, Desire desire)
    {
        if (desire.TargetId is not { } target) return;
        var theirs = await grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, from.Id)).GetAsync();
        if (!theirs.All.Any(d => d.Status == DesireStatus.Active && d.TargetId == target)) return;
        if (!await grains.GetGrain<IMimesisGrain>(partyId).TryRivalryAsync(who.Id, from.Id)) return;
        await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(partyId, who.Id)).ShiftWarmthAsync(from.Id, MimesisRules.Friction);
        await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(partyId, from.Id)).ShiftWarmthAsync(who.Id, MimesisRules.Friction);
        logger.LogInformation("Rivalry: {Who} and {From} want the same person's attention", who.Name, from.Name);
    }
}

/// <summary>Pure caps for mimesis: whom you imitate, how often, and how it sours.</summary>
public static class MimesisRules
{
    public const double Threshold = 0.7;
    /// <summary>Exchanges before someone is close enough to imitate ("talked a fair bit").</summary>
    public const double KnowsAt = 10;
    public static readonly TimeSpan CatchEvery = TimeSpan.FromDays(3);
    public static readonly TimeSpan RivalryLasts = TimeSpan.FromDays(3);
    public const double Friction = 0.35;

    public static bool Knows(double exchanges) => exchanges >= KnowsAt;

    public static bool MayCatch(long? lastCaughtAt, DateTimeOffset now)
        => lastCaughtAt is not { } at || now.ToUnixTimeMilliseconds() - at >= CatchEvery.TotalMilliseconds;

    public static bool IsMimetic(Desire d) => d.Source == DesireSource.Mimesis && d.Status == DesireStatus.Active;

    public static bool Names(string? about, string name)
        => !string.IsNullOrWhiteSpace(about) && about.Contains(name.Split(' ')[0], StringComparison.OrdinalIgnoreCase);
}

/// <summary>Per-Party mimesis caps (key = Party id): when each persona last caught a desire, and the one live rivalry.</summary>
public sealed class MimesisGrain(
    [PersistentState(stateName: "mimesis", storageName: "parties")]
    IPersistentState<MimesisState> state)
    : Grain, IMimesisGrain
{
    public Task<Dictionary<Guid, long>> GetCaughtAtAsync() => Task.FromResult(new Dictionary<Guid, long>(state.State.CaughtAt));

    public async Task<bool> TryCatchAsync(Guid who)
    {
        var now = DateTimeOffset.UtcNow;
        if (!MimesisRules.MayCatch(state.State.CaughtAt.TryGetValue(who, out var at) ? at : null, now)) return false;
        state.State.CaughtAt[who] = now.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> TryRivalryAsync(Guid a, Guid b)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - state.State.RivalryAt < MimesisRules.RivalryLasts.TotalMilliseconds) return false;
        state.State.RivalryAt = now;
        state.State.Rivals = new List<Guid> { a, b };
        await state.WriteStateAsync();
        return true;
    }

    public Task<List<Guid>> GetRivalsAsync()
        => Task.FromResult(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - state.State.RivalryAt < MimesisRules.RivalryLasts.TotalMilliseconds
            ? state.State.Rivals.ToList()
            : new List<Guid>());
}

[Alias("PartyTown.World.Desires.IMimesisGrain")]
public interface IMimesisGrain : IGrainWithGuidKey
{
    [Alias("GetCaughtAtAsync")] Task<Dictionary<Guid, long>> GetCaughtAtAsync();
    /// <summary>Reserve a catch for this persona; false within the cooldown.</summary>
    [Alias("TryCatchAsync")] Task<bool> TryCatchAsync(Guid who);
    /// <summary>Start a rivalry; false while another one is live.</summary>
    [Alias("TryRivalryAsync")] Task<bool> TryRivalryAsync(Guid a, Guid b);
    /// <summary>The live rivalry's pair, or empty.</summary>
    [Alias("GetRivalsAsync")] Task<List<Guid>> GetRivalsAsync();
}

[GenerateSerializer, Alias("PartyTown.World.Desires.MimesisState")]
public sealed class MimesisState
{
    [Id(0)] public Dictionary<Guid, long> CaughtAt { get; set; } = new();
    [Id(1)] public long RivalryAt { get; set; }
    [Id(2)] public List<Guid> Rivals { get; set; } = new();
}
