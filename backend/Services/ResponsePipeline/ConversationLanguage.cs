using System.Text.RegularExpressions;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>Which language a persona has been speaking in a conversation: a fact for the Speaking phase, so the line doesn't drift with the last speaker.</summary>
public static partial class ConversationLanguage
{
    /// <summary>Own lines read back to settle the language you've been using here.</summary>
    public const int OwnLinesRead = 3;

    /// <summary>Marker words a guess needs; fewer and the line is too short to tell.</summary>
    public const int MinMarkers = 2;

    // Words that are common in one language and rare in the others here; shared ones (de, en, je, die, is, was) are left out.
    private static readonly Dictionary<string, HashSet<string>> Markers = new()
    {
        ["English"] = Set("the and you are to of it that what have not with for this but just my your don't can i i'm it's at be so did how will if they she he we there about would yes ok okay"),
        ["Dutch"] = Set("het een ik jij niet dat wat maar ook nog van voor zijn heb hebt heeft wel naar mijn jouw nee zo hoe ben bent toch gewoon eigenlijk heel wil moet zit weet goed hij zij geen kom komt gaat ga alleen nu dus omdat bij uit waar eens daar graag lekker jullie mij tot straks"),
        ["French"] = Set("le les des du une est et tu vous pas ne pour avec mais sur dans ce c'est j'ai elle nous mon ma mes ton ta oui très bien au aux ça moi toi suis qui fait"),
        ["German"] = Set("ich nicht und ist das wir eine einen für aber auch schon nein sehr habe bist sind noch mit dem"),
        ["Spanish"] = Set("el los las y está pero muy sí yo tú usted porque también estoy qué"),
        ["Portuguese"] = Set("não você eu é muito obrigado obrigada uma com mas também estou então"),
    };

    private static HashSet<string> Set(string words) => words.Split(' ').ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"[\p{L}'’]+")]
    private static partial Regex Word();

    /// <summary>The line's language, or null when it's too short or mixed to tell.</summary>
    public static string? Guess(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var words = Word().Matches(text.ToLowerInvariant()).Select(m => m.Value.Replace('’', '\'')).ToList();
        var scores = Markers.Select(kv => (Language: kv.Key, Hits: words.Count(kv.Value.Contains)))
            .OrderByDescending(s => s.Hits).ToList();
        var (best, second) = (scores[0], scores[1]);
        return best.Hits >= MinMarkers && best.Hits >= 2 * second.Hits ? best.Language : null;
    }

    /// <summary>The language of your own last few lines here, the latest breaking a tie; null when you haven't spoken or it can't be told.</summary>
    public static string? Own(IReadOnlyList<ChatMessage> held, Guid self)
    {
        var guesses = held.Where(m => m.SenderId == self && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(OwnLinesRead).Select(m => Guess(m.Content)).Where(g => g is not null).Select(g => g!).ToList();
        if (guesses.Count == 0) return null;
        return guesses.GroupBy(g => g).OrderByDescending(g => g.Count()).ThenByDescending(g => guesses.LastIndexOf(g.Key)).First().Key;
    }

    /// <summary>The recency cue: what you've been speaking here, and what to do when the other side switched; null when nothing is known.</summary>
    public static string? Cue(string? own, string? theirs, string? theirName)
    {
        var who = string.IsNullOrWhiteSpace(theirName) ? "they" : theirName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (own is not null && (theirs is null || theirs == own))
            return $"Language: you've been talking {own} here; stay in it.";
        if (own is not null)
            return $"Language: you've been talking {own} here and {who} just used {theirs}. Switch only if {theirs} is a language you'd really speak with {who}, and then stay in it; otherwise keep to {own}. One language per message, the odd word aside.";
        if (theirs is not null)
            return $"Language: {who} wrote in {theirs}. Answer in it if it's a language you'd really speak with {who}, otherwise in your own, and keep to that choice from here. One language per message, the odd word aside.";
        return null;
    }
}
