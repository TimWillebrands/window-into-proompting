using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

/// <summary>A thing's words against the catalogue (ADR 0027): a model whose head noun the description names, best overlap first.</summary>
public static partial class CatalogueMatch
{
    private static readonly string[] Prefixes = { "k-", "kl-", "q-" };
    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "the", "and", "with", "for", "from", "that", "this", "her", "his", "their", "your", "some", "big", "small", "little",
        "new", "old", "model", "outdoors", "lift", "onto",
    };

    [GeneratedRegex(@"[a-z]+")]
    private static partial Regex Word();

    public static IReadOnlyList<PrefabInfo> Catalogue
        => PlaceDesign.Prefabs.Values.Where(p => Prefixes.Any(x => p.Name.StartsWith(x, StringComparison.Ordinal))).ToList();

    /// <summary>Up to <paramref name="take"/> catalogue models whose head noun ("plant" of k-potted-plant) the description names.</summary>
    public static List<PrefabInfo> Candidates(string description, IEnumerable<PrefabInfo> catalogue, int take = 5)
    {
        var said = Words(description).ToHashSet();
        return catalogue
            .Select(p => (Prefab: p, Head: Head(p.Name), Words: Words($"{p.Name.Replace('-', ' ')} {p.About}").ToHashSet()))
            .Where(c => said.Contains(c.Head))
            .OrderByDescending(c => c.Words.Count(said.Contains))
            .ThenBy(c => c.Prefab.Name.Length)
            .Take(take)
            .Select(c => c.Prefab)
            .ToList();
    }

    private static string Head(string name) => name[(name.LastIndexOf('-') + 1)..];

    private static IEnumerable<string> Words(string text)
        => Word().Matches(text.ToLowerInvariant()).Select(m => Singular(m.Value)).Where(w => w.Length >= 3 && !Stop.Contains(w));

    private static string Singular(string w) => w.Length > 4 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal) ? w[..^1] : w;
}
