using System.Text.Json;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// LLM-side of moment capture: one call extracts a neutral Event description and the
/// Concept / Participant tags this Event is *about*; a second batched call extracts every
/// present Participant's Recollection snippet at once. Stateless and side-effect free —
/// the repository owns persistence.
/// </summary>
public interface IMemoryExtractor
{
    /// <param name="existingConcepts">
    /// Display labels of Concepts already in the graph. Passed so the extractor reuses an
    /// existing tag when one fits instead of minting a near-duplicate ("lisp" vs
    /// "common lisp"). May be empty.
    /// </param>
    Task<EventExtraction?> ExtractEventAsync(
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        IReadOnlyList<ParticipantView> presentParticipants,
        IReadOnlyList<string> existingConcepts,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Extract one Recollection snippet per target in a single LLM call. Returns a map keyed
    /// by <see cref="RecollectionTarget.PersonaId"/>; a target the model declined (NONE /
    /// missing / unparseable) is simply absent from the map. Empty input → empty map, no call.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> ExtractRecollectionsAsync(
        IReadOnlyList<RecollectionTarget> targets,
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="IMemoryExtractor"/>
public sealed class MemoryExtractor(IGrainFactory grains, ILogger<MemoryExtractor> logger) : IMemoryExtractor
{
    private const int MaxContextMessages = 8;
    private const int MaxMessageChars = 500;
    private const int MaxSnippetChars = 500;
    private const int MaxDescriptionChars = 500;
    private const int MaxConceptsPerEvent = 8;
    private const int MaxConceptNameChars = 64;
    // Cap how many existing tags we paste into the event-extraction prompt. Concept count
    // is small today; once it grows past this, switch the repository-side fetch to a
    // prefix/fuzzy match instead of "all names" (see ADR 0014).
    private const int MaxExistingConceptsInPrompt = 60;

    public async Task<EventExtraction?> ExtractEventAsync(
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        IReadOnlyList<ParticipantView> presentParticipants,
        IReadOnlyList<string> existingConcepts,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var endpoint = await router.RouteAsync(JobComplexity.General, cancellationToken);

        var participantRoster = string.Join("\n", presentParticipants.Select(p =>
            $"- {p.Name} (id: {p.Id})"));

        // Match-or-mint: hand the model the existing Concept vocabulary so it reuses a tag
        // when one fits rather than fragmenting reality ("lisp" / "common lisp" / "lisp
        // programming"). NormaliseConceptName stays the dedup backstop on the write side.
        var conceptGuidance = existingConcepts.Count == 0
            ? $"- concepts: 0..{MaxConceptsPerEvent} short topic tags (one or two lowercase words). Subjects discussed or invoked. NOT names of people. Empty array if none."
            : $"- concepts: 0..{MaxConceptsPerEvent} short topic tags (one or two lowercase words). Subjects discussed or invoked. NOT names of people. REUSE one of the existing tags below verbatim when it fits the subject; only mint a new tag when none fit. Empty array if none.\nExisting tags: {string.Join(", ", existingConcepts.Take(MaxExistingConceptsInPrompt))}";

        var system = $$"""
You distill a single moment in a group chat into a neutral, factual record of what happened.

Output STRICT JSON with this shape:
{
  "description": "<one-sentence third-person summary of what happened, max 30 words>",
  "concepts": ["<short topic tag>", ...],
  "participant_ids": ["<uuid>", ...]
}

Rules:
- description: third-person, factual, neutral. No interpretation, no feelings, no commentary. If nothing notable happened, set to "".
{{conceptGuidance}}
- participant_ids: subset of the roster ids whose actions or words this moment is centrally about. Empty array if none.

Output ONLY the JSON object. No prose, no code fences.
""";

        var contextLines = recentContext
            .TakeLast(MaxContextMessages)
            .Where(m => m.MessageId != sourceMessage.MessageId)
            .Select(m =>
            {
                var author = resolveAuthorName(m.SenderId);
                var content = Truncate(m.Content ?? "", MaxMessageChars);
                return $"{author}: {content}";
            });

        var sourceText = Truncate(sourceMessage.Content ?? "", MaxMessageChars);
        var user = $"""
Participants present:
{participantRoster}

Recent conversation:
{string.Join("\n", contextLines)}

The marked moment:
{sourceAuthorName}: {sourceText}
""";

        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = user },
            },
            JobComplexity = JobComplexity.General,
        };

