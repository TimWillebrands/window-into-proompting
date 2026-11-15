namespace PartyTown.Model;

[GenerateSerializer, Alias(nameof(TurnOutcome))]
public abstract record class TurnOutcome
{
    [GenerateSerializer, Alias("TurnOutcome.Spoke")]
    public sealed record class Spoke(
        [property: Id(0)] string Content,
        [property: Id(1)] string? Reasoning,
        [property: Id(2)] string? Appraisal,
        [property: Id(3)] ChatMessageMetadata? Metadata) : TurnOutcome;

    [GenerateSerializer, Alias("TurnOutcome.Declined")]
    public sealed record class Declined([property: Id(0)] string? Appraisal) : TurnOutcome;

    [GenerateSerializer, Alias("TurnOutcome.Interrupted")]
    public sealed record class Interrupted(
        [property: Id(0)] string Emote,
        [property: Id(1)] string? Appraisal) : TurnOutcome;

    [GenerateSerializer, Alias("TurnOutcome.Failed")]
    public sealed record class Failed([property: Id(0)] string Error) : TurnOutcome;
}
