using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Generation;

/// <summary>
/// Per-persona decision service: each persona independently decides whether to respond.
/// Replaces the global Overseer with a self-referential "should I speak?" LLM call.
/// </summary>
public sealed class PersonaDecisionService(ILlmRouterGrain router, ILogger logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Calculates a response urge score (0.0 - 1.0) based on mention detection,
    /// silence streak, and question presence. Used to auto-respond or add pressure.
    /// </summary>
    public static ResponseUrge CalculateResponseUrge(
        GenerationParticipant self,
        IReadOnlyList<ChatMessage> history,
        int totalAiRoundsInGroup)
    {
        double mentionScore = 0;
        double questionScore = 0;
        double silenceStreakScore = 0;

        if (history.Count == 0)
            return new ResponseUrge(0, 0, 0, 0, Random.Shared.NextDouble());

        var latest = history[^1];
        var contentLower = (latest.Content ?? "").ToLowerInvariant();
        var nameLower = self.Name.ToLowerInvariant();

        // Direct mention: is the persona's name in the latest message?
        if (contentLower.Contains(nameLower))
            mentionScore = 1.0;

        // Question detection: does the latest message end with a question mark?
        if (ContentTrimmed(latest.Content ?? "").EndsWith('?'))
            questionScore = 0.6;

        // Silence streak: how many AI rounds without this persona responding?
        // Each round without a response increases urge slightly
        silenceStreakScore = Math.Min(0.4, totalAiRoundsInGroup * 0.1);

        var chaosScore = Random.Shared.NextDouble();
        var total = Math.Min(1.0, mentionScore + questionScore + silenceStreakScore);
        return new ResponseUrge(total, mentionScore, questionScore, silenceStreakScore, chaosScore);
    }

    private static string ContentTrimmed(string content) => content.TrimEnd();

    /// <summary>
    /// Appraises whether the persona should respond based on the conversation history and participants.
    /// </summary>
    public async Task<ShouldRespondResult> ShouldRespondAsync(
        GenerationParticipant self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<GenerationParticipant> participants,
        int totalAiRoundsInGroup,
        Func<string, string, bool, Task>? onEvent,
        CancellationToken cancellationToken)
    {
        var urge = CalculateResponseUrge(self, history, totalAiRoundsInGroup);
        var recentSelfMessageCount = CountRecentSelfMessages(history, self.Id);
        var participantIds = new HashSet<Guid>(participants.Select(p => p.Id));
        var recentMessages = history
            .Where(m => participantIds.Contains(m.SenderId) && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(8)
            // TODO: Store SenderName directly on ChatMessage to avoid this lookup. See: Option 1 (denormalize sender name into message)
            .Select(m => new ChatMessageWithSenderName(m, participants.First(p => p.Id == m.SenderId).Name));

        // Auto-respond threshold: if urge is very high (direct mention), skip LLM decision
        if (urge.Total >= 0.9)
        {
            logger.LogInformation("Persona {PersonaName} auto-responding (urge={Urge:F2}, mention={Mention:F2})", self.Name, urge.Total, urge.MentionScore);
            return new ShouldRespondResult
            {
                Respond = true,
                Instruction = "You were directly addressed — respond naturally.",
                Reason = $"Auto-respond: direct mention detected (urge={urge.Total:F2})"
            };
        }

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ShouldRespondSystemPrompt(self, participants) },
            new() { Role = "user", Content =
                ShouldRespondUserPrompt(recentMessages, totalAiRoundsInGroup, recentSelfMessageCount, self, urge) }
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

        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = JobComplexity.General,
            ResponseFormat = responseFormat.ToJsonString()
        };

        var generation = await router.RouteAsync(job.JobComplexity, cancellationToken);

        await foreach (var chunk in generation.GenerateAsync(job, cancellationToken))
        {
            if (chunk.Type == "message")
            {
                text.Append(chunk.Data);
            }

            if (onEvent is not null)
            {
                await onEvent(MessageStreamEvent.PersonaEvaluationStreaming, chunk.Data, false);
            }
        }

        var raw = text.ToString().Trim();
        logger.LogInformation("Persona {PersonaName} response urge: total={Total:F2} mention={Mention:F2} question={Question:F2} silence={Silence:F2} chaos={Chaos:F2}",
            self.Name, urge.Total, urge.MentionScore, urge.QuestionScore, urge.SilenceStreakScore, urge.ChaosScore);

        ShouldRespondResult? parsed = null;

        try
        {
            parsed = JsonSerializer.Deserialize<ShouldRespondResult>(raw, WebOptions);
            logger.LogInformation("Persona {PersonaName} decision urge={Urge:F2} respond={Respond} ({Chars} chars)",
                self.Name, urge.Total, parsed?.Respond, raw.Length);
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Persona {PersonaName} decision raw: {Raw}", self.Name, raw);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Persona {PersonaName} decision parse failed: {Error}", self.Name, ex.Message);
        }

        if (parsed is null)
        {
            try
            {
                string repairedJson = JsonRepair.RepairJson(raw, JsonRepair.InputType.LLM);
                parsed = JsonSerializer.Deserialize<ShouldRespondResult>(repairedJson, WebOptions);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Persona {PersonaName} decision JSON repair failed: {Error}", self.Name, ex.Message);
            }
        }

        // Fail closed: don't respond if we can't parse the decision
        parsed ??= new ShouldRespondResult
        {
            Respond = false,
            Instruction = "Do not send a message.",
            Reason = "Fallback — unparseable decision"
        };

        if (!parsed.Respond && parsed.Reason.StartsWith("Fallback"))
            logger.LogDebug("Persona {PersonaName} suppressed due to unparseable decision. Raw: {Raw}", self.Name, raw);

        if (onEvent is not null)
        {
            await onEvent(MessageStreamEvent.PersonaEvaluationComplete, JsonSerializer.Serialize(parsed, WebOptions), true);
        }

        return parsed;
    }

    private static int CountRecentSelfMessages(IReadOnlyList<ChatMessage> history, Guid selfId)
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
        IEnumerable<ChatMessageWithSenderName> messages,
        int totalAiRoundsInGroup,
        int recentSelfMessageCount,
        GenerationParticipant self,
        ResponseUrge urge)
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

        var urgePressure = urge.Total switch
        {
            >= 0.7 => "\n\n> There's a strong signal that you should respond right now — lean toward speaking up.",
            >= 0.5 => "\n\n> Some indicators suggest you might want to chime in. Consider it, but use your judgment.",
            >= 0.3 => "\n\n> The situation is somewhat quiet. You could respond if it feels natural, but it's not urgent.",
            _ => ""
        };

        var chaosPressure = urge.ChaosScore switch
        {
            >= 0.85 => "\n\n> (internal) Something sparks in you — an impulsive, irrational urge to just say something. Act on it.",
            >= 0.65 => "\n\n> (internal) For no particular reason, you feel like chiming in today.",
            _ => ""
        };

        return $"""
# Recent conversation
{string.Join("\n\n", messages.Select(m => $"<message senderName=\"{SecurityElement.Escape(m.SenderName)}\" senderId=\"{m.Message.SenderId}\">\n{SecurityElement.Escape(m.Message.Content ?? "")}\n</message>"))}
{urgePressure}{chaosPressure}{pressure}{selfPressure}

# Decision
Should {self.Name} respond right now?
JSON object with: respond (boolean), instruction (string — guidance for your response if respond=true), reason (string — why or why not)
""";
    }
}

public readonly record struct ResponseUrge(double Total, double MentionScore, double QuestionScore, double SilenceStreakScore, double ChaosScore);

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

public readonly record struct ChatMessageWithSenderName(ChatMessage Message, string SenderName);
