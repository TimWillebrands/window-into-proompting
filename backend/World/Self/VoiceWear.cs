using System.Text.RegularExpressions;

namespace PartyTown.World.Self;

/// <summary>One spoken line, as the wear analysis sees it.</summary>
public readonly record struct Utterance(Guid SpeakerId, string Text);

/// <summary>What's gone stale: shared words worn out across the world, and one speaker's own tics.</summary>
[GenerateSerializer, Alias("PartyTown.World.Self.VoiceWearReport")]
public sealed record VoiceWearReport
{
    /// <summary>Words several speakers keep reaching for (the world's monoculture).</summary>
    [Id(0)] public List<string> WornWords { get; init; } = new();

    /// <summary>Per speaker: own over-used words.</summary>
    [Id(1)] public Dictionary<Guid, List<string>> OwnWords { get; init; } = new();

    /// <summary>Per speaker: share of recent lines that open with an *action*.</summary>
    [Id(2)] public Dictionary<Guid, double> StageDirectionRate { get; init; } = new();

    /// <summary>Per speaker: average line length in words.</summary>
    [Id(3)] public Dictionary<Guid, double> AverageWords { get; init; } = new();

    public static readonly VoiceWearReport Empty = new();
}

/// <summary>
/// Anti-convergence signal: personas drift into one shared literary register ("load-bearing", "seam",
/// "lantern"). Deterministic counts over recent lines, no LLM.
/// </summary>
public static partial class VoiceWear
{
    private const int MinWordLength = 4;
    private const int WornMinSpeakers = 2;
    private const int WornMinCount = 5;
    private const int OwnMinCount = 3;
    private const int OwnWindow = 12;
    private const int MaxWords = 10;

    [GeneratedRegex(@"[\p{L}][\p{L}'\-]*[\p{L}]")]
    private static partial Regex Word();

    [GeneratedRegex(@"^\s*\*[^*\n]{2,}\*")]
    private static partial Regex LeadingAction();

    public static VoiceWearReport Analyze(IReadOnlyList<Utterance> recent, IEnumerable<string>? names = null)
    {
        if (recent.Count == 0) return VoiceWearReport.Empty;
        var nameWords = (names ?? Array.Empty<string>())
            .SelectMany(n => Word().Matches(n.ToLowerInvariant()).Select(m => m.Value))
            .ToHashSet();

        bool Counts(string w) => w.Length >= MinWordLength && !Stopwords.Contains(w) && !nameWords.Contains(w);
        List<string> Tokens(string text) => Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value.Trim('\'', '-')).Where(Counts).ToList();

        var tokenized = recent.Select(u => (u.SpeakerId, Tokens: Tokens(u.Text), u.Text)).ToList();

        var worn = tokenized
            .SelectMany(u => u.Tokens.Distinct().Select(w => (w, u.SpeakerId)))
            .GroupBy(x => x.w)
            .Select(g => (Word: g.Key, Lines: g.Count(), Speakers: g.Select(x => x.SpeakerId).Distinct().Count()))
            .Where(x => x.Speakers >= WornMinSpeakers && x.Lines >= WornMinCount)
            .OrderByDescending(x => x.Lines * x.Speakers)
            .Take(MaxWords)
            .Select(x => x.Word)
            .ToList();

        var own = new Dictionary<Guid, List<string>>();
        var stage = new Dictionary<Guid, double>();
        var avg = new Dictionary<Guid, double>();
        foreach (var speaker in tokenized.GroupBy(u => u.SpeakerId))
        {
            var lines = speaker.TakeLast(OwnWindow).ToList();
            own[speaker.Key] = lines
                .SelectMany(u => u.Tokens.Distinct())
                .GroupBy(w => w)
                .Where(g => g.Count() >= OwnMinCount)
                .OrderByDescending(g => g.Count())
                .Take(MaxWords / 2)
                .Select(g => g.Key)
                .ToList();
            stage[speaker.Key] = lines.Count(u => LeadingAction().IsMatch(u.Text)) / (double)lines.Count;
            avg[speaker.Key] = lines.Average(u => u.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
        }

        return new VoiceWearReport { WornWords = worn, OwnWords = own, StageDirectionRate = stage, AverageWords = avg };
    }

