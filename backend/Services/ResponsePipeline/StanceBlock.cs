namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Renders the ambient <c># Where you stand</c> block (ADR 0016) shared by the Decision and
/// Speaking prompts. A Stance is identity-adjacent content — an orientation the persona
/// speaks *from*, not a callback to weave in — so unlike Recollection it carries no selection
/// mechanic: both phases see the same lines verbatim. The lines are already anchor-scoped,
/// latest-wins, and capped by <see cref="Memory.IMemoryRepository.RecallStancesAsync"/>;
/// this helper only formats. Empty input renders nothing so the block never reads as a void.
/// </summary>
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
}
