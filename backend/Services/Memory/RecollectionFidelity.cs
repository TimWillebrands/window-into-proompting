using System.Text.RegularExpressions;

namespace PartyTown.Services.Memory;

/// <summary>
/// Perspective-fidelity policy for Recollection snippets — the write-side guard that keeps
/// corrupted memories out of the graph without throwing away the moment.
///
/// A snippet is read back later by ITS person alone, so "you" must mean that person and
/// everyone else must be named. Small extraction models reliably fumble this perspective
/// transformation ("You heard me announce I'm quitting" stored for the person who SPOKE)
/// while getting the neutral third-person Event description right — bench: every corrupted
/// draft in a run sat next to a perfectly good description. So instead of dropping a
/// corrupted draft (which erases the moment for that persona entirely), the capture path
/// substitutes a snippet templated from the Event description: the model does what it's
/// good at (neutral summary + weight judgment), code does the perspective. The weight is
/// the model's — the salience call wasn't the corrupted part.
/// </summary>
public static class RecollectionFidelity
{
    private const int MaxSnippetChars = 500;

    /// <summary>
    /// True when the snippet contains a first-person pronoun OUTSIDE quoted speech.
    /// "You announced \"I quit\" to Vlad" is fine (the pronoun is quoted, attributable);
    /// "You heard me announce I'm quitting" is not — outside quotes, "me"/"I'm" has no
    /// referent for the person reading the memory back later, and in practice marks a
    /// swapped subject.
    /// </summary>
    public static bool HasUnattributedFirstPerson(string snippet)
    {
        // Blank out straight- and curly-quoted spans, then scan what's left.
        var unquoted = System.Text.RegularExpressions.Regex.Replace(
            snippet, "\"[^\"]*\"|“[^”]*”|‘[^’]*’", " ");
        // Singular first person only — "we"/"us" can legitimately include the rememberer
        // ("Denise invited us all"). Bare "i" also catches curly-apostrophe contractions
        // ("I’m") since the apostrophe is a word boundary.
        return System.Text.RegularExpressions.Regex.IsMatch(
            unquoted,
            @"\b(i|i'm|i've|i'd|i'll|me|my|mine|myself)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Sanitizes one persona's draft against the Event description. A clean draft passes
    /// through; a corrupted one keeps its weight but trades its snippet for the templated
    /// description — deliberately third-person ("You remember: Denise announced …"):
    /// slightly stiff to read back, never wrong about who did what.
    /// </summary>
    public static (RecollectionDraft Draft, bool Substituted) Sanitize(
        RecollectionDraft draft, string eventDescription)
    {
        if (!HasUnattributedFirstPerson(draft.Snippet))
            return (draft, false);

        var fallback = $"You remember: {eventDescription.Trim()}";
        if (fallback.Length > MaxSnippetChars)
            fallback = fallback[..MaxSnippetChars];

        return (draft with { Snippet = fallback }, true);
    }

    // ── Near-duplicate detection (capture-time dedup of substituted snippets) ──────────
    //
    // A substituted snippet is a neutral Event description, and two Events captured
    // moments apart in one conversation often describe the same development in different
    // words — so a persona whose drafts corrupt twice banks two "You remember:" copies of
    // the same fact (they sit on DIFFERENT Events, so identity can't dedup them). Content-
    // word overlap catches the pair without an LLM call or embeddings: drop stopwords,
    // lightly stem, then require a solid shared core that also covers most of the smaller
    // snippet. The absolute floor keeps short snippets that merely share a cast ("Denise
    // told Vlad about her cat") from colliding just by naming the same people.
    private const int MinSharedContentWords = 5;
    private const double MinOverlapCoefficient = 0.55;

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "but", "of", "to", "in", "on", "at", "for", "from",
        "with", "after", "before", "about", "into", "over", "up", "out", "by", "as", "is",
        "are", "was", "were", "be", "been", "being", "do", "does", "did", "has", "have",
        "had", "will", "would", "can", "could", "that", "this", "these", "those", "it",
        "its", "he", "she", "him", "her", "his", "hers", "they", "them", "their", "you",
        "your", "yours", "i", "me", "my", "we", "us", "our", "not", "no", "so", "if",
        "then", "than", "when", "while", "who", "whom", "which", "there", "here", "now",
        "remember",
    };

    public static bool IsNearDuplicate(string a, string b)
    {
        var wordsA = ContentWords(a);
        var wordsB = ContentWords(b);
        if (wordsA.Count == 0 || wordsB.Count == 0)
            return false;
        // Verbatim repeats (equal content-word sets) are duplicates no matter how short.
        if (wordsA.SetEquals(wordsB))
            return true;

        var shared = wordsA.Count(wordsB.Contains);
        return shared >= MinSharedContentWords
            && shared >= MinOverlapCoefficient * Math.Min(wordsA.Count, wordsB.Count);
    }

    private static HashSet<string> ContentWords(string snippet)
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(snippet.ToLowerInvariant(), @"[a-z][a-z']*"))
        {
            var w = m.Value;
            if (w.EndsWith("'s", StringComparison.Ordinal))
                w = w[..^2];
            w = w.Trim('\'');
            if (w.Length < 2 || Stopwords.Contains(w))
                continue;
            words.Add(Stem(w));
        }
        return words;
    }

    /// <summary>
    /// Suffix-stripping just aggressive enough to unify the inflections seen in Event
    /// descriptions ("signing"/"signed" → sign, "opens" → open, "invited"/"inviting" →
    /// invit). Not a linguistic stemmer — over-stripping only risks a shared token, and
    /// the overlap thresholds absorb that.
    /// </summary>
    private static string Stem(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) return w[..^3];
        if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal)) return w[..^2];
        if (w.Length > 3 && w.EndsWith("es", StringComparison.Ordinal)) return w[..^2];
        if (w.Length > 3 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal)) return w[..^1];
        return w;
    }
}