    /// <summary>Prompt lines for one speaker; empty when nothing is worn.</summary>
    public static IReadOnlyList<string> RenderFor(VoiceWearReport report, Guid speakerId)
    {
        var lines = new List<string>();
        var own = report.OwnWords.TryGetValue(speakerId, out var o) ? o : new List<string>();
        var shared = report.WornWords.Except(own).ToList();
        if (shared.Count > 0)
            lines.Add($"Everyone around here keeps reaching for the same words lately: {string.Join(", ", shared)}. They've gone stale, so leave them out (and don't remark on leaving them out).");
        if (own.Count > 0)
            lines.Add($"You personally keep coming back to: {string.Join(", ", own)}. Give those a rest.");
        if (report.StageDirectionRate.TryGetValue(speakerId, out var rate) && rate >= 0.5)
            lines.Add("Most of your recent messages open with an *action*. You're typing, not on stage — skip it.");
        if (report.AverageWords.TryGetValue(speakerId, out var words) && words >= 60)
            lines.Add($"Your messages have been long (about {Math.Round(words)} words). People skim; keep it short.");
        return lines;
    }

    /// <summary>Your own latest lines, wherever you said them, so you don't repeat yourself across Rooms.</summary>
    public static string RenderOwnRecent(IReadOnlyList<VoiceLine> own, DateTimeOffset now)
    {
        if (own.Count == 0) return string.Empty;
        var lines = own.Select(l => $"- {WorldStream.Ago(l.Ts, now)}, {l.Room}: \"{WorldStream.Clip(l.Text, 160)}\"");
        return $"# What you said lately\n{string.Join("\n", lines)}\nPeople talk to each other; assume it got around. Don't say the same thing again, here or anywhere.\n";
    }

    private static readonly HashSet<string> Stopwords = new(StringComparer.Ordinal)
    {
        "about", "above", "after", "again", "against", "all", "also", "always", "among", "another", "anyone",
        "anything", "around", "back", "because", "been", "before", "being", "below", "between", "both",
        "came", "can't", "cannot", "come", "could", "couldn't", "didn't", "does", "doesn't", "doing", "done",
        "don't", "down", "during", "each", "else", "even", "ever", "every", "feel", "find", "first", "from",
        "further", "gets", "give", "going", "gone", "good", "got", "great", "had", "hadn't", "hasn't", "have",
        "haven't", "having", "he'd", "he'll", "he's", "hear", "here", "here's", "hers", "herself", "him",
        "himself", "his", "how's", "i'd", "i'll", "i'm", "i've", "into", "isn't", "it's", "its", "itself",
        "just", "keep", "kind", "know", "last", "least", "less", "let's", "like", "little", "long", "look",
        "made", "make", "many", "maybe", "mean", "might", "more", "most", "much", "must", "myself", "need",
        "never", "next", "nothing", "now", "okay", "once", "one", "only", "other", "others", "ought", "ours",
        "ourselves", "over", "own", "part", "people", "really", "right", "said", "same", "say", "says",
        "see", "she'd", "she'll", "she's", "should", "shouldn't", "since", "some", "something", "still",
        "such", "sure", "take", "tell", "than", "that", "that's", "thats", "the", "their", "theirs", "them",
        "themselves", "then", "there", "there's", "these", "they", "they'd", "they'll", "they're", "they've",
        "thing", "things", "think", "this", "those", "though", "through", "time", "today", "together", "told",
        "too", "under", "until", "upon", "very", "wanted", "wants", "want", "was", "wasn't", "way", "we'd",
        "we'll", "we're", "we've", "well", "went", "were", "weren't", "what", "what's", "when", "when's",
        "where", "where's", "which", "while", "who's", "whom", "whose", "why's", "will", "with", "without",
        "won't", "would", "wouldn't", "yeah", "yes", "you'd", "you'll", "you're", "you've", "your", "yours",
        "yourself", "yourselves", "actually", "already", "enough", "getting", "goes", "hey", "isnt", "dont",
        "im", "ive", "youre", "lot", "lets", "else", "anyway", "pretty", "quite", "rather", "seems", "seem",
        "thank", "thanks", "word", "words", "whole", "years", "year", "week", "night", "day", "days",
        "room", "rooms", "chat", "message", "messages",
    };
}
