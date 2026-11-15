using System.Diagnostics;
using System.Text;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

public sealed class PersonaEmoteService(ILlmRouterGrain router, ILogger logger)
{
    public const string DecisionPhaseFallback = "*starts to speak, then doesn't*";

    public const string GenerationFailureFallback = "*falls silent*";

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

        string content;
        try
        {
            content = (await new LlmCall(router, logger, self.Id).StreamAsync(job, LlmTiers.ThoughtsOnly, ct, span: span)).Content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Persona {PersonaName} emote call failed — using fallback", self.Name);
            span?.SetTag("emote.fallback", ex is InvalidOperationException ? "routing" : "generation");
            return GenerationFailureFallback;
        }

        var raw = Sanitize(content);
        if (string.IsNullOrWhiteSpace(raw))
        {
            span?.SetTag("emote.fallback", "empty");
            return GenerationFailureFallback;
        }

        span?.SetTag("emote.length", raw.Length);
        return raw;
    }

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

        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1].Trim();

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
