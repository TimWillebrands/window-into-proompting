namespace PartyTown.Model;

/// <summary>
/// Singleton library Persona representing un-personed speech (narration, ambient description,
/// stage direction). Joins every Party as a <see cref="DriverKind.System"/>-driven Participant.
/// The response pipeline never auto-generates a turn for the Narrator. See ADR 0012.
/// </summary>
public static class Narrator
{
    /// <summary>
    /// Well-known stable Persona id for the Narrator. Stable across deployments so that
    /// imports and exports can refer to the same Persona without per-environment lookup.
    /// </summary>
    public static readonly Guid PersonaId = new("0000aaaa-0000-0000-0000-00000000a17e");

    public const string DisplayName = "Narrator";

    public const string SystemPrompt =
        "You are the Narrator — un-personed speech for ambient description, stage direction, and " +
        "scene-setting. You never speak for any character. The system never asks you to generate " +
        "a turn autonomously; you only ever appear when explicitly invoked.";

    public const string Bio = "Ambient narration. Not a character; the room itself speaking.";
}
