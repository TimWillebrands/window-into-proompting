using System.Diagnostics;
using System.Text;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.ResponsePipeline;

public sealed class SpeakingSession(ILlmRouterGrain router, IReadOnlyList<ParticipantView> allParticipants)
{
    /// <summary>
    /// Generates a response for a specific persona.
    /// <paramref name="memoryToReference"/> is the single recollection the decision phase
    /// picked to carry into this beat (or null). Rendered at the recency position of the
    /// system prompt so it's the last thing the model sees before drafting — the contract
    /// is "decision selects, speaking executes".
    /// </summary>
    public async Task<SpeakingResult> GenerateResponseOnlyAsync(
        SelfView persona,
        IReadOnlyList<ChatMessage> history,
        Func<string, string, bool, Task> onEvent,
        CancellationToken cancellationToken,
        string? turnInstruction = null,
        string? scenario = null,
        string? memoryToReference = null)
    {
        var others = allParticipants.Where(p => p.Id != persona.Id).ToList();
        // Build sender-name lookup once. ParticipantView is a struct, so
        // FirstOrDefault would return default(struct) on miss (Name = null,
        // typed non-nullable) — robust dictionary lookup avoids that footgun.
        var nameById = allParticipants.ToDictionary(p => p.Id, p => p.Name);
        var messages = new List<LlmChatMessage>
        {
            new()
            {
                Role = "system",
                Content = Instruction(persona.SystemPrompt ?? string.Empty, persona, others, scenario, memoryToReference),
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
                    nameById.TryGetValue(message.SenderId, out var n) ? n : "Unknown"),
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

        using var generateSpan = Tracing.Persona.StartActivity("persona.generate", ActivityKind.Internal);
        generateSpan?.SetTag("persona.id", persona.Id);
        generateSpan?.SetTag("persona.name", persona.Name);

        var generation = await router.RouteAsync(job.JobComplexity, cancellationToken);
        var metadata = await generation.GetAttributionAsync();
        generateSpan?.SetTag("llm.provider", metadata?.Provider);
        generateSpan?.SetTag("llm.model", metadata?.ModelName);

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

        generateSpan?.SetTag("output.length", builder.Length);
        await onEvent(MessageStreamEvent.GenerationComplete, "finished", true);

        return new SpeakingResult
        {
            Stop = false,
            Message = builder.ToString(),
            Reasoning = reasoning.ToString(),
            Metadata = metadata
        };
    }

    private static string Instruction(string personaPrompt, SelfView self, List<ParticipantView> others, string? scenario, string? memoryToReference)
    {
        // Names only. Bios used to live here but leaked theme/style across personas:
        // Hana's "shrine"/"sacred" vocabulary primed Vlad to emit 🌸, address Hana before
        // she'd spoken, and adopt mystic register. Roster gives identity, not character.
        var othersSection = others.Count == 0
            ? "(no other participants)"
            : string.Join("\n", others.Select(p => p.Driver switch
            {
                DriverKind.User => $"- {p.Name} (human)",
                DriverKind.System => $"- {p.Name} (narrator)",
                _ => $"- {p.Name} (persona)",
            }));

        // Scenario sits between identity and the participant roster: persona-self comes first
        // (primacy), the in-fiction setting establishes context, then who else is there.
        var scenarioSection = string.IsNullOrWhiteSpace(scenario)
            ? string.Empty
            : $"\n# Scenario\n{scenario.Trim()}\n";

        // Memory block lands LAST in the system prompt (recency-positioned), after the
        // style rules — so a memory cue isn't drowned by general etiquette and is the
        // last thing the model sees before reading the conversation history. Decision
        // phase already picked this single recollection; we don't re-pass the full list,
        // because the contract is "decision selects, speaking executes". Active framing
        // ("surfacing for you", "bring it into your reply") invites use rather than just
        // listing facts. Block omitted entirely when no memory was selected.
        var memorySection = string.IsNullOrWhiteSpace(memoryToReference)
            ? string.Empty
            : $"""


                # A memory surfacing for you
                {memoryToReference.Trim()}

                This is on your mind right now. Bring it into your reply the way a callback
                drops into real conversation — naturally, in passing, maybe just an aside.
                Don't quote it; let it shape what you say.
                """;

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
{memorySection}
""";
    }
}

public sealed record class SpeakingResult
{
    public bool Stop { get; init; }
    public string? Message { get; init; }
    public string? Reasoning { get; init; }
    public ChatMessageMetadata? Metadata { get; init; }
}