        string raw;
        try
        {
            raw = await endpoint.CompleteOneShotAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MemoryExtractor.ExtractEventAsync failed");
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var json = TryRepairJson(raw);
        if (json is null)
        {
            logger.LogWarning("MemoryExtractor: extractor produced unparseable output: {Raw}", raw);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var description = Truncate(root.TryGetProperty("description", out var d) ? (d.GetString() ?? "") : "", MaxDescriptionChars).Trim();
            if (string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var concepts = new List<ConceptTag>();
            if (root.TryGetProperty("concepts", out var c) && c.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in c.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String) continue;
                    var display = (entry.GetString() ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(display)) continue;
                    var name = NormaliseConceptName(display);
                    if (name.Length == 0) continue;
                    concepts.Add(new ConceptTag(name, Truncate(display, MaxConceptNameChars)));
                    if (concepts.Count >= MaxConceptsPerEvent) break;
                }
            }

            var dedupedConcepts = concepts
                .GroupBy(x => x.Name)
                .Select(g => g.First())
                .ToList();

            var participantIds = new List<Guid>();
            if (root.TryGetProperty("participant_ids", out var pids) && pids.ValueKind == JsonValueKind.Array)
            {
                var presentIds = presentParticipants.Select(p => p.Id).ToHashSet();
                foreach (var entry in pids.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.String) continue;
                    if (Guid.TryParse(entry.GetString(), out var g) && presentIds.Contains(g))
                    {
                        participantIds.Add(g);
                    }
                }
            }

            return new EventExtraction(description, dedupedConcepts, participantIds.Distinct().ToList());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MemoryExtractor: JSON parse failure on {Json}", json);
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, string>> ExtractRecollectionsAsync(
        IReadOnlyList<RecollectionTarget> targets,
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken)
    {
        var empty = (IReadOnlyDictionary<Guid, string>)new Dictionary<Guid, string>();
        if (targets.Count == 0)
        {
            return empty;
        }

        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var endpoint = await router.RouteAsync(JobComplexity.General, cancellationToken);

        // One call covers every Participant. The model keys its output by name; we map back
        // to PersonaId via the target list. Observer-verb framing ("you saw / heard") doesn't
        // fit the Participant who SPOKE the marked moment — the model returns NONE because
        // nothing was "observed". So each target is tagged SPEAKER / OBSERVER inline and the
        // prompt asks for first-person ("you said…") framing for the speaker.
        var roster = string.Join("\n", targets.Select(t =>
            $"- {t.Name} [{(t.IsSpeaker ? "SPEAKER" : "OBSERVER")}]"));

        var system = $$"""
You summarize what each named person would actually remember from one moment in a group chat — one short memory per person, from THAT person's point of view.

You are given a roster. Each entry is tagged SPEAKER or OBSERVER:
- SPEAKER said the marked moment. Frame their memory in first-person-as-them, second person ("you said...", "you admitted...", "you brought up...") — capture what they meant or were aiming at.
- OBSERVER witnessed it. Frame their memory as ("you saw...", "you heard...", "you watched...").

Output STRICT JSON: an object mapping each person's exact name to either a SHORT sentence (max 25 words, addressing them as "you") OR null.
Use null when the moment is throwaway/mundane for that person (a "yeah", a "lol") and not worth remembering a day later.

Output ONLY the JSON object. No prose, no code fences. Include every name from the roster as a key.
""";

        var contextLines = recentContext
            .TakeLast(MaxContextMessages)
            .Where(m => m.MessageId != sourceMessage.MessageId)
            .Select(m =>
            {
                var author = resolveAuthorName(m.SenderId);
                var content = Truncate(m.Content ?? "", MaxMessageChars);
                return $"{author}: {content}";
            });

        var sourceText = Truncate(sourceMessage.Content ?? "", MaxMessageChars);
        var user = $"""
People (remember one memory for each):
{roster}

Recent conversation:
{string.Join("\n", contextLines)}

The marked moment:
{sourceAuthorName}: {sourceText}
""";

        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = user },
            },
            JobComplexity = JobComplexity.General,
        };

        string raw;
        try
        {
            raw = await endpoint.CompleteOneShotAsync(job, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MemoryExtractor.ExtractRecollectionsAsync failed for {Count} targets", targets.Count);
            return empty;
        }

        var json = TryRepairJson(raw ?? "");
        if (json is null)
        {
            logger.LogWarning("MemoryExtractor: recollection extractor produced unparseable output: {Raw}", raw);
            return empty;
        }

        // First name wins on a duplicate — matches the per-persona behaviour where each name
        // got its own call. Case-insensitive so the model's casing drift doesn't drop a hit.
        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targets)
        {
            byName.TryAdd(t.Name, t.PersonaId);
        }

        var result = new Dictionary<Guid, string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return empty;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String) continue;
                if (!byName.TryGetValue(prop.Name.Trim(), out var personaId)) continue;
                if (result.ContainsKey(personaId)) continue;

                var snippet = (prop.Value.GetString() ?? "").Trim().Trim('"').Trim();
                if (string.IsNullOrWhiteSpace(snippet) ||
                    snippet.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
                    snippet.Equals("(none)", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result[personaId] = Truncate(snippet, MaxSnippetChars);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MemoryExtractor: recollection JSON parse failure on {Json}", json);
            return empty;
        }

        return result;
    }

    internal static string NormaliseConceptName(string raw)
    {
        var trimmed = raw.Trim().ToLowerInvariant();
        if (trimmed.Length > MaxConceptNameChars)
        {
            trimmed = trimmed[..MaxConceptNameChars];
        }
        return trimmed;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max];

    private static string? TryRepairJson(string raw)
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
