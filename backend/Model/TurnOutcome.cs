namespace PartyTown.Model;

/// <summary>How a Persona's reserved turn in a Room ended. Exactly one per turn.</summary>
[GenerateSerializer, Alias(nameof(TurnOutcome))]
public abstract record class TurnOutcome
{
    /// <summary>Speaking phase shipped a message; the Room fans it out.</summary>
    [GenerateSerializer, Alias("TurnOutcome.Spoke")]
    public sealed record class Spoke(
        [property: Id(0)] string Content,
        [property: Id(1)] string? Reasoning,
        [property: Id(2)] string? Appraisal,
        [property: Id(3)] ChatMessageMetadata? Metadata) : TurnOutcome;

    /// <summary>Decision phase chose silence, or an initiative was cut off.</summary>
    [GenerateSerializer, Alias("TurnOutcome.Declined")]
    public sealed record class Declined([property: Id(0)] string? Appraisal) : TurnOutcome;

    /// <summary>Stop-signal race cut the draft; an in-character emote takes the slot (ADR 0001).</summary>
    [GenerateSerializer, Alias("TurnOutcome.Interrupted")]
    public sealed record class Interrupted(
        [property: Id(0)] string Emote,
        [property: Id(1)] string? Appraisal) : TurnOutcome;

    /// <summary>Error or external cancel.</summary>
    [GenerateSerializer, Alias("TurnOutcome.Failed")]
    public sealed record class Failed([property: Id(0)] string Error) : TurnOutcome;
}
