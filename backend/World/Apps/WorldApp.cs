using PartyTown.Model;

namespace PartyTown.World.Apps;

public sealed record LifeContext(
    Guid PartyId,
    CastMember Self,
    IReadOnlyList<CastMember> Cast,
    IReadOnlySet<Guid> PlayerIds,
    IReadOnlyList<ChatGroupInfo> Threads,
    IReadOnlyList<WorldActivity> RecentOwn,
    DateTimeOffset Now,
    IReadOnlyDictionary<Guid, PartyTown.World.Self.AttentionLook> Looks,
    IReadOnlyDictionary<Guid, PartyTown.World.Desires.Desire>? Wanted = null,
    IReadOnlyList<PartyTown.World.Happening>? Happenings = null,
    PartyTown.World.Places.PlaceFit? Fit = null)
{
    public PartyTown.World.Desires.Desire? WantAbout(Guid? id) => id is { } key ? Wanted?.GetValueOrDefault(key) : null;

    public bool Owes(Guid? id) => WantAbout(id) is { Source: PartyTown.World.Desires.DesireSource.Promise };

    public bool InMind(Guid target)
        => Looks.TryGetValue(target, out var look) && Now.ToUnixTimeMilliseconds() - look.At < (long)PartyTown.World.Self.ChatAttention.InMind.TotalMilliseconds;

    public int? ReadFrom(Guid thread) => PartyTown.World.Self.ChatAttention.ReadFrom(Looks.GetValueOrDefault(thread), Now);

    public Task LookAsync(IGrainFactory grains, Guid target, int? fromMessageId = null)
        => grains.GetGrain<PartyTown.World.Self.IPersonaAttentionGrain>(WorldKeys.Life(PartyId, Self.Id)).LookAsync(target, fromMessageId);

    public bool IsHuman(Guid id) => PlayerIds.Contains(id);

    public string NameOf(Guid id) => Cast.FirstOrDefault(c => c.Id == id)?.Name ?? "someone";

    public IEnumerable<CastMember> Others => Cast.Where(c => c.Id != Self.Id && c.Id != Narrator.PersonaId);

    public ParticipantView ViewOf(Guid id)
    {
        var c = Cast.FirstOrDefault(m => m.Id == id);
        return new ParticipantView(id, c?.Name ?? "someone", c?.DefaultDriver ?? DriverKind.LLM);
    }

    public bool Mentions(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(Self.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase);
}

public sealed record Affordance(string AppId, string Label, int Priority, Guid? TargetId = null, string? Verb = null, bool Look = false)
{
    public string Key => $"{AppId}|{Verb}|{TargetId}";
}

public sealed record AppPerception(string AppId, string Title, string Block, IReadOnlyList<Affordance> Affordances);

public sealed record ActOutcome(string Action, string Summary, Guid? TargetId, Guid? WithPersonaId = null)
{
    public bool Looked => Action == PersonaLifeGrain.LookAction;

    public static ActOutcome Seen(string summary, Guid? target, Guid? with = null) => new(PersonaLifeGrain.LookAction, summary, target, with);
}

public interface IWorldApp
{
    string Id { get; }

    Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct);

    Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct);
}
