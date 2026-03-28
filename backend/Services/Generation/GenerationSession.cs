using System.Text;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.Generation;

public sealed class GenerationSession(ILlmEndpointGrain endpoint)
{
    /// <summary>
    /// Generates a response for a specific persona. No overseer — the persona has already decided to respond.
    /// </summary>
    public async Task<GenerationResult> GenerateResponseOnlyAsync(
        GenerationParticipant persona,
        IReadOnlyList<MessageWithSender> history,
        string model,
        Guid roomId,
        Guid senderId,
        Func<string, string, bool, Task> onEvent,
        CancellationToken cancellationToken)
    {
        var messages = new List<LlmChatMessage>
        {
            new()
            {
                Role = "system",
                Content = Instruction(
                    "The headquarters of a stealth software startup producing a mobile app for the horticulture industry",
                    persona.SystemPrompt ?? string.Empty),
                Name = persona.Id.ToString()
            }
        };

        messages.AddRange(history.Select(message => new LlmChatMessage
        {
            Role = message.SenderId == persona.Id ? "assistant" : "user",
            Content = message.SenderId == persona.Id
                ? (message.Content ?? string.Empty)
                : ToUserScopedMessage(message),
            Name = message.SenderId.ToString()
        }));

        var builder = new StringBuilder();
        var reasoning = new StringBuilder();

        await foreach (var chunk in endpoint.GenerateAsync(new LlmGenerationParams
        {
            Model = model,
            Messages = messages,
            UserId = senderId.ToString(),
            RoomId = roomId.ToString()
        }, cancellationToken))
        {
            if (chunk.Type == "message")
            {
                builder.Append(chunk.Data);
            }
            else if (chunk.Type == "reasoning")
            {
                reasoning.Append(chunk.Data);
            }

            await onEvent(chunk.Type, chunk.Data, false);
        }

        await onEvent("finished", "finished", true);

        return new GenerationResult
        {
            Stop = false,
            Message = builder.ToString(),
            Reasoning = reasoning.ToString(),
            Persona = persona
        };
    }

    private static string ToUserScopedMessage(MessageWithSender message)
        => $"<message sender=\"{message.SenderName}\" senderId=\"{message.SenderId}\">\n{message.Content}\n</message>";

    private static string Instruction(string scenario, string personaPrompt)
        => $"""
# Instruction
You are in a group chat with other people. Stay completely in character
as your persona — never acknowledge that you are an AI or playing a role.

Talk like a real person in a casual group chat: short, reactive, and natural.
Actually respond to what others just said — agree, disagree, interrupt, joke,
get annoyed, ask follow-up questions. Don't monologue or lecture.

Keep your messages short — a few sentences at most, like a real chat message.
Only go longer if you're genuinely explaining something or telling a story.

You can use *italics* sparingly for brief actions or reactions (e.g. *sighs*,
*leans back*), but don't narrate elaborate scenes or stage directions.

# Persona
{personaPrompt}

# Scenario
{scenario}
""";
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
