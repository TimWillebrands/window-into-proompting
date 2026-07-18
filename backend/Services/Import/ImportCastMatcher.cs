using PartyTown.Model;

namespace PartyTown.Services.Import;

/// <summary>
/// Match-or-mint proposal side (ADR 0017, mechanism from ADR 0013): when a cast member
/// enters the registry, scan the persona library for a same-person candidate — exact
/// name equality first, then Levenshtein over the entry's primary name + aliases. Pure
/// (library snapshot in → proposal out); the grain records the proposal, the human
/// decides, commit executes.
/// </summary>
public static class ImportCastMatcher
{
    /// <summary>Best library candidate for one cast entry, or null when nothing is near.
    /// Exact (case-insensitive, honorific/parenthetical-normalized) beats fuzzy; fuzzy
    /// accepts a Levenshtein distance ≤ 1 for short names, ≤ 2 from 5 chars up.</summary>
    public static PersonaMetadata? ProposeMatch(
        string name, IReadOnlyList<string> aliases, IReadOnlyList<PersonaMetadata> library)
    {
        var candidates = new List<string> { name };
        candidates.AddRange(aliases.Where(a => !string.IsNullOrWhiteSpace(a)));
        var normalized = candidates
            .Select(c => ImportFold.NormalizeName(c).ToLowerInvariant())
            .Where(c => c.Length > 0)
            .Distinct()
            .ToList();
        if (normalized.Count == 0) return null;

        PersonaMetadata? best = null;
        var bestDistance = int.MaxValue;
        foreach (var persona in library)
        {
            var libraryName = ImportFold.NormalizeName(persona.Name).ToLowerInvariant();
            if (libraryName.Length == 0) continue;
            foreach (var candidate in normalized)
            {
                var distance = Levenshtein(candidate, libraryName);
                if (distance > Tolerance(candidate, libraryName)) continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = persona;
                }
            }
        }
        return best;
    }

    private static int Tolerance(string a, string b)
        => Math.Min(a.Length, b.Length) switch
        {
            < 3 => 0,
            < 5 => 1,
            _ => 2,
        };

    internal static int Levenshtein(string a, string b)
    {
        if (a.Length == 0) return b.Length;
        if (b.Length == 0) return a.Length;

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
