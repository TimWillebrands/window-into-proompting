using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.Generation;

/// <summary>
/// Per-persona decision service: each persona independently decides whether to respond.
/// Replaces the global Overseer with a self-referential "should I speak?" LLM call.
/// </summary>
public sealed class PersonaDecisionService(ILlmEndpointGrain endpoint, ILogger logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShouldRespondResult> ShouldRespondAsync(
        GenerationParticipant self,
        IReadOnlyList<MessageWithSender> history,
        IReadOnlyList<GenerationParticipant> participants,
        string model,
        Guid roomId,
        int totalAiRoundsInGroup,
        Func<string, string, bool, Task>? onEvent,
        CancellationToken cancellationToken)
    {
        var recentSelfMessageCount = CountRecentSelfMessages(history, self.Id);

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ShouldRespondSystemPrompt(self, participants) },
            new() { Role = "user", Content = ShouldRespondUserPrompt(history.TakeLast(8).ToArray(), totalAiRoundsInGroup, recentSelfMessageCount, self) }
        };

        var text = new StringBuilder();

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "should_respond",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["respond"] = new JsonObject { ["type"] = "boolean" },
                        ["instruction"] = new JsonObject { ["type"] = "string" },
                        ["reason"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray("respond", "instruction", "reason")
                }
            }
        };

        await foreach (var chunk in endpoint.GenerateAsync(new LlmGenerationParams
        {
            Model = model,
            Messages = messages,
            UserId = $"persona-decision-{self.Id}",
            RoomId = roomId.ToString(),
            ResponseFormat = responseFormat.ToJsonString()
        }, cancellationToken))
        {
            if (chunk.Type == "message")
            {
                text.Append(chunk.Data);
            }

            if (onEvent is not null)
            {
                await onEvent("overseer", chunk.Data, false);
            }
        }

        var raw = text.ToString().Trim();
        logger.LogInformation("Persona {PersonaName} decision raw ({Chars} chars): {Raw}", self.Name, raw.Length, raw);

        ShouldRespondResult? parsed = null;

        try
        {
            parsed = JsonSerializer.Deserialize<ShouldRespondResult>(raw, WebOptions);
            logger.LogInformation("Persona {PersonaName} decision: Respond={Respond} Reason={Reason}", self.Name, parsed?.Respond, parsed?.Reason);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Persona {PersonaName} decision parse failed: {Error}", self.Name, ex.Message);
        }

        if (parsed is null)
        {
            try
            {
                string repairedJson = JsonRepair.RepairJson(raw);
                parsed = JsonSerializer.Deserialize<ShouldRespondResult>(repairedJson, WebOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Persona {PersonaName} decision JSON repair failed: {Error}", self.Name, ex.Message);
            }
        }

        // Default to responding if we can't parse — better to speak than stay silent
        parsed ??= new ShouldRespondResult
        {
            Respond = true,
            Instruction = "Continue the conversation naturally.",
            Reason = "Fallback — could not parse decision"
        };

        if (onEvent is not null)
        {
            await onEvent("overseerComplete", JsonSerializer.Serialize(parsed, WebOptions), false);
        }

        return parsed;
    }

    private static int CountRecentSelfMessages(IReadOnlyList<MessageWithSender> history, Guid selfId)
    {
        var count = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].SenderType != "assistant")
                break;
            if (history[i].SenderId == selfId)
                count++;
        }
        return count;
    }

    private static string ShouldRespondSystemPrompt(GenerationParticipant self, IReadOnlyList<GenerationParticipant> participants)
        => $"""
# Instruction
You are {self.Name}. You are deciding whether YOU should respond to the latest message
in a group chat. You are NOT generating a response — just deciding whether to speak.

Consider:
- Were you directly addressed, mentioned, or asked a question?
- Would you naturally react to what was just said, given your personality?
- Would it be weird or forced if you jumped in right now?
- Has someone else already said what you would say?
- Have you been talking too much recently? Give others space.

Be honest — not everyone needs to respond to everything. Silence is fine.
If you do decide to respond, provide a brief instruction (one sentence) to guide
your response — a natural nudge, not a script.

# About you
Name: {self.Name}
Bio: {self.Bio ?? "No bio"}

# Other participants
{string.Join("\n", participants.Where(p => p.Id != self.Id).Select(p =>
    p.IsUser
        ? $"- {p.Name} (human)"
        : $"- {p.Name}: {p.Bio ?? "No bio"}"))}
""";

    private static string ShouldRespondUserPrompt(
        IReadOnlyList<MessageWithSender> messages,
        int totalAiRoundsInGroup,
        int recentSelfMessageCount,
        GenerationParticipant self)
    {
        var pressure = totalAiRoundsInGroup switch
        {
            <= 1 => "",
            2 => "\n\n> The conversation has been going for a couple of rounds without human input. Only respond if you have something genuinely worth saying.",
            3 => "\n\n> It's been a few rounds with no human input. Think carefully about whether you really need to add something.",
            4 => "\n\n> The conversation has been running without human input for a while. Strongly lean toward not responding unless you were directly asked.",
            _ => "\n\n> This conversation has been on autopilot for too long. Do NOT respond unless you were explicitly asked a question."
        };

        var selfPressure = recentSelfMessageCount switch
        {
            0 => "",
            1 => "\n\n> You spoke recently. Make sure you're not dominating the conversation.",
            >= 2 => "\n\n> You've spoken multiple times in a row. Strongly consider staying quiet to let others talk.",
            _ => ""
        };

        return $"""
# Recent conversation
{string.Join("\n\n", messages.Select(m => $"<message sender=\"{m.SenderName}\" senderId=\"{m.SenderId}\">\n{m.Content}\n</message>"))}
{pressure}{selfPressure}

# Decision
Should {self.Name} respond right now?
JSON object with: respond (boolean), instruction (string — guidance for your response if respond=true), reason (string — why or why not)
""";
    }
}

[GenerateSerializer, Alias(nameof(ShouldRespondResult))]
public sealed record class ShouldRespondResult
{
    [Id(0)]
    public bool Respond { get; init; }

    [Id(1)]
    public string Instruction { get; init; } = string.Empty;

    [Id(2)]
    public string Reason { get; init; } = string.Empty;
}
