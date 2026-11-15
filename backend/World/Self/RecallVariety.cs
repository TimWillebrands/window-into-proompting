using System.Text.RegularExpressions;
using PartyTown.Services.Memory;

namespace PartyTown.World.Self;

public static partial class RecallVariety
{
    public const double SameStory = 0.5;

    private static readonly HashSet<string> Framing = new(StringComparer.Ordinal)
    {
        "told", "tell", "tells", "heard", "said", "says", "asked", "asks", "mentioned", "talked", "called", "once",
    };

    public static RecallBeat Apply(RecallBeat beat, IReadOnlyCollection<string> names, DateTimeOffset? now = null)
        => beat.Memories.Count == 0 ? beat : new RecallBeat(Collapse(beat.Memories, m => m.Snippet, names, m => m.CapturedAt)
            .Select(Annotate).Select(m => Aged(m, now ?? DateTimeOffset.UtcNow)).ToList(), beat.Stances);

    public static RecalledMemory Aged(RecalledMemory memory, DateTimeOffset now)
        => memory with { Snippet = $"({WorldStream.Ago(memory.CapturedAt.ToUnixTimeMilliseconds(), now)}) {memory.Snippet}" };

    public static List<T> Collapse<T>(IReadOnlyList<T> items, Func<T, string> text, IReadOnlyCollection<string> names, Func<T, DateTimeOffset>? at = null)
    {
        var kept = new List<(T Item, List<string> Words, HashSet<string> Pairs)>();
        foreach (var item in items)
        {
            var words = VoiceWear.ContentWords(text(item), names).Where(w => !Framing.Contains(w)).ToList();
            var pairs = Pairs(words);
            var same = kept.FindIndex(k => VoiceWear.Overlap(k.Words, words) >= SameStory || k.Pairs.Overlaps(pairs));
            if (same < 0) kept.Add((item, words, pairs));
            else if (at is not null && at(item) > at(kept[same].Item)) kept[same] = kept[same] with { Item = item };
        }
        return kept.Select(k => k.Item).ToList();
    }

    private static HashSet<string> Pairs(List<string> words)
        => words.Zip(words.Skip(1)).Where(p => p.First.Length >= 4 && p.Second.Length >= 4 && p.First != p.Second)
            .Select(p => $"{p.First} {p.Second}").ToHashSet();

    public static RecalledMemory Annotate(RecalledMemory memory)
        => Told(memory.Snippet) is { } who ? memory with { Snippet = $"{memory.Snippet.TrimEnd()} ({who} already heard this from you)" } : memory;

    public static string? Told(string snippet)
        => YouTold().Match(snippet) is { Success: true } m ? m.Groups[1].Value : null;

    [GeneratedRegex(@"\b(?i:you told) ((?:[A-Z][\p{L}'’-]+)(?:(?:,? and |, | )[A-Z][\p{L}'’-]+)*)")]
    private static partial Regex YouTold();
}
