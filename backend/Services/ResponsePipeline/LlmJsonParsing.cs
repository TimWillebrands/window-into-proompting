using System.Text;
using System.Text.RegularExpressions;

namespace PartyTown.Services.ResponsePipeline;

internal static class LlmJsonParsing
{
    public static T? TryDeserialize<T>(string raw, System.Text.Json.JsonSerializerOptions options) where T : class
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var direct = Attempt<T>(raw, options);
        if (direct is not null) return direct;
        var cleaned = ExtractJsonPayload(raw);
        var afterCleanup = Attempt<T>(cleaned, options);
        if (afterCleanup is not null) return afterCleanup;
        try { return Attempt<T>(PartyTown.Services.Memory.SafeJsonRepair.Repair(cleaned), options); }
        catch { return null; }
    }

    private static T? Attempt<T>(string candidate, System.Text.Json.JsonSerializerOptions options) where T : class
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<T>(candidate, options); }
        catch { return null; }
    }

    public static string ExtractJsonPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        var s = raw.Trim();

        if (s.StartsWith("```"))
        {
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0)
                s = s[(firstNewline + 1)..];
            else
                s = s[3..];

            var closing = s.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0)
                s = s[..closing];

            s = s.Trim();
        }

        var firstBrace = s.IndexOf('{');
        var lastBrace = s.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            s = s[firstBrace..(lastBrace + 1)];

        s = CollapseStrayDoubleQuoteWrappers(s);

        return EscapeControlCharsInStrings(s);
    }

    private static readonly Regex StrayDoubleQuoteWrapperRegex = new(
        @":\s*""""(?<inner>(?:[^""]|""(?!""))*?)""""(?=\s*[,}\]])",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static string CollapseStrayDoubleQuoteWrappers(string json)
        => StrayDoubleQuoteWrapperRegex.Replace(
            json,
            m => $": \"{m.Groups["inner"].Value}\"");

    private static string EscapeControlCharsInStrings(string json)
    {
        var sb = new StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;

        foreach (var c in json)
        {
            if (inString)
            {
                if (escaped)
                {
                    sb.Append(c);
                    escaped = false;
                    continue;
                }

                switch (c)
                {
                    case '\\':
                        sb.Append(c);
                        escaped = true;
                        break;
                    case '"':
                        sb.Append(c);
                        inString = false;
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            else
            {
                sb.Append(c);
                if (c == '"')
                    inString = true;
            }
        }

        return sb.ToString();
    }
}
