using System.Diagnostics;
using System.Text;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Generates a short in-character "abandonment emote" for a generation that the
/// stop-signal race elected to cancel. Replaces the bare red "cancelled" error
/// with a single italic action line (e.g. "*Vlad sets the cup down, decides against it*"),
/// shipped into the chat history with <c>ChatMessage.Kind = "emote"</c> so other
/// personas can react to it and the frontend can render it distinctly.
///
/// Routes via <see cref="JobComplexity.CharacterThoughts"/> — same cheap/fast model
/// contract as <see cref="PersonaSalienceService"/>. Failure mode is "literal fallback":
/// on any routing or generation error, returns a generic <c>"*falls silent*"</c> rather
/// than throwing — the cancel path must always produce a renderable line.
/// </summary>
public sealed class PersonaEmoteService(ILlmRouterGrain router, ILogger logger)
{
    /// <summary>Used when no draft exists (decision-phase cancel) — saves an LFM call
    /// when there's nothing to summarize.</summary>
    public const string DecisionPhaseFallback = "*starts to speak, then doesn't*";

    /// <summary>Used when the LFM call fails or returns garbage — never empty.</summary>
    public const string GenerationFailureFallback = "*falls silent*";

    /// <summary>Hard cap on emote length. Models occasionally ignore the "one short
    /// action" instruction and produce paragraphs; trim aggressively.</summary>
    private const int MaxEmoteChars = 160;

    public async Task<string> GenerateAbandonmentEmoteAsync(
        SelfView self,
        string partialDraft,
        string interruptingMessage,
        string interruptingSenderName,
        CancellationToken ct)
    {
        using var span = Tracing.Persona.StartActivity("persona.emote", ActivityKind.Internal);
        span?.SetTag("persona.id", self.Id);
        span?.SetTag("persona.name", self.Name);
        span?.SetTag("emote.draft_chars", partialDraft?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(partialDraft))
        {
            span?.SetTag("emote.fallback", "no-draft");
            return DecisionPhaseFallback;
        }

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = SystemPrompt(self.Name) },
            new() { Role = "user", Content = UserPrompt(
                self.Name, partialDraft, interruptingMessage, interruptingSenderName) }
        };

        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = JobComplexity.CharacterThoughts
        };

        ILlmEndpointGrain? generation;
        try
        {
            generation = await router.RouteAsync(job.JobComplexity, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Persona {PersonaName} emote routing failed — using fallback", self.Name);
            span?.SetTag("emote.fallback", "routing");
            return GenerationFailureFallback;
        }

        try
        {
            var attribution = await generation.GetAttributionAsync();
            span?.SetTag("llm.provider", attribution?.Provider);
            span?.SetTag("llm.model", attribution?.ModelName);
        }
        catch
        {
            // Best-effort instrumentation.
        }

        var text = new StringBuilder();
        try
        {
            await foreach (var chunk in generation.GenerateAsync(job, ct))
            {
                if (chunk.Type == LlmGenerationEvent.ContentChunk)
                    text.Append(chunk.Data);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Persona {PersonaName} emote generation failed — using fallback", self.Name);
            span?.SetTag("emote.fallback", "generation");
            return GenerationFailureFallback;
        }

        var raw = Sanitize(text.ToString());
        if (string.IsNullOrWhiteSpace(raw))
        {
            span?.SetTag("emote.fallback", "empty");
            return GenerationFailureFallback;
        }

        span?.SetTag("emote.length", raw.Length);
        return raw;
    }

    /// <summary>Strip code fences, quotes, leading/trailing whitespace, and clamp length.
    /// Does NOT enforce asterisk wrapping — the LLM is asked for it, and the frontend can
    /// fall back to italic rendering even if the asterisks are missing.</summary>
    private static string Sanitize(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("```"))
        {
            var nl = s.IndexOf('\n');
            if (nl >= 0) s = s[(nl + 1)..];
            var close = s.LastIndexOf("```", StringComparison.Ordinal);
            if (close >= 0) s = s[..close];
            s = s.Trim();
        }

        // Models sometimes wrap output in quotes despite "no quotes" instruction.
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1].Trim();

        // Collapse multi-line emotes into a single line — they're meant to be one beat.
        s = s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

        if (s.Length > MaxEmoteChars)
            s = s[..MaxEmoteChars].TrimEnd() + "…";

        return s;
    }

    private static string SystemPrompt(string personaName) => $$"""
You are {{personaName}}. You were mid-utterance in a group chat when someone else
spoke and made you reconsider. You pull back without finishing.

Output ONE short italic action describing this — wrap it in asterisks, no quotes,
no preamble, no explanation. Examples:
- *clears throat, decides against it*
- *closes mouth mid-word*
- *waves it off — never mind*
- *trails off, listening instead*

One line. Under 80 characters. The action only.
""";

    private static string UserPrompt(
        string personaName,
        string partialDraft,
        string interruptingMessage,
        string interruptingSenderName) => $"""
You ({personaName}) had started writing:
"{Trim(partialDraft)}"

Then {interruptingSenderName} said:
"{Trim(interruptingMessage)}"

You decide not to finish. One short italic action describing the pull-back.
""";

    private static string Trim(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(empty)" : s.Trim();
}
