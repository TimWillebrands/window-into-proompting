using System.Text.RegularExpressions;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>Strips model artefacts from a spoken line: chat-template tokens and a leading "Name:" self-label.</summary>
public static partial class SpeechCleanup
{
    [GeneratedRegex(@"<\|[^|>]{1,40}\|>")]
    private static partial Regex TemplateToken();

    public static string Clean(string? text, string? selfName)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        var s = TemplateToken().Replace(text, " ").Trim();
        // "private" survives as a bare word when the template was <|channel|>private<|message|>.
        if (s.StartsWith("private ", StringComparison.Ordinal) && text.Contains("<|channel|>")) s = s[8..].TrimStart();
        if (!string.IsNullOrWhiteSpace(selfName))
        {
            var prefix = new Regex($@"^\**{Regex.Escape(selfName)}\**\s*:\s*\**\s*", RegexOptions.IgnoreCase);
            s = prefix.Replace(s, string.Empty);
        }
        return s.Trim();
    }
}
