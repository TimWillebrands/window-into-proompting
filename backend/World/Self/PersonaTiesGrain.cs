using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Relations;

namespace PartyTown.World.Self;

public sealed class PersonaTiesGrain(
    [PersistentState(stateName: "ties", storageName: "parties")]
    IPersistentState<TiesState> state)
    : Grain, IPersonaTiesGrain
{
    public async Task NoteExchangeAsync(List<Guid> withIds, bool direct)
    {
        await EnsureSeededAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var id in withIds)
        {
            var tie = state.State.Ties.TryGetValue(id, out var t) ? t : state.State.Ties[id] = new Tie();
            tie.Exchanges += direct ? 1 : Ties.RoomWeight;
            tie.LastAt = now;
        }
        await state.WriteStateAsync();
    }

    public async Task ShiftWarmthAsync(Guid otherId, double closer)
    {
        await EnsureSeededAsync();
        var tie = state.State.Ties.TryGetValue(otherId, out var t) ? t : state.State.Ties[otherId] = new Tie();
        var now = DateTimeOffset.UtcNow;
        if (!Ties.MayWarm(tie, closer, now)) return;
        tie.Warmth = Ties.ShiftWarmth(Ties.Warmth(tie, now), closer);
        tie.WarmthAt = now.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
    }

    public async Task<TieRead> GetTieAsync(Guid otherId)
    {
        await EnsureSeededAsync();
        var tie = state.State.Ties.GetValueOrDefault(otherId);
        return tie is null ? TieRead.None : new TieRead(tie.Exchanges, Ties.Warmth(tie, DateTimeOffset.UtcNow), tie.LastAt);
    }

    public async Task<List<Guid>> GetTalkedWithAsync()
    {
        await EnsureSeededAsync();
        return state.State.Ties.Where(t => t.Value.Exchanges > 0).Select(t => t.Key).ToList();
    }

    public async Task<Guid?> GetClosestAsync()
    {
        await EnsureSeededAsync();
        var (_, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        return Ties.Closest(state.State.Ties.Where(t => t.Key != personaId && t.Key != Narrator.PersonaId), DateTimeOffset.UtcNow);
    }

    public async Task<string> RenderAsync(List<Guid> presentIds, bool here = true)
    {
        await EnsureSeededAsync();
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var cast = await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var book = await GrainFactory.GetGrain<IRelationsGrain>(partyId).GetAsync();
        var firstNames = cast.ToDictionary(c => c.Id, c => c.Name.Split(' ')[0]);
        var people = new List<Acquaintance>();
        foreach (var c in cast.Where(c => presentIds.Contains(c.Id) && c.Id != personaId && c.Id != Narrator.PersonaId))
            people.Add(RelationRules.Seen(book, personaId, c.Id, c.Name, state.State.Ties.GetValueOrDefault(c.Id), id => firstNames.GetValueOrDefault(id), here)
                with { Promised = await HeardAsync(partyId, c.Id, personaId, firstNames[c.Id]) });
        return Ties.Render(people, DateTimeOffset.UtcNow);
    }

    private async Task<List<string>> HeardAsync(Guid partyId, Guid promiser, Guid holder, string first)
    {
        try
        {
            var theirs = await GrainFactory.GetGrain<Desires.IDesiresGrain>(WorldKeys.Life(partyId, promiser)).GetAsync();
            return PromiseRules.Heard(theirs.Acquired, holder, first, DateTimeOffset.UtcNow).ToList();
        }
        catch (Exception) { return new List<string>(); }
    }

    private async Task EnsureSeededAsync()
    {
        if (state.State.Seeded && state.State.WarmthVersion < Ties.WarmthVersion)
        {
            foreach (var tie in state.State.Ties.Values) tie.Warmth /= 2;
            state.State.WarmthVersion = Ties.WarmthVersion;
            await state.WriteStateAsync();
        }
        if (state.State.Seeded) return;
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var party = GrainFactory.GetGrain<IPartyGrain>(partyId);
        foreach (var room in await party.GetChatGroups())
        {
            var messages = await GrainFactory.GetGrain<IChatGroupGrain>(room.Id).GetMessagesAsync();
            Ties.Seed(state.State.Ties, personaId, room.Kind == "dm", messages);
        }
        state.State.Seeded = true;
        state.State.WarmthVersion = Ties.WarmthVersion;
        await state.WriteStateAsync();
    }
}

public static class Ties
{
    public const double RoomWeight = 0.3;

    public static void Seed(Dictionary<Guid, Tie> ties, Guid self, bool direct, IReadOnlyList<ChatMessage> messages)
    {
        var spoken = messages.Where(m => !string.IsNullOrWhiteSpace(m.Content)).ToList();
        var mine = spoken.Count(m => m.SenderId == self);
        if (mine == 0) return;
        foreach (var other in spoken.Where(m => m.SenderId != self).GroupBy(m => m.SenderId))
        {
            var tie = ties.TryGetValue(other.Key, out var t) ? t : ties[other.Key] = new Tie();
            tie.Exchanges += Math.Min(mine, other.Count()) * (direct ? 1 : RoomWeight);
            tie.LastAt = Math.Max(tie.LastAt, spoken.Where(m => m.SenderId == self || m.SenderId == other.Key).Max(m => m.SendAt ?? 0));
        }
    }

    private const double WarmthHalfLifeDays = 2;

    public static double Warmth(Tie tie, DateTimeOffset now)
        => tie.WarmthAt == 0 ? 0 : tie.Warmth * Math.Pow(0.5, Math.Max(0, (now.ToUnixTimeMilliseconds() - tie.WarmthAt) / 86_400_000.0) / WarmthHalfLifeDays);

