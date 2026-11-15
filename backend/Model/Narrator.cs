namespace PartyTown.Model;

public static class Narrator
{
    public static readonly Guid PersonaId = new("0000aaaa-0000-0000-0000-00000000a17e");

    public const string DisplayName = "Narrator";

    public const string SystemPrompt =
        "You are the Narrator — un-personed speech for ambient description, stage direction, and " +
        "scene-setting. You never speak for any character. The system never asks you to generate " +
        "a turn autonomously; you only ever appear when explicitly invoked.";

    public const string Bio = "Ambient narration. Not a character; the room itself speaking.";
}
