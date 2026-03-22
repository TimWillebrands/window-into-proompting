using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonRepairSharp;
using PartyTown.Model;
using PartyTown.Services.Llm;

namespace PartyTown.Services.Generation;

public sealed class GenerationSession(ILlmProvider provider, ILogger<GenerationSession> logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<MessageWithSender> history,
        IReadOnlyList<GenerationParticipant> participants,
        string model,
        Guid roomId,
        Guid? personaOverrideId,
        Guid senderId,
        Func<string, string, bool, Task> onEvent,
        CancellationToken cancellationToken = default)
    {
        OverseerOutput overseer;
        GenerationParticipant? selectedPersona;

        if (personaOverrideId is { } forced)
        {
            selectedPersona = participants.FirstOrDefault(p => p.Id == forced);
            overseer = new OverseerOutput
            {
                PersonaId = forced,
                Reason = "Persona override",
                Instruction = "Continue the conversation naturally.",
                Stop = selectedPersona is null
            };
        }
        else
        {
            overseer = await FindRespondentAsync(history, participants, model, roomId, onEvent, cancellationToken);
            selectedPersona = participants.FirstOrDefault(p => p.Id == overseer.PersonaId);
        }

        if (selectedPersona is null)
        {
            // Fallback to first non-user participant if overseer's selection is invalid
            selectedPersona = participants.FirstOrDefault(p => !p.IsUser);
            if (selectedPersona is not null)
            {
                await onEvent("debug", $"Overseer selected invalid persona {overseer.PersonaId}, falling back to {selectedPersona.Name}", false);
                overseer = overseer with { PersonaId = selectedPersona.Id, Reason = $"{overseer.Reason} (fallback)" };
            }
            else
            {
                await onEvent("error", $"No persona {overseer.PersonaId} found by overseer", false);
                await onEvent("finished", "finished", true);
                return new GenerationResult { Stop = true, Overseer = overseer };
            }
        }

        if (selectedPersona.IsUser)
        {
            await onEvent("userInstruction", overseer.Instruction, false);
            await onEvent("finished", "finished", true);
            return new GenerationResult { Stop = true, Overseer = overseer, Persona = selectedPersona };
        }

        if (overseer.Stop)
        {
            await onEvent("overseerStop", JsonSerializer.Serialize(new { overseer.Reason, overseer.Instruction }), false);
            await onEvent("finished", "finished", true);
            return new GenerationResult { Stop = true, Overseer = overseer, Persona = selectedPersona };
        }

        await onEvent("personaChange", selectedPersona.Id.ToString(), false);

        var messages = new List<LlmChatMessage>
        {
            new()
            {
                Role = "system",
                Content = Instruction(
                    "The headquarters of a stealth software startup producing a mobile app for the horticulture industry",
                    selectedPersona.SystemPrompt ?? string.Empty),
                Name = selectedPersona.Id.ToString()
            }
        };

        messages.AddRange(history.Select(message => new LlmChatMessage
        {
            Role = message.SenderId == selectedPersona.Id ? "assistant" : "user",
            Content = message.SenderId == selectedPersona.Id
                ? (message.Content ?? string.Empty)
                : ToUserScopedMessage(message),
            Name = message.SenderId.ToString()
        }));

        var builder = new StringBuilder();
        var reasoning = new StringBuilder();

        await foreach (var chunk in provider.GenerateAsync(new LlmGenerationParams
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
            Overseer = overseer,
            Persona = selectedPersona
        };
    }

    private async Task<OverseerOutput> FindRespondentAsync(
        IReadOnlyList<MessageWithSender> history,
        IReadOnlyList<GenerationParticipant> participants,
        string model,
        Guid roomId,
        Func<string, string, bool, Task> onEvent,
        CancellationToken cancellationToken)
    {
        var autonomousRounds = CountTrailingAssistantMessages(history);

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = OverseerPrompt(participants) },
            new() { Role = "user", Content = OverseerInstructionMessage(history.TakeLast(8).ToArray(), autonomousRounds) }
        };

        var text = new StringBuilder();

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "overseer",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["personaId"] = new JsonObject { ["type"] = "string" },
                        ["instruction"] = new JsonObject { ["type"] = "string" },
                        ["reason"] = new JsonObject { ["type"] = "string" },
                        ["stop"] = new JsonObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JsonArray("reason", "instruction", "personaId", "stop")
                }
            }
        };

        await foreach (var chunk in provider.GenerateAsync(new LlmGenerationParams
        {
            Model = model,
            Messages = messages,
            UserId = "overseer",
            RoomId = roomId.ToString(),
            ResponseFormat = responseFormat
        }, cancellationToken))
        {
            if (chunk.Type == "message")
            {
                text.Append(chunk.Data);
            }

            await onEvent("overseer", chunk.Data, false);
        }

        var raw = text.ToString().Trim();
        logger.LogInformation("Overseer raw response ({Chars} chars): {Raw}", raw.Length, raw);

        OverseerOutput? parsed = null;

        try
        {
            parsed = JsonSerializer.Deserialize<OverseerOutput>(raw, WebOptions);
            logger.LogInformation("Overseer parsed OK: PersonaId={PersonaId} Stop={Stop} Reason={Reason}", parsed?.PersonaId, parsed?.Stop, parsed?.Reason);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Overseer direct parse failed: {Error}", ex.Message);
        }

        if (parsed is null)
        {
            try
            {
                string repairedJson = JsonRepair.RepairJson(raw);
                parsed = JsonSerializer.Deserialize<OverseerOutput>(repairedJson, WebOptions);
                if (parsed is not null)
                {
                    logger.LogInformation("Overseer parsed via JSON repair: PersonaId={PersonaId}", parsed.PersonaId);
                    await onEvent("debug", "Used JSON repair for overseer output", false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Overseer JSON repair failed: {Error}", ex.Message);
                await onEvent("debug", $"JSON repair failed: {ex.Message}", false);
            }
        }

        // Treat a zero GUID as a failed parse (model returned an empty/invalid ID)
        if (parsed is not null && parsed.PersonaId == Guid.Empty && !parsed.Stop)
        {
            logger.LogWarning("Overseer returned zero GUID for personaId — treating as parse failure");
            await onEvent("debug", "Overseer returned zero GUID for personaId, treating as parse failure", false);
            parsed = null;
        }

        if (parsed is null)
        {
            var fallback = participants.FirstOrDefault(p => !p.IsUser);
            logger.LogWarning("Overseer fallback triggered, selecting {FallbackName}", fallback?.Name ?? "none");
            parsed = new OverseerOutput
            {
                PersonaId = fallback?.Id ?? Guid.Empty,
                Reason = "Fallback responder selected",
                Instruction = "Continue the conversation naturally.",
                Stop = fallback is null
            };
        }

        var participantIds = participants.Select(p => p.Id).ToList();
        logger.LogInformation("Overseer selected PersonaId={PersonaId}, available participants: [{Ids}]",
            parsed.PersonaId, string.Join(", ", participantIds));

        // Send the parsed overseer output to the frontend as a complete event
        // done=false so the generation stream stays active until persona finishes
        await onEvent("overseerComplete", JsonSerializer.Serialize(parsed, WebOptions), false);
        
        return parsed;
    }

    private static string ToUserScopedMessage(MessageWithSender message)
        => $"<message sender=\"{message.SenderName}\" senderId=\"{message.SenderId}\">\n{message.Content}\n</message>";

    private static string OverseerInstructionMessage(IReadOnlyList<MessageWithSender> messages, int autonomousRounds)
    {
        var wrapUpPressure = autonomousRounds switch
        {
            <= 1 => "",
            2 => "\n\n> The conversation has been going for a couple of rounds without human input. Let it breathe — only continue if there's something genuinely worth saying.",
            3 => "\n\n> It's been a few rounds now with no human input. The conversation should start finding a natural stopping point soon.",
            4 => "\n\n> The conversation has been running without human input for a while. Strongly consider setting stop to true unless someone was directly asked a question.",
            5 => "\n\n> This conversation has been on autopilot for too long. It's time to land the plane. Set stop to true unless there's a truly compelling reason to continue.",
            _ => "\n\n> This has gone on long enough without human input. Stop the conversation now. Set stop to true."
        };

        return $"""
# Task
Pick who should speak next. Consider:
- Who was directly addressed or mentioned?
- Who would naturally react to what was just said?
- Would someone new jumping in make the conversation more interesting?

Use the persona's `id` attribute from the participant list as the personaId.
Keep your instruction brief — one sentence, a natural nudge, not a script.

## Recent conversation
{string.Join("\n\n", messages.Select(ToUserScopedMessage))}
{wrapUpPressure}

# Output
JSON object with: personaId (string), reason (string), instruction (string), stop (boolean)
""";
    }

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

    private static string OverseerPrompt(IReadOnlyList<GenerationParticipant> participants)
        => $"""
# Instruction
You are the director of a group chat. Pick who should speak next based on
the natural flow of conversation — who would realistically respond, who was
addressed, or who has something relevant to add.

Keep the conversation feeling natural. Don't always alternate between the same
two people — let others jump in when it makes sense. If the conversation has
reached a natural stopping point, set stop to true.

Your "instruction" field should be a brief, natural nudge (one sentence) —
NOT a detailed script. Let the persona decide what to say. Good examples:
- "React to what Karen just said about the deadline"
- "You were just called out — defend yourself"
- "Change the subject, this is getting stale"

Bad examples (too prescriptive):
- "Respond with a 9-page memo about..."
- "Pretend to get emotional then sabotage..."

# Human participants
Some participants are marked isHuman="true" — these are real people, not AI.
When you select a human, the conversation pauses and your instruction is shown
to them as a prompt for what to say next. Pick the human when:
- They were directly addressed or asked a question
- It's their turn to naturally contribute
- The AI personas have been talking among themselves for too long

Don't pick a human just to fill silence — only when it genuinely makes sense.

# Participants
{string.Join("\n\n", participants.Select(PersonaDetails))}
""";

    private static int CountTrailingAssistantMessages(IReadOnlyList<MessageWithSender> history)
    {
        var count = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].SenderType != "assistant")
                break;
            count++;
        }
        return count;
    }

    private static string PersonaDetails(GenerationParticipant persona)
        => persona.IsUser
            ? $"""
### {persona.Name} (Human)
<persona id="{persona.Id}" name="{persona.Name}" isHuman="true">This is a real human participant. When selected, the conversation pauses for their input.</persona>
"""
            : $"""
### {persona.Name}
<persona id="{persona.Id}" name="{persona.Name}">{persona.Bio}</persona>
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

public sealed record class OverseerOutput
{
    public Guid PersonaId { get; init; }
    public string Instruction { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool Stop { get; init; }
}

public sealed record class GenerationResult
{
    public bool Stop { get; init; }
    public string? Message { get; init; }
    public string? Reasoning { get; init; }
    public OverseerOutput? Overseer { get; init; }
    public GenerationParticipant? Persona { get; init; }
}
