using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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
        var content = latest.Content ?? "";

        // Direct mention: persona name as a whole word (case-insensitive).
        // Substring matching triggered on "Tim" in "intimate", "optimization", etc.
        if (!string.IsNullOrWhiteSpace(self.Name))
        {
            var mentionRegex = new Regex(
                $@"\b{Regex.Escape(self.Name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mentionRegex.IsMatch(content))
                mentionScore = 1.0;
        }

        // Question detection: does the latest message end with a question mark?
        if (content.TrimEnd().EndsWith('?'))
            questionScore = 0.6;

        // Silence streak: how many AI rounds without this persona responding?
        // Each round without a response increases urge slightly
        silenceStreakScore = Math.Min(0.4, totalAiRoundsInGroup * 0.1);

        var chaosScore = Random.Shared.NextDouble();
        var total = Math.Min(1.0, mentionScore + questionScore + silenceStreakScore);
        return new ResponseUrge(total, mentionScore, questionScore, silenceStreakScore, chaosScore);
    }

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

        // Schema field order drives generation order. Reason first → the model reasons before
        // committing to a boolean, countering the confirmation-bias pattern where an early
        // `respond` decision is then rationalized by a post-hoc `reason`.
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
                        ["reason"] = new JsonObject { ["type"] = "string" },
                        ["respond"] = new JsonObject { ["type"] = "boolean" },
                        ["instruction"] = new JsonObject { ["type"] = "string" }
                    },
                    ["required"] = new JsonArray("reason", "respond", "instruction")
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
# You are: {self.Name}
Bio: {self.Bio ?? "No bio"}

# Task
You are deciding whether YOU should respond to the latest message in a group chat.
You are NOT generating a response — just deciding whether to speak.

# Other participants
{string.Join("\n", participants.Where(p => p.Id != self.Id).Select(p =>
    p.IsUser
        ? $"- {p.Name} (human)"
        : $"- {p.Name}: {p.Bio ?? "No bio"}"))}

# How to decide
Consider:
- Were you directly addressed, mentioned, or asked a question?
- Would you naturally react to what was just said, given your personality?
- Would it be weird or forced if you jumped in right now?
- Has someone else already said what you would say?
- Have you been talking too much recently? Give others space.

Be honest — not everyone needs to respond to everything. Silence is fine.

# Output
Respond with a JSON object. Reason through the decision first; the `respond` boolean
must follow from your reasoning, not precede it. If you decide to respond, provide
a brief instruction (one sentence) to guide your response — a natural nudge, not a script.
""";

    private static string ShouldRespondUserPrompt(
        IEnumerable<ChatMessageWithSenderName> messages,
        int totalAiRoundsInGroup,
        int recentSelfMessageCount,
        GenerationParticipant self,
        ResponseUrge urge)
    {
        // Net pressure bucket. Previously four independent switches rendered contradictory lines
        // like "lean toward speaking up" + "do NOT respond unless asked" in the same prompt.
        // Here we collapse pull (urge, chaos) and push (rounds, self-dominance) into one signal
        // so the model sees a single, coherent nudge.
        var roundPenalty = totalAiRoundsInGroup switch
        {
            <= 1 => 0.0,
            2 => 0.15,
            3 => 0.35,
            4 => 0.6,
            _ => 0.9
        };
        var selfPenalty = recentSelfMessageCount switch
        {
            0 => 0.0,
            1 => 0.3,
            _ => 0.7
        };
        var chaosBonus = urge.ChaosScore switch
        {
            >= 0.85 => 0.3,
            >= 0.65 => 0.1,
            _ => 0.0
        };

        var net = urge.Total - roundPenalty - selfPenalty + chaosBonus;

        var nudge = net switch
        {
            >= 0.7 => "> Strong pull to speak up — lean toward responding.",
            >= 0.4 => "> Some pull to chime in. Use your judgment.",
            >= 0.0 => "> No strong signal either way.",
            >= -0.4 => "> Lean toward staying quiet — give others room.",
            _ => "> Do NOT respond unless you were directly asked a question."
        };

        var renderedMessages = string.Join(
            "\n\n",
            messages.Select(m => ChatMessageRenderer.Render(m.Message, m.SenderName)));

        return $"""
# Recent conversation
{renderedMessages}

{nudge}

# Decision
Should {self.Name} respond right now?
JSON object with: reason (string — think first, why or why not), respond (boolean — follows from reason), instruction (string — guidance if respond=true, empty otherwise)
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
