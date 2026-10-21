using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.World.Self;

/// <summary>
/// A Persona's ties in one Party (key = <c>party:persona</c>): how much they've talked with each person and when last.
/// Calibrates intimacy: confessions belong with people you know, not with everyone at once (ADR 0021).
/// </summary>
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
        tie.Warmth = Ties.ShiftWarmth(Ties.Warmth(tie, now), closer);
        tie.WarmthAt = now.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
    }

    public async Task<TieRead> GetTieAsync(Guid otherId)
    {
        await EnsureSeededAsync();
        var tie = state.State.Ties.GetValueOrDefault(otherId);
        return tie is null ? TieRead.None : new TieRead(tie.Exchanges, Ties.Warmth(tie, DateTimeOffset.UtcNow));
    }

    public async Task<string> RenderAsync(List<Guid> presentIds)
    {
        await EnsureSeededAsync();
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var cast = await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var people = cast
            .Where(c => presentIds.Contains(c.Id) && c.Id != personaId && c.Id != Narrator.PersonaId)
            .Select(c => (c.Name, Tie: state.State.Ties.GetValueOrDefault(c.Id)))
            .ToList();
        return Ties.Render(people, DateTimeOffset.UtcNow);
    }

    /// <summary>First use: reconstruct ties from who spoke in the same Rooms and DMs.</summary>
    private async Task EnsureSeededAsync()
    {
        if (state.State.Seeded) return;
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var party = GrainFactory.GetGrain<IPartyGrain>(partyId);
        foreach (var room in await party.GetChatGroups())
        {
            var messages = await GrainFactory.GetGrain<IChatGroupGrain>(room.Id).GetMessagesAsync();
            Ties.Seed(state.State.Ties, personaId, room.Kind == "dm", messages);
        }
        state.State.Seeded = true;
        await state.WriteStateAsync();
    }
}

/// <summary>Pure tie math and rendering.</summary>
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

    /// <summary>How warm things feel lately, fading back to neutral over days.</summary>
    public static double Warmth(Tie tie, DateTimeOffset now)
        => tie.WarmthAt == 0 ? 0 : tie.Warmth * Math.Pow(0.5, Math.Max(0, (now.ToUnixTimeMilliseconds() - tie.WarmthAt) / 86_400_000.0) / WarmthHalfLifeDays);

    /// <summary>One appraisal (p closer) moves warmth by up to ±0.3.</summary>
    public static double ShiftWarmth(double warmth, double closer) => Math.Clamp(warmth + (closer - 0.5) * 0.6, -1, 1);

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
    {
        if (people.Count == 0) return string.Empty;
        var lines = people.Select(p => p.Tie is null || p.Tie.Exchanges <= 0
            ? $"- {p.Name}: you've never really talked"
            : $"- {p.Name}: {Closeness(p.Tie.Exchanges)}; last talked {WorldStream.Ago(p.Tie.LastAt, now)}{(Lately(Warmth(p.Tie, now)) is { } lately ? $"; {lately}" : "")}");
        return $"# Who these people are to you\n{string.Join("\n", lines)}\nHow close you are shapes what you share. With people you barely know you keep it light; personal things are for people you know well, and even then not all the time.\n";
    }
}

[Alias("PartyTown.World.Self.IPersonaTiesGrain")]
public interface IPersonaTiesGrain : IGrainWithStringKey
{
    [Alias("NoteExchangeAsync")] Task NoteExchangeAsync(List<Guid> withIds, bool direct);
    [Alias("RenderAsync")] Task<string> RenderAsync(List<Guid> presentIds);
    /// <summary>How well this persona knows one other, and how warm it feels now.</summary>
    [Alias("GetTieAsync")] Task<TieRead> GetTieAsync(Guid otherId);
    /// <summary>A closed Moment's appraisal toward one person: <paramref name="closer"/> = p(it brought them closer).</summary>
    [Alias("ShiftWarmthAsync")] Task ShiftWarmthAsync(Guid otherId, double closer);
}

[GenerateSerializer, Alias("PartyTown.World.Self.TieRead")]
public sealed record TieRead([property: Id(0)] double Exchanges, [property: Id(1)] double Warmth)
{
    public static readonly TieRead None = new(0, 0);
}

[GenerateSerializer, Alias("PartyTown.World.Self.TiesState")]
public sealed class TiesState
{
    [Id(0)] public Dictionary<Guid, Tie> Ties { get; set; } = new();
    [Id(1)] public bool Seeded { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Self.Tie")]
public sealed class Tie
{
    [Id(0)] public double Exchanges { get; set; }
    [Id(1)] public long LastAt { get; set; }
    [Id(2)] public double Warmth { get; set; }
    [Id(3)] public long WarmthAt { get; set; }
}
