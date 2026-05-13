using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.Memory;

public sealed class MemoryExtractor(IGrainFactory grains, ILogger<MemoryExtractor> logger)
{
    private const int MaxContextMessages = 8;
    private const int MaxMessageChars = 500;
    private const int MaxSnippetChars = 500;

    public async Task<string> ExtractForPersonaAsync(
        string personaName,
        ChatMessage sourceMessage,
        string sourceAuthorName,
        IReadOnlyList<ChatMessage> recentContext,
        Func<Guid, string> resolveAuthorName,
        CancellationToken cancellationToken)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var endpoint = await router.RouteAsync(JobComplexity.General, cancellationToken);

        var system = $$"""
You are summarizing what {{personaName}} would actually remember from this moment in a chat conversation.

Output a SHORT sentence from {{personaName}}'s point of view, in second person addressing them ("you saw...", "you heard...", "you watched..."). Max 25 words.

Capture only what's notable enough to remember a day later. If nothing notable happened or the moment is mundane, output the single word: NONE

Do not add commentary, do not address the user, do not narrate. Output just the snippet text or NONE.
""";

        var contextLines = recentContext
            .TakeLast(MaxContextMessages)
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
            logger.LogWarning(ex, "MemoryExtractor failed for persona {Persona}", personaName);
            return "";
        }
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max];
}
