using System.Text.Json;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.Memory;

public interface IMemoryExtractor
{
    Task<EventExtraction?> ExtractEventAsync(
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        IReadOnlyList<ParticipantView> presentParticipants,
        IReadOnlyList<string> existingConcepts,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, RecollectionDraft>> ExtractRecollectionsAsync(
        IReadOnlyList<RecollectionTarget> targets,
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken);
}

public sealed class MemoryExtractor(IGrainFactory grains, ILogger<MemoryExtractor> logger) : IMemoryExtractor
{
    private const int MaxContextMessages = 8;
    private const int MaxMessageChars = 500;
    private const int MaxSnippetChars = 500;
    private const double MinPersistedWeight = 0.05;
    private const int MaxDescriptionChars = 500;
    private const int MaxConceptsPerEvent = 8;
    internal const int MaxConceptNameChars = 64;
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
        var (endpoint, complexity) = await RouteExtractionAsync(cancellationToken);

        var participantRoster = string.Join("\n", presentParticipants.Select(p =>
            $"- {p.Name} (id: {p.Id})"));

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
            JobComplexity = complexity,
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
            logger.LogWarning("MemoryExtractor: extractor produced unparseable output: {Preview}", LlmJson.LogPreview(raw));
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
            logger.LogWarning(ex, "MemoryExtractor: JSON parse failure on {Preview}", LlmJson.LogPreview(json));
            return null;
        }
    }

    public async Task<IReadOnlyDictionary<Guid, RecollectionDraft>> ExtractRecollectionsAsync(
        IReadOnlyList<RecollectionTarget> targets,
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken)
    {
        var empty = (IReadOnlyDictionary<Guid, RecollectionDraft>)new Dictionary<Guid, RecollectionDraft>();
        if (targets.Count == 0)
        {
            return empty;
        }

        var (endpoint, complexity) = await RouteExtractionAsync(cancellationToken);

        var roster = string.Join("\n", targets.Select(t =>
            $"- {t.Name} [{(t.IsSpeaker ? "SPEAKER" : "OBSERVER")}]"));

        var system = $$"""
You summarize what each named person would actually remember from one moment in a group chat — one short memory per person, from THAT person's point of view.

Perspective rules (strict):
- In each memory, "you" is the person remembering it — no one else, ever.
- Refer to every other person by their NAME. Never write "I", "me", or "my": the memory is read back later by that person alone, and an "I" has no referent.
- Keep who-did-what exact. If Maya invited Tom to dinner, then Tom's memory is "Maya invited you to dinner" and Maya's is "You invited Tom to dinner" — never the reverse.
- Keep concrete specifics (names of places, projects, dates) verbatim — they are what makes the memory usable later.

You are given a roster. Each entry is tagged SPEAKER or OBSERVER:
- SPEAKER said the marked moment ("you said...", "you announced...", "you asked Tom...").
- OBSERVER witnessed it ("you heard Maya say...", "Maya told you...", "you watched Tom...").

Output STRICT JSON: an object mapping each person's exact name to either null OR an object:
  {"memory": "<SHORT sentence, max 25 words, addressing them as 'you'>", "weight": <number 0.0-1.0>}

weight answers: how much would this moment stick for THAT person, a day later?
- null (no object at all): throwaway/mundane for them (a "yeah", a "lol") — nothing worth remembering
- 0.1-0.3: minor color — they'd recall it only if prompted
- 0.4-0.6: notable — would resurface naturally when the topic comes up
- 0.7-0.9: significant — they'd bring it up on their own
- 1.0: flashbulb — unforgettable

Calibration: a friend announcing a major life change (quitting a career, moving away, a new venture, a birth) is 0.7-0.9 for everyone close to it — the listener too, not just the speaker. Plans made together (a meetup, a promise) are at least 0.4-0.6 for both sides. Weights should still differ between people: the same moment can be huge for one person and background noise for another.

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
            JobComplexity = complexity,
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
            logger.LogWarning("MemoryExtractor: recollection extractor produced unparseable output: {Preview}", LlmJson.LogPreview(raw));
            return empty;
        }

        var byName = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in targets)
        {
            byName.TryAdd(t.Name, t.PersonaId);
        }

        var result = new Dictionary<Guid, RecollectionDraft>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return empty;
            }

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!byName.TryGetValue(prop.Name.Trim(), out var personaId)) continue;
                if (result.ContainsKey(personaId)) continue;

                var draft = ParseDraft(prop.Value);
                if (draft is null) continue;

                result[personaId] = draft;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MemoryExtractor: recollection JSON parse failure on {Preview}", LlmJson.LogPreview(json));
            return empty;
        }

        return result;
    }

    private RecollectionDraft? ParseDraft(JsonElement value)
    {
        string? snippet;
        var weight = SalienceMath.DefaultWeight;

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                snippet = value.TryGetProperty("memory", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()
                    : null;
                if (value.TryGetProperty("weight", out var w))
                {
                    weight = w.ValueKind switch
                    {
                        JsonValueKind.Number => w.GetDouble(),
                        JsonValueKind.String when double.TryParse(
                            w.GetString(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
                        _ => SalienceMath.DefaultWeight,
                    };
                }
                break;
            case JsonValueKind.String:
                snippet = value.GetString();
                break;
            default:
                return null;
        }

        snippet = (snippet ?? "").Trim().Trim('"').Trim();
        if (string.IsNullOrWhiteSpace(snippet) ||
            snippet.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
            snippet.Equals("(none)", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        weight = Math.Clamp(weight, 0.0, 1.0);
        if (weight < MinPersistedWeight)
        {
            return null;
        }

        return new RecollectionDraft(Truncate(snippet, MaxSnippetChars), weight);
    }

    private Task<(ILlmEndpointGrain Endpoint, JobComplexity Served)> RouteExtractionAsync(CancellationToken cancellationToken)
        => new LlmCall(grains.GetGrain<ILlmRouterGrain>(0), logger).RouteAsync(LlmTiers.Thoughts, cancellationToken);

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

    private static string? TryRepairJson(string raw) => LlmJson.TryRepairObject(raw);
}
