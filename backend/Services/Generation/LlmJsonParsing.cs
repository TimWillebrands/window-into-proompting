using System.Text;

namespace PartyTown.Services.Generation;

/// <summary>
/// Cleanup helpers for JSON produced by LLMs. Models routinely wrap structured output
/// in markdown code fences and emit raw control chars (LF/CR/TAB) inside string values
/// that the standard JSON parser rejects. Centralised here so any service running a
/// structured-output prompt can use the same pipeline.
/// </summary>
internal static class LlmJsonParsing
{
    /// <summary>
    /// Normalizes an LLM-produced JSON blob: strips markdown code fences, narrows to the
    /// first balanced object, and escapes raw control characters (LF/CR/TAB) found inside
    /// string values. Safe to hand to <c>JsonSerializer</c> or <c>JsonRepair</c>.
    /// </summary>
    public static string ExtractJsonPayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return raw;

        var s = raw.Trim();

        // Strip markdown code fences: ```json … ``` or ``` … ```.
        // Models very often wrap structured output this way despite being asked for JSON only.
        if (s.StartsWith("```"))
        {
            // Drop the opening fence line (```json, ```JSON, ```, etc.)
            var firstNewline = s.IndexOf('\n');
            if (firstNewline >= 0)
                s = s[(firstNewline + 1)..];
            else
                s = s[3..];

            // Drop the trailing closing fence, if any
            var closing = s.LastIndexOf("```", StringComparison.Ordinal);
            if (closing >= 0)
                s = s[..closing];

            s = s.Trim();
        }

        // Narrow to the first balanced JSON object. Models sometimes prefix commentary
        // (e.g. "Here is my decision:") or append stray text after the closing brace.
        var firstBrace = s.IndexOf('{');
        var lastBrace = s.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
            s = s[firstBrace..(lastBrace + 1)];

        return EscapeControlCharsInStrings(s);
    }

    /// <summary>
    /// Walks a JSON-ish string and replaces raw CR/LF/TAB characters that appear inside
    /// double-quoted string values with their escape sequences. JsonRepairSharp does not
    /// do this, yet LLMs frequently emit multi-line reason fields with literal newlines.
    /// </summary>
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
