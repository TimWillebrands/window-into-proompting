using PartyTown.Model;

namespace PartyTown.World.Apps;

/// <summary>Everything an App needs to know about the Persona whose Life is ticking.</summary>
public sealed record LifeContext(
    Guid PartyId,
    CastMember Self,
    IReadOnlyList<CastMember> Cast,
    IReadOnlySet<Guid> PlayerIds,
    IReadOnlyList<ChatGroupInfo> Threads,
    IReadOnlyList<WorldActivity> RecentOwn,
    DateTimeOffset Now,
    IReadOnlyDictionary<Guid, PartyTown.World.Self.AttentionLook> Looks)
{
    /// <summary>Looked at <paramref name="target"/> recently enough that it's still in mind.</summary>
    public bool InMind(Guid target)
        => Looks.TryGetValue(target, out var look) && Now.ToUnixTimeMilliseconds() - look.At < (long)PartyTown.World.Self.ChatAttention.InMind.TotalMilliseconds;

    /// <summary>The read-back still in mind for a chat thread.</summary>
    public int? ReadFrom(Guid thread) => PartyTown.World.Self.ChatAttention.ReadFrom(Looks.GetValueOrDefault(thread), Now);

    public Task LookAsync(IGrainFactory grains, Guid target, int? fromMessageId = null)
        => grains.GetGrain<PartyTown.World.Self.IPersonaAttentionGrain>(WorldKeys.Life(PartyId, Self.Id)).LookAsync(target, fromMessageId);

    public bool IsHuman(Guid id) => PlayerIds.Contains(id);

    public string NameOf(Guid id) => Cast.FirstOrDefault(c => c.Id == id)?.Name ?? "someone";

    /// <summary>People the Persona can engage with: everyone but self and the Narrator.</summary>
    public IEnumerable<CastMember> Others => Cast.Where(c => c.Id != Self.Id && c.Id != Narrator.PersonaId);

    public ParticipantView ViewOf(Guid id)
    {
        var c = Cast.FirstOrDefault(m => m.Id == id);
        return new ParticipantView(id, c?.Name ?? "someone", c?.DefaultDriver ?? DriverKind.LLM);
    }

    public bool Mentions(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(Self.Name.Split(' ')[0], StringComparison.OrdinalIgnoreCase);
}

/// <summary>An action the Arena offers this Persona right now (ADR 0018). Priority foregrounds, never forces.</summary>
/// <param name="Look">Only looks (opens an App, scrolls back): Life follows it with a choice over what's now in view.</param>
public sealed record Affordance(string AppId, string Label, int Priority, Guid? TargetId = null, string? Verb = null, bool Look = false);

/// <summary>What the Persona sees of one App: a prose block plus the Affordances it offers.</summary>
public sealed record AppPerception(string AppId, string Title, string Block, IReadOnlyList<Affordance> Affordances);

/// <param name="WithPersonaId">The other party of a 1:1 act (a DM), so clients never parse it out of Summary.</param>
public sealed record ActOutcome(string Action, string Summary, Guid? TargetId, Guid? WithPersonaId = null)
{
    public bool Looked => Action == PersonaLifeGrain.LookAction;

    /// <summary>A look: something is now in view and the Persona decides next what to do with it.</summary>
    public static ActOutcome Seen(string summary, Guid? target, Guid? with = null) => new(PersonaLifeGrain.LookAction, summary, target, with);
}

/// <summary>A mode of engagement inside a Party. Adding a mode = adding an App; Life is untouched.</summary>
public interface IWorldApp
{
    string Id { get; }

    Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct);

    Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct);
}
