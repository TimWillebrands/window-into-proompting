using System.Text;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Generation;

public sealed class GenerationSession(ILlmRouterGrain router, List<GenerationParticipant> allParticipants)
{
    /// <summary>
    /// Generates a response for a specific persona.
    /// </summary>
    public async Task<GenerationResult> GenerateResponseOnlyAsync(
        GenerationParticipant persona,
        IReadOnlyList<ChatMessage> history,
        Func<string, string, bool, Task> onEvent,
        CancellationToken cancellationToken,
        string? turnInstruction = null,
        string? scenario = null)
    {
        var others = allParticipants.Where(p => p.Id != persona.Id).ToList();
        var messages = new List<LlmChatMessage>
        {
            new()
            {
                Role = "system",
                Content = Instruction(persona.SystemPrompt ?? string.Empty, persona, others, scenario),
                Name = persona.Id.ToString()
            }
        };

        messages.AddRange(history.Select(message => new LlmChatMessage
        {
            Role = message.SenderId == persona.Id ? "assistant" : "user",
            Content = message.SenderId == persona.Id
                ? (message.Content ?? string.Empty)
                : ChatMessageRenderer.Render(
                    message,
                    allParticipants.FirstOrDefault(p => p.Id == message.SenderId)?.Name ?? "Unknown"),
            Name = message.SenderId.ToString()
        }));

        // Recency-position nudge from the decision step, if present.
        if (!string.IsNullOrWhiteSpace(turnInstruction))
        {
            messages.Add(new LlmChatMessage
            {
                Role = "system",
                Content = $"Guidance for this turn: {turnInstruction}",
                Name = persona.Id.ToString()
            });
        }

        var builder = new StringBuilder();
        var reasoning = new StringBuilder();
        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = JobComplexity.General
        };

        var generation = await router.RouteAsync(job.JobComplexity, cancellationToken);
        await foreach (var chunk in generation.GenerateAsync(job, cancellationToken))
        {
            if (chunk.Type == LlmGenerationEvent.ContentChunk)
            {
                builder.Append(chunk.Data);
            }
            else if (chunk.Type == LlmGenerationEvent.ReasoningChunk)
            {
                reasoning.Append(chunk.Data);
            }

            await onEvent(chunk.Type, chunk.Data, false);
        }

        await onEvent(MessageStreamEvent.GenerationComplete, "finished", true);

        return new GenerationResult
        {
            Stop = false,
            Message = builder.ToString(),
            Reasoning = reasoning.ToString(),
            Persona = persona
        };
    }

    private static string Instruction(string personaPrompt, GenerationParticipant self, List<GenerationParticipant> others, string? scenario)
    {
        var othersSection = others.Count == 0
            ? "(no other participants)"
            : string.Join("\n", others.Select(p =>
                p.IsUser
                    ? $"- {p.Name} (human)"
                    : $"- {p.Name}: {p.Bio ?? "No bio available"}"));

        // Scenario sits between identity and the participant roster: persona-self comes first
        // (primacy), the in-fiction setting establishes context, then who else is there.
        var scenarioSection = string.IsNullOrWhiteSpace(scenario)
            ? string.Empty
            : $"\n# Scenario\n{scenario.Trim()}\n";

        // Persona identity block claims the primacy position; chat-style rules land after,
        // so the model sees who it is before it sees generic etiquette.
        return $"""
# You are: {self.Name} (ID: {self.Id})
{personaPrompt}
{scenarioSection}
# Other participants
{othersSection}

# Style
You are in a (group) chat with other people. Stay completely in character as
your persona — never acknowledge that you are an AI or playing a role.

Talk like a real person in a casual group chat: short, reactive, and natural.
Actually respond to what others just said — agree, disagree, interrupt, joke,
get annoyed, ask follow-up questions. Don't monologue or lecture.

Keep your messages short — a few sentences at most, like a real chat message.
Only go longer if you're genuinely explaining something or telling a story.

You can use *italics* sparingly for brief actions or reactions (e.g. *sighs*,
*leans back*), but don't narrate elaborate scenes or stage directions.
""";
    }
}

public sealed record class GenerationParticipant
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? Bio { get; init; }
    public string? SystemPrompt { get; init; }
    public bool IsUser { get; init; }
}

public sealed record class GenerationResult
{
    public bool Stop { get; init; }
    public string? Message { get; init; }
    public string? Reasoning { get; init; }
    public GenerationParticipant? Persona { get; init; }
}
