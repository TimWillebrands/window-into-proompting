namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Per-turn length target (ADR 0021): real chat is mostly short lines and tracks the other person's length.
/// Sampled, so a persona sometimes goes long; never uniform.
/// </summary>
public static class UtteranceRhythm
{
    public enum Length { Short, Medium, Long }

    /// <param name="lastOtherWords">Words in the latest line from someone else; 0 when opening.</param>
    /// <param name="roll">Uniform [0,1).</param>
    public static Length Pick(int lastOtherWords, double roll)
    {
        var (shortP, mediumP) = lastOtherWords switch
        {
            <= 15 => (0.70, 0.25),
            <= 40 => (0.45, 0.40),
            _ => (0.30, 0.45),
        };
        return roll < shortP ? Length.Short : roll < shortP + mediumP ? Length.Medium : Length.Long;
    }

    public static string Cue(Length length) => length switch
    {
        Length.Short => "Length: one short line this time, under 12 words.",
        Length.Medium => "Length: a sentence or two this time, under 30 words.",
        _ => "Length: you can take a bit more room this time if you have something to say, but under 60 words.",
    };

    public static int Words(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
