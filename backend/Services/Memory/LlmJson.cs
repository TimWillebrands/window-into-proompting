using System.Text.Json;
using JsonRepairSharp;

namespace PartyTown.Services.Memory;

internal static class LlmJson
{
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
                return PartyTown.Services.Memory.SafeJsonRepair.Repair(candidate);
            }
            catch
            {
                return null;
            }
        }
    }
}
