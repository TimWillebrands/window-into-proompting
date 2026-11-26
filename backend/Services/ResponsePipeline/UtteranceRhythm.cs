namespace PartyTown.Services.ResponsePipeline;

public static class UtteranceRhythm
{
    public enum Length { Short, Medium, Long, Full }

    public const int ShortLineWords = 15;
    public const int MidLineWords = 40;

    public const double ShortAfterShort = 0.70;
    public const double MediumAfterShort = 0.25;
    public const double ShortAfterMid = 0.45;
    public const double MediumAfterMid = 0.40;
    public const double ShortAfterLong = 0.30;
    public const double MediumAfterLong = 0.45;

    public const double Light = 0.25;
    public const double FullExponent = 1.5;

    public static Length Pick(int lastOtherWords, double roll, double? gravity = null)
    {
        var (shortP, mediumP) = lastOtherWords switch
        {
            <= ShortLineWords => (ShortAfterShort, MediumAfterShort),
            <= MidLineWords => (ShortAfterMid, MediumAfterMid),
            _ => (ShortAfterLong, MediumAfterLong),
        };
        var asked = gravity is { } g ? Asked(g) : 0;
        if (asked <= 0)
            return roll < shortP ? Length.Short : roll < shortP + mediumP ? Length.Medium : Length.Long;

        var fullP = Math.Pow(asked, FullExponent);
        if (roll < fullP) return Length.Full;
        var rest = fullP >= 1 ? 0 : (roll - fullP) / (1 - fullP);
        var longP = 1 - shortP - mediumP;
        var weighedShort = shortP * (1 - asked);
        var weighedMedium = (1 - weighedShort) * mediumP / (mediumP + longP);
        return rest < weighedShort ? Length.Short : rest < weighedShort + weighedMedium ? Length.Medium : Length.Long;
    }

    public static double Asked(double gravity) => Math.Clamp((gravity - Light) / (1 - Light), 0, 1);

    public static string Cue(Length length) => length switch
    {
        Length.Short => "Length: one short line this time, under 12 words.",
        Length.Medium => "Length: a sentence or two this time, under 30 words.",
        Length.Long => "Length: you can take a bit more room this time if you have something to say, but under 60 words.",
        _ => "Length: this one deserves a real answer, so take the room: a short paragraph, under 70 words. Say what you actually think or feel about it: the one or two things that matter most, not a tour of everything. Nothing made up to fill it out, and you don't have to hand the question back. Write it the way you'd write it in a chat, not as an essay.",
    };

    public static int Words(string? text)
        => string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
