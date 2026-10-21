using System.Text.RegularExpressions;
using PartyTown.Services.Memory;

namespace PartyTown.World.Self;

/// <summary>
/// One story, one slot (ADR 0021): every retelling becomes a new memory, so a favourite story crowds recall and
/// gets told again. Near-duplicates collapse to the strongest, and stories you told someone say so.
/// </summary>
public static partial class RecallVariety
{
    public const double SameStory = 0.5;

    /// <summary>Snippet verbs that say how a memory was made, not what it's about.</summary>
    private static readonly HashSet<string> Framing = new(StringComparer.Ordinal)
    {
        "told", "tell", "tells", "heard", "said", "says", "asked", "asks", "mentioned", "talked", "called", "once",
    };

    public static RecallBeat Apply(RecallBeat beat, IReadOnlyCollection<string> names)
        => beat.Memories.Count == 0 ? beat : new RecallBeat(Collapse(beat.Memories, m => m.Snippet, names).Select(Annotate).ToList(), beat.Stances);

    public static List<T> Collapse<T>(IReadOnlyList<T> items, Func<T, string> text, IReadOnlyCollection<string> names)
    {
        var kept = new List<(T Item, List<string> Words, HashSet<string> Pairs)>();
        foreach (var item in items)
        {
            var words = VoiceWear.ContentWords(text(item), names).Where(w => !Framing.Contains(w)).ToList();
            var pairs = Pairs(words);
            if (kept.Any(k => VoiceWear.Overlap(k.Words, words) >= SameStory || k.Pairs.Overlaps(pairs))) continue;
            kept.Add((item, words, pairs));
        }
        return kept.Select(k => k.Item).ToList();
    }

    /// <summary>Adjacent content-word pairs of 4+ letters: "coldest address", "espresso machine".</summary>
    private static HashSet<string> Pairs(List<string> words)
        => words.Zip(words.Skip(1)).Where(p => p.First.Length >= 4 && p.Second.Length >= 4 && p.First != p.Second)
            .Select(p => $"{p.First} {p.Second}").ToHashSet();

    public static RecalledMemory Annotate(RecalledMemory memory)
        => Told(memory.Snippet) is { } who ? memory with { Snippet = $"{memory.Snippet.TrimEnd()} ({who} already heard this from you)" } : memory;

    /// <summary>Who a "You told X…" memory was told to, or null.</summary>
    public static string? Told(string snippet)
        => YouTold().Match(snippet) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"\b(?i:you told) ((?:[A-Z][\p{L}'’-]+)(?:(?:,? and |, | )[A-Z][\p{L}'’-]+)*)")]
    private static partial Regex YouTold();
}
