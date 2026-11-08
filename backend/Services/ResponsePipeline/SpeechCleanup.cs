using System.Text.RegularExpressions;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>Strips model artefacts from a spoken line: chat-template tokens, a leading "Name:" self-label, narrated stage directions, a planning note, an error placeholder.</summary>
public static partial class SpeechCleanup
{
    [GeneratedRegex(@"^\s*[\[<({]+[^\]>)}\n]{0,200}[\]>)}]+\s*$")]
    private static partial Regex Wrapped();

    [GeneratedRegex(@"\b(?:error|exception|failed|failure|can'?t parse|cannot parse|unparseable|invalid|timeout|timed out|refused|unavailable)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FailureWord();

    [GeneratedRegex(@"^\s*(?:ERROR|Error|error)\s*:")]
    private static partial Regex ErrorPrefix();

    /// <summary>"[ERROR: Can't parse response]", "&lt;&lt;error: 503&gt;&gt;", "Error: …": a failure placeholder, never a line to publish.</summary>
    public static bool IsFailureMarker(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (Wrapped().IsMatch(t)) return FailureWord().IsMatch(t);
        // "Error: coffee not found" is a joke; "Error: request failed" is not.
        return ErrorPrefix().Match(t) is { Success: true } prefix && FailureWord().IsMatch(t[prefix.Length..]);
    }

    // A note about the line, not the line: word budgets, the draft, stale words, which language to answer in.
    [GeneratedRegex(@"\b(?:under|max(?:imum)?|at most|below|fewer than) \d+ words\b|\b\d+ words\b|\bstale words?\b|\bthe draft\b|\bstage direction|\b(?:reply|respond|answer|write|keep it|send it)\b[^.\n]{0,30}\bin (?:english|dutch|french|german|spanish|portuguese)\b", RegexOptions.IgnoreCase)]
    private static partial Regex StrongNote();

    [GeneratedRegex(@"^\s*[A-Z][\w'-]+ (?:asks|says|wants|wrote|writes|mentions|mentioned|is asking|just asked|replied)\b|\bI(?:'ll| will| need to| have to| want to| should) (?:send|reply|respond|answer|write|keep)\b|\b(?:short|light|brief|one line|private line|keep it|avoid|leave out|in english|in dutch|in french|reply|respond)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WeakNote();

    [GeneratedRegex(@"^\s*(?:send(?:ing)? to [^\n:]{1,40}|reply|message|line|final|answer|my reply|text|draft)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex LineLabel();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();

    private static int NoteScore(string paragraph)
        => 2 * StrongNote().Matches(paragraph).Count + WeakNote().Matches(paragraph).Count;

    /// <summary>"Reply short, under 12 words…\n\nthe line" → "the line"; all note → empty. The model thinking aloud before speaking.</summary>
    public static string DropPlanningNote(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var paragraphs = ParagraphBreak().Split(text.Trim()).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        // A lone paragraph needs more evidence: it may just be someone talking about replying.
        if (paragraphs.Count == 1)
            return NoteScore(paragraphs[0]) >= 4 && StrongNote().IsMatch(paragraphs[0]) ? string.Empty : text;
        var kept = paragraphs.SkipWhile(p => StrongNote().IsMatch(p) || NoteScore(p) >= 3).ToList();
        if (kept.Count == paragraphs.Count) return text;
        if (kept.Count == 0) return string.Empty;
        var line = LineLabel().Replace(string.Join("\n\n", kept), string.Empty).Trim();
        return line.Length > 1 && line[0] is '"' or '“' && line[^1] is '"' or '”' ? line[1..^1].Trim() : line;
    }

    /// <summary>"Al wijzer geworden? Ik ben…" answering "Al wijzer geworden?" → "Ik ben…": small models open by echoing the line they answer.</summary>
    public static string DropEcho(string text, string? answering)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(answering)) return text;
        var echo = answering.Trim();
        if (echo.Length < 12) return text;
        var t = text.TrimStart();
        if (!t.StartsWith(echo, StringComparison.OrdinalIgnoreCase)) return text;
        return t[echo.Length..].TrimStart(' ', ',', '-', '—', '–', ':', '\n').Trim();
    }

    [GeneratedRegex(@"<\|[^|>]{1,40}\|>")]
    private static partial Regex TemplateToken();

    [GeneratedRegex(@"(?<!\*)\*(?!\*)([^*\n]+)\*(?!\*)")]
    private static partial Regex Action();

    /// <summary>Narrated actions of 4+ words (<c>*sets the phone down on the table*</c>) are stage directions; a short <c>*sigh*</c> stays.</summary>
    public static string StripStageDirections(string text)
    {
        var stripped = Action().Replace(text, m => IsNarration(m.Groups[1].Value) ? " " : m.Value);
        stripped = Regex.Replace(stripped, @"[ \t]{2,}", " ");
        stripped = Regex.Replace(stripped, @"^\s*[—–]\s+", "");
        return string.IsNullOrWhiteSpace(stripped) ? text : stripped.Trim();
    }

    // Lowercase start separates "*leans back in the chair*" from emphasis like "*I mean it this time*".
    private static bool IsNarration(string inner)
        => inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4 && char.IsLower(inner.TrimStart()[0]);

    /// <summary>"Denise, eat first." answering Denise → "Eat first.": the name is obvious, and it would re-trigger her mention auto-reply (ADR 0021).</summary>
    public static string DropObviousVocative(string text, string? lastSpeakerName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(lastSpeakerName)) return text;
        var first = lastSpeakerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var m = Regex.Match(text, $@"^\s*(?:{Regex.Escape(lastSpeakerName)}|{Regex.Escape(first)})(?:\s*[,:]\s*|\s+[—–-]\s+)(?=\S)", RegexOptions.IgnoreCase);
        if (!m.Success) return text;
        var rest = text[m.Length..];
        // "Ann, Ben and I went" is a list, not an address.
        if (rest.Length < 3 || Regex.IsMatch(rest, @"^[A-Z][\w'-]*\s+and\b")) return text;
        return char.IsUpper(text.TrimStart()[0]) ? char.ToUpperInvariant(rest[0]) + rest[1..] : rest;
    }

    public static string Clean(string? text, string? selfName)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (IsFailureMarker(text)) return string.Empty;
        var s = DropPlanningNote(TemplateToken().Replace(text, " ").Trim());
        if (IsFailureMarker(s)) return string.Empty;
        // "private" survives as a bare word when the template was <|channel|>private<|message|>.
        if (s.StartsWith("private ", StringComparison.Ordinal) && text.Contains("<|channel|>")) s = s[8..].TrimStart();
        // A leaked room label: "#kitchen: hi" or a trailing "Going. #test:" header.
        s = Regex.Replace(s, @"(^|\s)#[A-Za-z][\w-]*:\s*", "$1").Trim();
        if (!string.IsNullOrWhiteSpace(selfName))
        {
            var prefix = new Regex($@"^\**{Regex.Escape(selfName)}\**\s*:\s*\**\s*", RegexOptions.IgnoreCase);
            s = prefix.Replace(s, string.Empty);
            // Addressing yourself ("Eiko, you're still at the counter" from Eiko) is a model slip; drop the name.
            var first = selfName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            var self = Regex.Match(s, $@"^(?:{Regex.Escape(selfName)}|{Regex.Escape(first)})\s*,\s*(?=\S)", RegexOptions.IgnoreCase);
            if (self.Success && s.Length - self.Length >= 3)
                s = char.ToUpperInvariant(s[self.Length]) + s[(self.Length + 1)..];
            // "Lena's head is still pounding" from Lena: talking about yourself in third person is a model slip.
            var possessive = $@"(?<!\b(?:the|this|that|a|an|last|next|every|my|your|our)\s)\b(?:{Regex.Escape(selfName)}|{Regex.Escape(first)})['’]s\b(?!\s+(?:here|there|not|going|gonna|been|got|fine|right|ok|okay|back|done|just|still)\b)";
            s = Regex.Replace(s, $@"(^|[.!?]\s+){possessive}", "$1My");
            s = Regex.Replace(s, possessive, "my");
        }
        return StripStageDirections(s.Trim());
    }
}
