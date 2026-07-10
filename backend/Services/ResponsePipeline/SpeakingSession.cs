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
    /// <paramref name="gutReaction"/> is the decision phase's in-character first reaction;
    /// it rides the final turn-guidance message so the speaking model refines a felt
    /// moment instead of re-deriving one from cold history.
    /// </summary>
    public async Task<SpeakingResult> GenerateResponseOnlyAsync(
        SelfView persona,
        IReadOnlyList<ChatMessage> history,
        Func<string, string, bool, Task> onEvent,
        CancellationToken cancellationToken,
        string? turnInstruction = null,
        string? scenario = null,
        string? memoryToReference = null,
        IReadOnlyList<string>? stances = null,
        string? gutReaction = null)
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
                Content = Instruction(ComposeIdentity(persona), persona, others, scenario, memoryToReference, stances),
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

        // Recency-position handoff from the decision step. Gut reaction and draft carry the
        // decision's lived read of the beat; the picked memory is restated here as well —
        // the system-prompt block alone proved droppable by smaller models (bench: pick
        // made, utterance generic), and this final message is the strongest recency slot.
        var guidanceParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(gutReaction))
            guidanceParts.Add($"How this moment hits you: {gutReaction.Trim()}");
        if (!string.IsNullOrWhiteSpace(turnInstruction))
            guidanceParts.Add($"Draft of what you'd say: {turnInstruction.Trim()} — make it your own; refine, don't recite.");
        if (!string.IsNullOrWhiteSpace(memoryToReference))
            guidanceParts.Add($"The memory on your mind — \"{memoryToReference.Trim()}\" — belongs in this reply. Work a specific from it in naturally.");
        if (guidanceParts.Count > 0)
        {
            messages.Add(new LlmChatMessage
            {
                Role = "system",
                Content = string.Join("\n", guidanceParts),
                Name = persona.Id.ToString()
            });
        }

        var builder = new StringBuilder();
        var reasoning = new StringBuilder();

        using var generateSpan = Tracing.Persona.StartActivity("persona.generate", ActivityKind.Internal);
        generateSpan?.SetTag("persona.id", persona.Id);
        generateSpan?.SetTag("persona.name", persona.Name);

        // The spoken utterance is what the user reads — route it to the CharacterVoice tier
        // (a smarter model) while the high-frequency Decision phase stays on the fast General
        // tier. Falls back to General when no CharacterVoice-capable provider is configured
        // (mirrors MemoryExtractor.RouteExtractionAsync) so speaking degrades in quality,
        // never in function — without this a General-only setup silently mutes every persona.
        var complexity = JobComplexity.CharacterVoice;
        ILlmEndpointGrain generation;
        try
        {
            generation = await router.RouteAsync(complexity, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            complexity = JobComplexity.General;
            generation = await router.RouteAsync(complexity, cancellationToken);
            generateSpan?.SetTag("llm.tier_fallback", true);
        }

        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = complexity
        };
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

    /// <summary>
    /// The persona's self-identity block: Bio (the one-liner the Decision phase also sees)
    /// followed by SystemPrompt (detailed voice/character instructions) — either alone,
    /// both when present. Speaking used to read SystemPrompt only, so a persona authored
    /// with just a Bio spoke from a blank identity (name + generic style rules): strong
    /// models carried the voice anyway, weak models collapsed to assistant register
    /// (bench: in-character decision, out-of-character utterance). Both phases now anchor
    /// on the same Bio. Note this is the persona's OWN identity — other participants stay
    /// names-only in the roster (see the bio-leak note in <see cref="Instruction"/>).
    /// </summary>
    private static string ComposeIdentity(SelfView self)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(self.Bio))
            parts.Add(self.Bio.Trim());
        if (!string.IsNullOrWhiteSpace(self.SystemPrompt))
            parts.Add(self.SystemPrompt.Trim());
        return string.Join("\n\n", parts);
    }

    private static string Instruction(string personaPrompt, SelfView self, List<ParticipantView> others, string? scenario, string? memoryToReference, IReadOnlyList<string>? stances)
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
                Don't recite it word-for-word, but keep its specifics — names, places,
                plans — those are what make the callback land.
                """;

        // ADR 0016: ambient Stance block — identity-adjacent orientation toward who's present
        // and what's live, rendered in both phases (no Decision→Speaking handoff field). Lands
        // right after the roster, before the generic style rules. Omitted when nothing anchors.
        var stanceSection = StanceBlock.Render(stances);

        // Persona identity block claims the primacy position; chat-style rules land after,
        // so the model sees who it is before it sees generic etiquette.
        return $"""
# You are: {self.Name} (ID: {self.Id})
{personaPrompt}
{scenarioSection}
# Other participants
{othersSection}
{stanceSection}
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