    public static double ShiftWarmth(double warmth, double closer)
    {
        var step = (closer - 0.5) * 0.6;
        return Math.Clamp(warmth + step * (step > 0 ? 1 - warmth : 1 + warmth), -1, 1);
    }

    public const int WarmthVersion = 1;

    public static readonly TimeSpan WarmCooldown = TimeSpan.FromHours(3);

    public static bool MayWarm(Tie tie, double closer, DateTimeOffset now)
        => closer <= 0.5 || tie.WarmthAt == 0 || now.ToUnixTimeMilliseconds() - tie.WarmthAt >= WarmCooldown.TotalMilliseconds;

    public static Guid? Closest(IEnumerable<KeyValuePair<Guid, Tie>> ties, DateTimeOffset now)
        => ties.Select(t => (t.Key, Score: t.Value.Exchanges * (1 + Warmth(t.Value, now))))
            .Where(t => t.Score > 0).OrderByDescending(t => t.Score).Select(t => (Guid?)t.Key).FirstOrDefault();

    public static string? Lately(double warmth) => warmth switch
    {
        >= 0.4 => "things have felt really warm between you lately",
        >= 0.2 => "you've been getting on well lately",
        <= -0.4 => "things have been strained between you lately",
        <= -0.2 => "it's been a bit tense between you lately",
        _ => null,
    };

    public static string Closeness(double exchanges) => exchanges switch
    {
        < 2 => "you barely know them",
        < 10 => "you've talked a few times",
        < 40 => "you know each other fairly well",
        _ => "you know each other well",
    };

    public static string Render(IReadOnlyList<(string Name, Tie? Tie)> people, DateTimeOffset now)
        => Render(people.Select(p => new Acquaintance(p.Name, p.Tie)).ToList(), now);

    private static IEnumerable<string> Facts(Acquaintance p, DateTimeOffset now)
    {
        if (p.Between is { } between) yield return between;
        foreach (var promised in p.Promised ?? Array.Empty<string>()) yield return promised;
        if (p.Known is { } known) yield return known;
        if (Distance(p) is { } distance) yield return distance;
        if (p.Tie is not { Exchanges: > 0 } tie) yield break;
        yield return $"last talked {WorldStream.Ago(tie.LastAt, now)}";
        if (Lately(Warmth(tie, now)) is { } lately) yield return lately;
    }

    private static string? Distance(Acquaintance p)
    {
        if (p.Current) return null;
        var exchanges = p.Tie?.Exchanges ?? 0;
        if (p.Between is not null && exchanges < 2) return "you've barely spoken since";
        return exchanges <= 0 ? "you've never really talked" : Closeness(exchanges);
    }

    public static string Render(IReadOnlyList<Acquaintance> people, DateTimeOffset now)
    {
        if (people.Count == 0) return string.Empty;
        var lines = people.Select(p => $"- {p.Name}: {string.Join("; ", Facts(p, now))}");
        return $"# Who these people are to you\n{string.Join("\n", lines)}\nHow close you are shapes what you share. With people you barely know you keep it light; personal things are for people you know well, and even then not all the time.\n";
    }
}

public static class Acquaintances
{
    public static async Task<HashSet<Guid>> KnownAsync(IGrainFactory grains, Guid partyId, Guid selfId)
    {
        var known = (await grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, selfId)).GetTalkedWithAsync()).ToHashSet();
        foreach (var relation in (await grains.GetGrain<IRelationsGrain>(partyId).GetAsync()).Relations.Where(r => r.Involves(selfId)))
            known.Add(relation.Other(selfId));
        return known;
    }
}

public sealed record Acquaintance(string Name, Tie? Tie, string? Between = null, bool Current = false, string? Known = null, IReadOnlyList<string>? Promised = null);

[Alias("PartyTown.World.Self.IPersonaTiesGrain")]
public interface IPersonaTiesGrain : IGrainWithStringKey
{
    [Alias("NoteExchangeAsync")] Task NoteExchangeAsync(List<Guid> withIds, bool direct);
    [Alias("RenderAsync")] Task<string> RenderAsync(List<Guid> presentIds, bool here = true);
    [Alias("GetTieAsync")] Task<TieRead> GetTieAsync(Guid otherId);
    [Alias("GetClosestAsync")] Task<Guid?> GetClosestAsync();
    [Alias("GetTalkedWithAsync")] Task<List<Guid>> GetTalkedWithAsync();
    [Alias("ShiftWarmthAsync")] Task ShiftWarmthAsync(Guid otherId, double closer);
}

[GenerateSerializer, Alias("PartyTown.World.Self.TieRead")]
public sealed record TieRead([property: Id(0)] double Exchanges, [property: Id(1)] double Warmth, [property: Id(2)] long LastAt = 0)
{
    public static readonly TieRead None = new(0, 0);
}

[GenerateSerializer, Alias("PartyTown.World.Self.TiesState")]
public sealed class TiesState
{
    [Id(0)] public Dictionary<Guid, Tie> Ties { get; set; } = new();
    [Id(1)] public bool Seeded { get; set; }
    [Id(2)] public int WarmthVersion { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Self.Tie")]
public sealed class Tie
{
    [Id(0)] public double Exchanges { get; set; }
    [Id(1)] public long LastAt { get; set; }
    [Id(2)] public double Warmth { get; set; }
    [Id(3)] public long WarmthAt { get; set; }
}
