using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JsonRepairSharp;
using PartyTown.Grains.Generation;

namespace PartyTown.Services.Import;

/// <summary>What one finalize call produced: the folded card (compressed traits) plus the
/// one-line Bio the decision phase composes on. Degraded = the LLM output was unusable
/// and the card fell back to the verbatim trait fold (no Bio) — still reviewable.</summary>
public sealed record FinalizedCard(string Persona, string SystemPrompt, string? Bio, bool Degraded);

/// <summary>
/// The one LLM step of the commit path (ADR 0017), run pre-commit: compress/dedup one
/// persona's extracted trait list and synthesise a one-line Bio. Output lands in the
/// draft as a reviewable card — commit executes the reviewed card, never this call.
/// Stateless; same traits, rerun freely.
/// </summary>
public interface IImportPersonaFinalizeService
{
    Task<FinalizedCard> FinalizeAsync(string personaName, IReadOnlyList<string> traits, CancellationToken ct);
}

/// <inheritdoc cref="IImportPersonaFinalizeService"/>
public sealed class ImportPersonaFinalizeService(
    IGrainFactory grains, ILogger<ImportPersonaFinalizeService> logger) : IImportPersonaFinalizeService
{
    private const int Attempts = 3;
    private const int MaxTraits = 12;

    // A verbatim 24-trait list reads as a fact-sheet the speaking model recites, and the
    // decision phase composes on Bio alone — so compression + a synthesized Bio are
    // importer requirements, not niceties (ADR 0017).
    private const string SystemPrompt =
        """
        You compress an imported character's raw extracted trait list into a playable
        persona card.

        The input traits come from an automated extractor: expect duplicates,
        near-duplicates, and overlapping phrasings of the same fact.

        Return STRICT JSON:
        - traits: the deduplicated, compressed trait list — at most 12 entries. Merge
          near-duplicates into one. Keep the concrete, character-defining facts
          (personality, occupation, standing relationships, speech style); fold minor
          variations into the entry they belong to. Each entry is one plain sentence of
          at most 160 characters, written in second person ("You ..."), because these
          lines become the character's system prompt. Order from most to least defining.
        - bio: ONE line, at most 140 characters, third person — who this character is at
          a glance. No lists, no markup, no trailing period needed.

        Every claim must be traceable to the input traits. Do NOT invent, do NOT pad.
        FEWER, sharper traits beat exhaustive coverage.
        """;

    public async Task<FinalizedCard> FinalizeAsync(
        string personaName, IReadOnlyList<string> traits, CancellationToken ct)
    {
        var user = new StringBuilder()
            .AppendLine($"CHARACTER: {personaName}")
            .AppendLine()
            .AppendLine("EXTRACTED TRAITS:")
            .AppendLine(string.Join("\n", traits.Select(t => "- " + t.Trim())))
            .ToString();

        var raw = await CompleteAsync(personaName, user, ct);
        var parsed = TryParse(raw);
        var compressed = (parsed?.Traits ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Take(MaxTraits)
            .ToList();
        var bio = string.IsNullOrWhiteSpace(parsed?.Bio) ? null : parsed.Bio.Trim();

        if (compressed.Count == 0)
        {
            logger.LogWarning(
                "Persona finalize for '{Persona}' degraded to the verbatim trait fold ({Traits} traits)",
                personaName, traits.Count);
            return new FinalizedCard(personaName, FoldCard(personaName, traits), bio, Degraded: true);
        }
        return new FinalizedCard(personaName, FoldCard(personaName, compressed), bio, Degraded: false);
    }

    /// <summary>Card shape: a "You are …" header plus one bullet per trait.</summary>
    internal static string FoldCard(string name, IReadOnlyList<string> traits)
        => new StringBuilder()
            .Append("You are ").Append(name).AppendLine(".")
            .AppendLine()
            .AppendLine(string.Join("\n", traits.Select(t => "- " + t.Trim())))
            .ToString().Trim();

    // ── the actual call: route General, one-shot, retry transient failures ───────

    private async Task<string> CompleteAsync(string personaName, string user, CancellationToken ct)
    {
        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = SystemPrompt },
                new() { Role = "user", Content = user },
            },
            JobComplexity = JobComplexity.General,
            ResponseFormat = CardSchema.ToJsonString(),
        };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var router = grains.GetGrain<ILlmRouterGrain>(0);
                var endpoint = await router.RouteAsync(JobComplexity.General, ct);
                var raw = (await endpoint.CompleteOneShotAsync(job, ct)).Trim();
                if (raw.Length == 0 && attempt < Attempts)
                {
                    logger.LogWarning("Persona finalize '{Persona}': empty content, attempt {Attempt}", personaName, attempt);
                    continue;
                }
                return raw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < Attempts)
            {
                logger.LogWarning(ex, "Persona finalize '{Persona}' failed, attempt {Attempt}", personaName, attempt);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Persona finalize '{Persona}' failed after {Attempts} attempts", personaName, Attempts);
                return string.Empty;
            }
        }
    }

    private static readonly JsonObject CardSchema = new()
    {
        ["type"] = "json_schema",
        ["json_schema"] = new JsonObject
        {
            ["name"] = "persona_card",
            ["strict"] = true,
            ["schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["traits"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = MaxTraits,
                        ["items"] = new JsonObject { ["type"] = "string" },
                    },
                    ["bio"] = new JsonObject { ["type"] = "string" },
                },
                ["required"] = new JsonArray("traits", "bio"),
            },
        },
    };

    private sealed record CardOutput(
        [property: JsonPropertyName("traits")] List<string>? Traits,
        [property: JsonPropertyName("bio")] string? Bio);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static CardOutput? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var candidate in new[] { raw, StripFences(raw), TryRepair(StripFences(raw)) })
        {
            if (candidate is null) continue;
            try { return JsonSerializer.Deserialize<CardOutput>(candidate, WebOptions); }
            catch { /* next candidate */ }
        }
        return null;
    }

    private static string? TryRepair(string s)
    {
        try { return JsonRepair.RepairJson(s, JsonRepair.InputType.LLM); }
        catch { return null; }
    }

    private static string StripFences(string raw)
    {
        var s = raw.Trim();
        if (!s.StartsWith("```")) return s;
        var firstNewline = s.IndexOf('\n');
        s = firstNewline >= 0 ? s[(firstNewline + 1)..] : s[3..];
        var closing = s.LastIndexOf("```", StringComparison.Ordinal);
        return (closing >= 0 ? s[..closing] : s).Trim();
    }
}
