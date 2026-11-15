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
        string? presence = null,
        IReadOnlyList<string>? avoidWords = null,
        bool inPerson = false)
    {
        var others = allParticipants.Where(p => p.Id != persona.Id).ToList();
        var nameById = allParticipants.ToDictionary(p => p.Id, p => p.Name);
        var messages = new List<LlmChatMessage>
        {
            new()
            {
                Role = "system",
                Content = Instruction(ComposeIdentity(persona), persona, others, scenario, memoryToReference, stances, presence, inPerson),
                Name = persona.Id.ToString()
            }
        };

        messages.AddRange(history.Where(m => !string.IsNullOrWhiteSpace(m.Content)).Select(message => new LlmChatMessage
        {
            Role = message.SenderId == persona.Id ? "assistant" : "user",
            Content = message.SenderId == persona.Id
                ? (message.Content ?? string.Empty)
                : ChatMessageRenderer.Render(
                    message,
                    nameById.TryGetValue(message.SenderId, out var n) ? n : "Unknown"),
            Name = message.SenderId.ToString()
        }));

        var guidanceParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(gutReaction))
            guidanceParts.Add($"How this moment hits you: {gutReaction.Trim()}");
        if (!string.IsNullOrWhiteSpace(turnInstruction))
            guidanceParts.Add($"What you mean to get across: {turnInstruction.Trim()} Say it your own way, and leave out whatever you already said lately.");
        if (!string.IsNullOrWhiteSpace(memoryToReference))
        {
            var subjects = PresentSubjectsIn(memoryToReference, others);
            var addressCue = subjects.Count == 0
                ? string.Empty
                : $" {JoinNames(subjects)} {(subjects.Count == 1 ? "is" : "are")} right here in the room — say it to {(subjects.Count == 1 ? subjects[0] : "them")} directly, not about them.";
            guidanceParts.Add($"A memory that may be on your mind: \"{memoryToReference.Trim()}\". Only if it fits what's being said now, work a specific from it in; otherwise leave it.{addressCue}");
        }
        var lastOther = history.LastOrDefault(m => m.SenderId != persona.Id && !string.IsNullOrWhiteSpace(m.Content));
        if (avoidWords is { Count: > 0 })
            guidanceParts.Add($"Leave these words out this time, they've gone stale here: {string.Join(", ", avoidWords.Take(8))}.");
        var lastOtherName = lastOther is null ? null : nameById.GetValueOrDefault(lastOther.SenderId);
        guidanceParts.Add(UtteranceRhythm.Cue(UtteranceRhythm.Pick(UtteranceRhythm.Words(lastOther?.Content), Random.Shared.NextDouble())));
        guidanceParts.Add(OnlyTheLine(persona.Name, inPerson));
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

        var result = await new LlmCall(router, persona: persona.Id).StreamAsync(
            new LlmGenerationJob { Messages = messages }, LlmTiers.Voice, cancellationToken,
            chunk => onEvent(chunk.Type, chunk.Data, false), generateSpan);

        generateSpan?.SetTag("output.length", result.Content.Length);
        await onEvent(MessageStreamEvent.GenerationComplete, "finished", true);

        return new SpeakingResult
        {
            Stop = false,
            Message = SpeechCleanup.DropObviousVocative(
                SpeechCleanup.DropEcho(SpeechCleanup.Clean(result.Content, persona.Name), lastOther?.Content),
                lastOtherName),
            Reasoning = result.Reasoning,
            Metadata = result.Attribution
        };
    }

    public static string OnlyTheLine(string name, bool inPerson)
    {
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
        return $"Now reply with only what {first} {(inPerson ? "says out loud" : "sends")}, word for word: no plan, no notes on length, language or wording, no summary of what others said.";
    }

    private static string ComposeIdentity(SelfView self)
    {
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(self.Bio))
            parts.Add(self.Bio.Trim());
        if (!string.IsNullOrWhiteSpace(self.SystemPrompt))
            parts.Add(self.SystemPrompt.Trim());
        return string.Join("\n\n", parts);
    }

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

    private static string Instruction(string personaPrompt, SelfView self, List<ParticipantView> others, string? scenario, string? memoryToReference, IReadOnlyList<string>? stances, string? presence = null, bool inPerson = false)
    {
        var othersSection = others.Count == 0
            ? "(no other participants)"
            : string.Join("\n", others.Select(p => Roster.Line(p)));

        var scenarioSection = string.IsNullOrWhiteSpace(scenario)
            ? string.Empty
            : $"\n# Scenario\n{scenario.Trim()}\n";

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

                It may be on your mind. Only if it fits what's being said right now, let it drop in
                the way a callback does in real conversation: in passing, with its own specifics
                (names, places, plans), never recited. The conversation in front of you comes first;
                if it doesn't fit, leave it.{addressLine}
                """;

        var stanceSection = StanceBlock.Render(stances);
        var presenceSection = string.IsNullOrWhiteSpace(presence) ? string.Empty : presence.Trim() + "\n";

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
{(inPerson ? LivedSelf.InPersonStyle : LivedSelf.ChatStyle)}
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
