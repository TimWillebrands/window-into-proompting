using System.Text.RegularExpressions;

namespace PartyTown.Services.Memory;

public static class RecollectionFidelity
{
    private const int MaxSnippetChars = 500;

    public static bool HasUnattributedFirstPerson(string snippet)
    {
        var unquoted = System.Text.RegularExpressions.Regex.Replace(
            snippet, "\"[^\"]*\"|“[^”]*”|‘[^’]*’", " ");
        return System.Text.RegularExpressions.Regex.IsMatch(
            unquoted,
            @"\b(i|i'm|i've|i'd|i'll|me|my|mine|myself)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

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

    private static string Stem(string w)
    {
        if (w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal)) return w[..^3];
        if (w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal)) return w[..^2];
        if (w.Length > 3 && w.EndsWith("es", StringComparison.Ordinal)) return w[..^2];
        if (w.Length > 3 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal)) return w[..^1];
        return w;
    }
}
