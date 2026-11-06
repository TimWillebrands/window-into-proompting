namespace PartyTown.Services.ResponsePipeline;

/// <summary>The generator's user message: the seed, a random Draw, and the World the newcomer lands in.</summary>
public static class PersonaBrief
{
    private const string World = "Amsterdam, today.";
    private const int BioChars = 140;

    /// <summary>Independent calls can't see each other's dice, and left alone they all write someone guarded and wry.</summary>
    public static readonly string[] Temperaments =
    [
        "warm, nosy, talks to anyone",
        "cheerful busybody who keeps track of everyone",
        "shy, but glad when someone makes the first move",
        "blunt and loud, generous with favours",
        "gentle listener who remembers small details about people",
        "anxious to be liked, over-helpful",
        "competitive, teases the people they like",
        "formal and courteous, slow to first names",
        "easily bored, always recruiting people for something",
        "self-contained, content with a few people",
        "grumpy on the surface, quietly looks after people",
        "earnest, takes people at their word, easily fooled",
    ];

    public static readonly string[] LifeStages =
    [
        "student, still working out what they're doing",
        "early twenties, first proper job",
        "late twenties, settling down or refusing to",
        "thirties, small kids at home",
        "forties, mid-career, teenagers or ageing parents",
        "fifties, kids gone, work on autopilot",
        "sixties, counting down to retirement",
        "seventies, retired, time on their hands",
        "eighties, widowed, the world getting smaller",
    ];

    public static string Compose(string seed, Random random, IReadOnlyList<string> places, IReadOnlyList<(string Name, string? Bio)> residents)
    {
        var lines = new List<string>
        {
            $"Seed: {seed.Trim()}",
            string.Empty,
            "Draw (use unless the seed says otherwise):",
            $"- Temperament: {Temperaments[random.Next(Temperaments.Length)]}",
            $"- Life stage: {LifeStages[random.Next(LifeStages.Length)]}",
            string.Empty,
            $"This world: {World}",
        };
        if (places.Count > 0) lines.Add($"Places here: {string.Join(", ", places)}");
        if (residents.Count > 0)
        {
            lines.Add("Already living here (be unlike them in age, trade, background, temperament; don't reuse their names or their people's names):");
            lines.AddRange(residents.Select(r => string.IsNullOrWhiteSpace(r.Bio) ? $"- {r.Name}" : $"- {r.Name}: {Clip(r.Bio)}"));
        }
        return string.Join("\n", lines);
    }

    private static string Clip(string bio)
    {
        var line = bio.ReplaceLineEndings(" ").Trim();
        return line.Length <= BioChars ? line : line[..BioChars].TrimEnd() + "…";
    }
}
