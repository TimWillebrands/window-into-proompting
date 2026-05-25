using System.Text.Json;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// LLM-side of moment capture: one call extracts a neutral Event description and the
/// Concept / Participant tags this Event is *about*; per-Persona Recollection snippets
/// are extracted by repeated calls. Stateless and side-effect free — the repository
/// owns persistence.
/// </summary>
public interface IMemoryExtractor
{
    Task<EventExtraction?> ExtractEventAsync(
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        IReadOnlyList<ParticipantView> presentParticipants,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken);

    Task<string> ExtractRecollectionAsync(
        string personaName,
        bool isSpeaker,
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

    public async Task<EventExtraction?> ExtractEventAsync(
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        IReadOnlyList<ParticipantView> presentParticipants,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var endpoint = await router.RouteAsync(JobComplexity.General, cancellationToken);

        var participantRoster = string.Join("\n", presentParticipants.Select(p =>
            $"- {p.Name} (id: {p.Id})"));

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
- concepts: 0..{{MaxConceptsPerEvent}} short topic tags (one or two lowercase words). Subjects discussed or invoked. NOT names of people. Empty array if none.
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

    public async Task<string> ExtractRecollectionAsync(
        string personaName,
        bool isSpeaker,
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var endpoint = await router.RouteAsync(JobComplexity.General, cancellationToken);

        // Observer-verb prompt (you saw / heard / watched) doesn't fit when {personaName} IS
        // the speaker of the marked moment — the LLM tends to return NONE because nothing
        // was "observed". For the speaker we ask for their own first-person framing instead,
        // preserving how *they* would remember saying it.
        var system = isSpeaker
            ? $$"""
You are summarizing what {{personaName}} would actually remember about something THEY just said in a chat conversation.

Output a SHORT sentence from {{personaName}}'s point of view, in second person addressing them ("you said...", "you admitted...", "you brought up..."). Capture their own interpretation — what they meant, what they were aiming at, how they framed it. Max 25 words.

If the moment was throwaway/mundane (a "yeah", a "lol") and not worth remembering a day later, output the single word: NONE

Do not add commentary, do not address the user, do not narrate. Output just the snippet text or NONE.
"""
            : $$"""
You are summarizing what {{personaName}} would actually remember from this moment in a chat conversation.

Output a SHORT sentence from {{personaName}}'s point of view, in second person addressing them ("you saw...", "you heard...", "you watched..."). Max 25 words.

Capture only what's notable enough to remember a day later. If nothing notable happened or the moment is mundane, output the single word: NONE

Do not add commentary, do not address the user, do not narrate. Output just the snippet text or NONE.
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
Recent conversation:
{string.Join("\n", contextLines)}

The marked moment {personaName} should remember:
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

        try
        {
            var raw = await endpoint.CompleteOneShotAsync(job, cancellationToken);
            var snippet = (raw ?? "").Trim().Trim('"').Trim();

            if (string.IsNullOrWhiteSpace(snippet) ||
                snippet.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
                snippet.Equals("(none)", StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return Truncate(snippet, MaxSnippetChars);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MemoryExtractor.ExtractRecollectionAsync failed for persona {Persona}", personaName);
            return "";
        }
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
