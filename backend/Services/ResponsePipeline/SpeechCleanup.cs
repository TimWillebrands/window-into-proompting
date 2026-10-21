using System.Text.RegularExpressions;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>Strips model artefacts from a spoken line: chat-template tokens, a leading "Name:" self-label, narrated stage directions.</summary>
public static partial class SpeechCleanup
{
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
        var s = TemplateToken().Replace(text, " ").Trim();
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
