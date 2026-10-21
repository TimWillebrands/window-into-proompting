using System.Text.RegularExpressions;
using PartyTown.Services.Memory;

namespace PartyTown.World.Self;

/// <summary>
/// Habituation for recall (ADR 0021): memories and stances made of words that have gone stale step back,
/// so a world-wide storyline stops resurfacing every beat. At most one stale item stays when there's enough else.
/// </summary>
public static class StaleRecall
{
    public const int KeepFresh = 3;

    public static RecallBeat Apply(RecallBeat beat, IReadOnlyCollection<string> stale)
        => stale.Count == 0
            ? beat
            : new RecallBeat(
                Demote(beat.Memories, m => m.Snippet, stale),
                Demote(beat.Stances, s => $"{s.Reasoning} {s.Contrast}", stale, keepFresh: 2));

    public static IReadOnlyList<T> Demote<T>(IReadOnlyList<T> items, Func<T, string> text, IReadOnlyCollection<string> stale, int keepFresh = KeepFresh)
    {
        if (stale.Count == 0 || items.Count == 0) return items;
        var pattern = new Regex($@"\b(?:{string.Join("|", stale.Select(Regex.Escape))})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var fresh = items.Where(i => !pattern.IsMatch(text(i))).ToList();
        var worn = items.Where(i => pattern.IsMatch(text(i))).ToList();
        return fresh.Count >= keepFresh ? fresh.Concat(worn.Take(1)).ToList() : fresh.Concat(worn).ToList();
    }
}
