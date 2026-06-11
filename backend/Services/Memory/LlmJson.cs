using System.Text.Json;
using JsonRepairSharp;

namespace PartyTown.Services.Memory;

/// <summary>
/// Shared tolerance layer for the rest-path extractors (capture, consolidation): clip the
/// model's output to its outermost JSON object and repair near-JSON drift. Null when no
/// object can be salvaged — callers treat that as a declined extraction.
/// </summary>
internal static class LlmJson
{
    /// <summary>
    /// Bounded diagnostic for parse-failure warnings. Model payloads are built from
    /// recollection snippets, bios and user free-text — dumping them whole turns that
    /// content into plain-text logs. A short prefix plus the total length keeps the
    /// failure shape debuggable without persisting the payload.
    /// </summary>
    internal static string LogPreview(string? raw)
    {
        const int max = 160;
        if (string.IsNullOrEmpty(raw)) return "(empty)";
        return raw.Length <= max ? raw : $"{raw[..max]}… [{raw.Length} chars total]";
    }

    internal static string? TryRepairObject(string raw)
    {
        var trimmed = raw.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var candidate = trimmed.Substring(start, end - start + 1);

        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return candidate;
        }
        catch
        {
            try
            {
                return JsonRepair.RepairJson(candidate);
            }
            catch
            {
                return null;
            }
        }
    }
}
