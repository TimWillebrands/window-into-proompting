using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PartyTown.Data;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Generation;

public sealed class GenerationSession(
    ILlmRouterGrain router,
    List<GenerationParticipant> allParticipants,
    IDbContextFactory<AppDbContext>? memoryDb = null)
{
    private const int MaxRecalledMemories = 50;

    /// <summary>
    /// Generates a response for a specific persona.
    /// </summary>
    public async Task<GenerationResult> GenerateResponseOnlyAsync(
        GenerationParticipant persona,
        IReadOnlyList<ChatMessage> history,
        Func<string, string, bool, Task> onEvent,
        CancellationToken cancellationToken,
        string? turnInstruction = null,
        string? scenario = null,
        Guid partyId = default)
    {
        var memoriesBlock = await LoadMemoriesBlockAsync(persona.Id, partyId, cancellationToken);

        var others = allParticipants.Where(p => p.Id != persona.Id).ToList();
        var messages = new List<LlmChatMessage>
        {
            new()
            {
                Role = "system",
                Content = Instruction(persona.SystemPrompt ?? string.Empty, persona, others, scenario, memoriesBlock),
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

        return new GenerationResult
        {
            Stop = false,
            Message = builder.ToString(),
            Reasoning = reasoning.ToString(),
            Persona = persona,
            Metadata = metadata
        };
    }

    private async Task<string> LoadMemoriesBlockAsync(Guid personaId, Guid partyId, CancellationToken ct)
    {
        // The default party legitimately has Guid.Empty as its ID, so we cannot use that as a
        // "no party" sentinel — the only opt-out is when the host did not register a DbContext
        // factory (i.e. unit-test cluster).
        if (memoryDb is null)
            return string.Empty;

        try
        {
            await using var ctx = await memoryDb.CreateDbContextAsync(ct);
            var memories = await ctx.PersonaMemories
                .Where(m => m.PersonaId == personaId && m.PartyId == partyId)
                .OrderByDescending(m => m.EncodedAt)
                .Take(MaxRecalledMemories)
                .Select(m => m.Text)
                .ToListAsync(ct);

            if (memories.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("# Things you remember");
            foreach (var m in memories)
                sb.AppendLine($"- {m}");
            return sb.ToString();
        }
        catch (Exception)
        {
            // Memory recall is best-effort: a DB hiccup must not break generation.
            return string.Empty;
        }
    }

    private static string Instruction(string personaPrompt, GenerationParticipant self, List<GenerationParticipant> others, string? scenario, string memoriesBlock)
    {
        // Names only. Bios used to live here but leaked theme/style across personas:
        // Hana's "shrine"/"sacred" vocabulary primed Vlad to emit 🌸, address Hana before
        // she'd spoken, and adopt mystic register. Roster gives identity, not character.
        var othersSection = others.Count == 0
            ? "(no other participants)"
            : string.Join("\n", others.Select(p =>
                p.IsUser
                    ? $"- {p.Name} (human)"
                    : $"- {p.Name} (persona)"));

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
{scenarioSection}{memoriesBlock}
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

    /// <summary>0..1 dial controlling urge to chime in. Drives chaos-bonus weighting in
    /// PersonaDecisionService. Defaults to 0.5 for users / unset personas.</summary>
    public double Chattiness { get; init; } = 0.5;

    /// <summary>0..1 dial controlling commitment-to-in-flight-utterance. 0 = deliberative
    /// (easily interrupted, repairs often); 1 = impulsive (commits hard, rarely repairs).
    /// Drives the stop-signal race in PersonaGrain. Defaults to 0.5 for users / unset personas.</summary>
    public double Impulsivity { get; init; } = 0.5;
}

public sealed record class GenerationResult
{
    public bool Stop { get; init; }
    public string? Message { get; init; }
    public string? Reasoning { get; init; }
    public GenerationParticipant? Persona { get; init; }
    public ChatMessageMetadata? Metadata { get; init; }
}
