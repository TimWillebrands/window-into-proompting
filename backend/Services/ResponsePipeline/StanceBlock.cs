using PartyTown.Services.Memory;

namespace PartyTown.Services.ResponsePipeline;

public static class StanceBlock
{
    public static string Render(IReadOnlyList<string>? stanceLines)
    {
        if (stanceLines is null || stanceLines.Count == 0)
        {
            return string.Empty;
        }

        var lines = string.Join("\n", stanceLines
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => $"- {s.Trim()}"));
        if (string.IsNullOrEmpty(lines))
        {
            return string.Empty;
        }
        return $"\n# Where you stand\n{lines}\n";
    }

    public static string FormatLine(StanceLine line)
        => line.Contrast is { Length: > 0 } past
            ? $"{line.Reasoning} — though you used to feel otherwise: {past}"
            : line.Reasoning;
}
