using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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
        string? gutReaction = null,
        string? presence = null)
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
                Content = Instruction(ComposeIdentity(persona), persona, others, scenario, memoryToReference, stances, presence),
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
        {
            // Small-model crutch (candidate to gate off for stronger models): repeat the
            // address cue here, the strongest recency slot, since the system-prompt block alone
            // let small models recite the memory in third person at a present subject.
            var subjects = PresentSubjectsIn(memoryToReference, others);
            var addressCue = subjects.Count == 0
                ? string.Empty
                : $" {JoinNames(subjects)} {(subjects.Count == 1 ? "is" : "are")} right here in the room — say it to {(subjects.Count == 1 ? subjects[0] : "them")} directly, not about them.";
            guidanceParts.Add($"The memory on your mind — \"{memoryToReference.Trim()}\" — belongs in this reply. Work a specific from it in naturally.{addressCue}");
        }
        var lastOther = history.LastOrDefault(m => m.SenderId != persona.Id && !string.IsNullOrWhiteSpace(m.Content));
        guidanceParts.Add(UtteranceRhythm.Cue(UtteranceRhythm.Pick(UtteranceRhythm.Words(lastOther?.Content), Random.Shared.NextDouble())));
        if (guidanceParts.Count > 0)
        {
            messages.Add(new LlmChatMessage
            {
                Role = "system",
                Content = string.Join("\n", guidanceParts),
                Name = persona.Id.ToString()
            });
        }

        using var generateSpan = Tracing.Persona.StartActivity("persona.generate", ActivityKind.Internal);
        generateSpan?.SetTag("persona.id", persona.Id);
        generateSpan?.SetTag("persona.name", persona.Name);

        // The spoken utterance is what the user reads: CharacterVoice tier, degrading to General
        // so a General-only setup never mutes every persona.
        var result = await new LlmCall(router).StreamAsync(
            new LlmGenerationJob { Messages = messages }, LlmTiers.Voice, cancellationToken,
            chunk => onEvent(chunk.Type, chunk.Data, false), generateSpan);

        generateSpan?.SetTag("output.length", result.Content.Length);
        await onEvent(MessageStreamEvent.GenerationComplete, "finished", true);

        return new SpeakingResult
        {
            Stop = false,
            Message = SpeechCleanup.Clean(result.Content, persona.Name),
            Reasoning = result.Reasoning,
            Metadata = result.Attribution
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

    // Other present participants named in the memory text (whole-word, like UrgeMath mentions).
    private static List<string> PresentSubjectsIn(string memory, IEnumerable<ParticipantView> others)
        => others
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && Regex.IsMatch(
                memory,
                $@"\b{Regex.Escape(p.Name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Select(p => p.Name)
            .ToList();

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };

    private static string Instruction(string personaPrompt, SelfView self, List<ParticipantView> others, string? scenario, string? memoryToReference, IReadOnlyList<string>? stances, string? presence = null)
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
        // Small-model crutch (candidate to gate off for stronger models): when the memory
        // names a present participant, tell weak models to address them, not recite about them;
        // suppressed for absent subjects, where third person is correct.
        var presentSubjects = string.IsNullOrWhiteSpace(memoryToReference)
            ? new List<string>()
            : PresentSubjectsIn(memoryToReference, others);
        var addressLine = presentSubjects.Count == 0
            ? string.Empty
            : $"\n{JoinNames(presentSubjects)} {(presentSubjects.Count == 1 ? "is" : "are")} here in the room with you. A memory about someone present is something you say to their face — \"you\", not \"she\" or \"he\".";
        var memorySection = string.IsNullOrWhiteSpace(memoryToReference)
            ? string.Empty
            : $"""


                # A memory surfacing for you
                {memoryToReference.Trim()}

                This is on your mind right now. Bring it into your reply the way a callback
                drops into real conversation — naturally, in passing, maybe just an aside.
                Don't recite it word-for-word, but keep its specifics — names, places,
                plans — those are what make the callback land.{addressLine}
                """;

        // ADR 0016: ambient Stance block — identity-adjacent orientation toward who's present
        // and what's live, rendered in both phases (no Decision→Speaking handoff field). Lands
        // right after the roster, before the generic style rules. Omitted when nothing anchors.
        var stanceSection = StanceBlock.Render(stances);
        var presenceSection = string.IsNullOrWhiteSpace(presence) ? string.Empty : presence.Trim() + "\n";

        // Persona identity block claims the primacy position; chat-style rules land after,
        // so the model sees who it is before it sees generic etiquette.
        return $"""
# You are: {self.Name} (ID: {self.Id})
{LivedSelf.SheetFraming}
{personaPrompt}
{scenarioSection}
# Other participants
{othersSection}
{stanceSection}
{presenceSection}
# Style
{LivedSelf.ChatStyle}
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
