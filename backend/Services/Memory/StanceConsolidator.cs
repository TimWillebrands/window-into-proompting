using System.Globalization;
using System.Text.Json;
using PartyTown.Grains.Generation;

namespace PartyTown.Services.Memory;

/// <summary>
/// LLM side of Consolidation v1 (ADR 0016): one call per run that reads the Participant's
/// unconsolidated Recollections against their bio and current latest-wins Stances, and
/// proposes Stance appends where a belief has crystallised. Stateless and side-effect free —
/// <see cref="ConsolidationService"/> owns persistence and the watermark.
/// </summary>
public interface IStanceConsolidator
{
    /// <summary>
    /// Propose 0..N Stance appends for <paramref name="subject"/>. An empty list is a valid
    /// outcome ("slept on it, nothing crystallised") and still consumes the batch. Proposals
    /// come back fully resolved to write-shape: roster names mapped to persona ids, concept
    /// names normalised, valence clamped, reasoning capped.
    /// </summary>
    Task<IReadOnlyList<StanceProposal>> ProposeStancesAsync(
        ConsolidationSubject subject,
        IReadOnlyList<ConsolidationRosterEntry> roster,
        IReadOnlyList<UnconsolidatedRecollection> recollections,
        IReadOnlyList<StanceRecord> currentStances,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="IStanceConsolidator"/>
public sealed class StanceConsolidator(IGrainFactory grains, ILogger<StanceConsolidator> logger)
    : IStanceConsolidator
{
    // A run distills up to ~50 Recollections; more than a handful of belief shifts out of
    // one sleep is noise, not crystallisation.
    private const int MaxProposalsPerRun = 5;
    // Matches MemoryController's stance-reasoning cap so a proposal is never un-author-able.
    private const int MaxReasoningChars = 600;
    private const int MaxBioChars = 1500;

    public async Task<IReadOnlyList<StanceProposal>> ProposeStancesAsync(
        ConsolidationSubject subject,
        IReadOnlyList<ConsolidationRosterEntry> roster,
        IReadOnlyList<UnconsolidatedRecollection> recollections,
        IReadOnlyList<StanceRecord> currentStances,
        CancellationToken cancellationToken)
    {
        if (recollections.Count == 0)
        {
            return Array.Empty<StanceProposal>();
        }

        var (endpoint, complexity) = await RouteAsync(cancellationToken);

        var nameById = roster.ToDictionary(r => r.PersonaId, r => r.Name);
        var rosterBlock = roster.Count == 0
            ? "(nobody else)"
            : string.Join("\n", roster.Select(r => $"- {r.Name}"));

        var stanceBlock = currentStances.Count == 0
            ? "(none yet)"
            : string.Join("\n", currentStances.Select(s =>
                $"- [toward {StanceTargetLabel(s, subject, nameById)}, valence {s.Valence.ToString("+0.0#;-0.0#", CultureInfo.InvariantCulture)}] {s.Reasoning}"));

        var memoryBlock = string.Join("\n", recollections.Select(r =>
            $"- (weight {r.Weight.ToString("0.0#", CultureInfo.InvariantCulture)}) {r.Snippet}"));

        var system = $$"""
You are the overnight consolidation pass for a roleplay persona's memory: you decide which beliefs crystallised out of their recent experiences, the way sleep turns a day's moments into how someone feels about people and things.

Output STRICT JSON with this shape:
{
  "stances": [
    {"target_kind": "participant" | "concept" | "self", "target": "<name>", "valence": <number -1.0..1.0>, "reasoning": "<second person, max 40 words>"},
    ...
  ]
}

Rules:
- Propose a stance ONLY where the new memories genuinely support a belief forming or shifting. Most runs should propose 0-2 stances; an empty array is a perfectly good answer.
- Do NOT restate a current stance unless the new memories meaningfully change its valence or reasoning.
- target_kind "participant": target MUST be one of the roster names, verbatim. "concept": target is a short topic tag (one or two lowercase words). "self": a belief about themselves — target is ignored.
- valence: how they feel about the target. -1 hostile, 0 neutral, +1 warm.
- reasoning: second person, addressed to the persona, grounded in what they experienced ("Vlad exaggerates — you've watched him inflate three stories").
- At most {{MaxProposalsPerRun}} stances.

Output ONLY the JSON object. No prose, no code fences.
""";

        var bio = subject.Bio is { Length: > 0 } b
            ? b.Length > MaxBioChars ? b[..MaxBioChars] : b
            : "(no bio)";
        var user = $"""
Persona: {subject.Name}
Bio: {bio}

Others they know:
{rosterBlock}

Where they currently stand:
{stanceBlock}

New memories to sleep on (oldest first, weight = how much it stuck):
{memoryBlock}
""";

        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = user },
            },
            JobComplexity = complexity,
        };

        string raw;
        try
        {
            raw = await endpoint.CompleteOneShotAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "StanceConsolidator: proposal call failed for persona {PersonaId}", subject.PersonaId);
            throw;
        }

        var json = LlmJson.TryRepairObject(raw ?? "");
        if (json is null)
        {
            logger.LogWarning("StanceConsolidator: unparseable output for persona {PersonaId}: {Raw}", subject.PersonaId, raw);
            return Array.Empty<StanceProposal>();
        }

        return ParseProposals(json, subject, roster);
    }

    private IReadOnlyList<StanceProposal> ParseProposals(
        string json,
        ConsolidationSubject subject,
        IReadOnlyList<ConsolidationRosterEntry> roster)
    {
        // First name wins on a duplicate, case-insensitive — same drift tolerance as the
        // capture extractor's name-keyed output.
        var idByName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in roster)
        {
            idByName.TryAdd(entry.Name.Trim(), entry.PersonaId);
        }

        var proposals = new List<StanceProposal>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("stances", out var stances)
                || stances.ValueKind != JsonValueKind.Array)
            {
                return proposals;
            }

            foreach (var entry in stances.EnumerateArray())
            {
                if (proposals.Count >= MaxProposalsPerRun) break;
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var reasoning = entry.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String
                    ? (r.GetString() ?? "").Trim()
                    : "";
                if (reasoning.Length == 0) continue;
                if (reasoning.Length > MaxReasoningChars) reasoning = reasoning[..MaxReasoningChars];

                var valence = 0.0;
                if (entry.TryGetProperty("valence", out var v))
                {
                    valence = v.ValueKind switch
                    {
                        JsonValueKind.Number => v.GetDouble(),
                        JsonValueKind.String when double.TryParse(
                            v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
                        _ => 0.0,
                    };
                }
                valence = Math.Clamp(valence, -1.0, 1.0);

                var kind = entry.TryGetProperty("target_kind", out var k) && k.ValueKind == JsonValueKind.String
                    ? (k.GetString() ?? "").Trim().ToLowerInvariant()
                    : "";
                var target = entry.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.String
                    ? (t.GetString() ?? "").Trim()
                    : "";

                switch (kind)
                {
                    case "self":
                        proposals.Add(new StanceProposal(
                            StanceTargetKind.Self, null, null, null, valence, reasoning));
                        break;

                    case "participant":
                        // The persona naming themselves is a self-stance wearing the wrong
                        // kind; an unknown name is dropped — never mint Participants here.
                        if (target.Equals(subject.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            proposals.Add(new StanceProposal(
                                StanceTargetKind.Self, null, null, null, valence, reasoning));
                        }
                        else if (idByName.TryGetValue(target, out var personaId))
                        {
                            proposals.Add(new StanceProposal(
                                StanceTargetKind.Participant, personaId, null, null, valence, reasoning));
                        }
                        else
                        {
                            logger.LogDebug(
                                "StanceConsolidator: dropped proposal toward unknown participant {Target}", target);
                        }
                        break;

                    case "concept":
                        var name = MemoryExtractor.NormaliseConceptName(target);
                        if (name.Length == 0) continue;
                        proposals.Add(new StanceProposal(
                            StanceTargetKind.Concept, null, name, target, valence, reasoning));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "StanceConsolidator: proposal JSON parse failure on {Json}", json);
        }

        return proposals;
    }

    private static string StanceTargetLabel(
        StanceRecord s,
        ConsolidationSubject subject,
        IReadOnlyDictionary<Guid, string> nameById)
        => s.TargetKind switch
        {
            StanceTargetKind.Self => $"{subject.Name} (self)",
            StanceTargetKind.Concept => s.TargetConceptDisplay ?? s.TargetConceptName ?? "?",
            _ => s.TargetPersonaId is Guid tp && nameById.TryGetValue(tp, out var n)
                ? n
                : "someone no longer around",
        };

    /// <summary>
    /// Consolidation runs in rest, off the beat path — route it to the cheap tier like the
    /// capture extractors, falling back to General so it degrades in cost, never in function.
    /// </summary>
    private async Task<(ILlmEndpointGrain Endpoint, JobComplexity Complexity)> RouteAsync(
        CancellationToken cancellationToken)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        try
        {
            return (await router.RouteAsync(JobComplexity.CharacterThoughts, cancellationToken), JobComplexity.CharacterThoughts);
        }
        catch (InvalidOperationException)
        {
            logger.LogDebug("No cheap-tier provider for consolidation; falling back to General");
            return (await router.RouteAsync(JobComplexity.General, cancellationToken), JobComplexity.General);
        }
    }
}
