using PartyTown.Model;

namespace PartyTown.World.Apps;

/// <summary>Everything an App needs to know about the Persona whose Life is ticking.</summary>
public sealed record LifeContext(
    Guid PartyId,
    CastMember Self,
    IReadOnlyList<CastMember> Cast,
    Guid? PlayerId,
    IReadOnlyList<ChatGroupInfo> Threads,
    IReadOnlyList<WorldActivity> RecentOwn,
    DateTimeOffset Now)
{
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
public sealed record Affordance(string AppId, string Label, int Priority, Guid? TargetId = null, string? Verb = null);

/// <summary>What the Persona sees of one App: a prose block plus the Affordances it offers.</summary>
public sealed record AppPerception(string AppId, string Title, string Block, IReadOnlyList<Affordance> Affordances);

public sealed record ActOutcome(string Action, string Summary, Guid? TargetId);

/// <summary>A mode of engagement inside a Party. Adding a mode = adding an App; Life is untouched.</summary>
public interface IWorldApp
{
    string Id { get; }

    Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct);

    Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct);
}
