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
    /// <summary>
    /// Compute a bounded urgency score indicating how strongly the given persona should respond to the latest message.
    /// </summary>
    /// <param name="self">The persona whose name and identity are used to detect direct mentions.</param>
    /// <param name="history">Conversation history; the latest message (history[^1]) is used for mention and question detection. If empty, all scores are zero.</param>
    /// <param name="totalAiRoundsInGroup">A proxy for recent AI-only turns in the group used to compute silence-streak pressure.</param>
    /// <returns>
    /// A <see cref="ResponseUrge"/> where <c>Total</c> is between 0.0 and 1.0 and component scores are:
    /// <c>MentionScore</c> (direct name mention), <c>QuestionScore</c> (latest message ends with '?'), and <c>SilenceStreakScore</c> (pressure from consecutive AI rounds).
    /// </returns>
    public static ResponseUrge CalculateResponseUrge(
        GenerationParticipant self,
        IReadOnlyList<ChatMessage> history,
        int totalAiRoundsInGroup)
    {
        double mentionScore = 0;
        double questionScore = 0;
        double silenceStreakScore = 0;

        if (history.Count == 0)
            return new ResponseUrge(0, 0, 0, 0);

        var latest = history[^1];
        var contentLower = (latest.Content ?? "").ToLowerInvariant();
        var nameLower = self.Name.ToLowerInvariant();

        // Direct mention: is the persona's name in the latest message?
        if (contentLower.Contains(nameLower))
            mentionScore = 1.0;

        // Question detection: does the latest message end with a question mark?
        if (contentTrimmed(latest.Content ?? "").EndsWith('?'))
            questionScore = 0.6;

        // Silence streak: how many AI rounds without this persona responding?
        // Each round without a response increases urge slightly
        silenceStreakScore = Math.Min(0.4, totalAiRoundsInGroup * 0.1);

        var total = Math.Min(1.0, mentionScore + questionScore + silenceStreakScore);
        return new ResponseUrge(total, mentionScore, questionScore, silenceStreakScore);
    }

    /// <summary>
/// Returns the input string with trailing whitespace removed.
/// </summary>
/// <param name="content">The string to trim.</param>
/// <returns>The string with trailing whitespace removed; returns the original string if it has no trailing whitespace.</returns>
private static string contentTrimmed(string content) => content.TrimEnd();

    /// <summary>
    /// Appraises whether the persona should respond based on the conversation history and participants.
    /// <summary>
    /// Decides whether the given persona should send a message in the conversation by combining deterministic urgency heuristics with an optional LLM-enforced decision.
    /// </summary>
    /// <param name="self">The persona for whom the decision is being made.</param>
    /// <param name="history">Full conversation history used to compute urgency and build the prompt.</param>
    /// <param name="participants">All group participants used to resolve sender display names in recent messages.</param>
    /// <param name="totalAiRoundsInGroup">Autopilot-length metric that increases pressure to speak as it grows.</param>
    /// <param name="onEvent">Optional streaming callback invoked for evaluation events. Called with (eventName, payload, final) where payload is partial or final data and final indicates completion.</param>
    /// <param name="cancellationToken">Cancellation token for LLM routing and streaming operations.</param>
    /// <returns>A <see cref="ShouldRespondResult"/> containing the decision: whether to respond, a short instruction for the persona if responding, and a human-readable reason. If the LLM output is unparseable, the method returns a fallback result that does not respond.</returns>
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
        var recentMessages = history.TakeLast(8)
            .Where(m => participantIds.Contains(m.SenderId))
            .Select(m => new ChatMessageWithSenderName(m, participants.First(p => p.Id == m.SenderId).Name));

        // TODO: Store SenderName directly on ChatMessage to avoid this lookup. See: Option 1 (denormalize sender name into message)

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
        logger.LogInformation("Persona {PersonaName} response urge: total={Total:F2} mention={Mention:F2} question={Question:F2} silence={Silence:F2}",
            self.Name, urge.Total, urge.MentionScore, urge.QuestionScore, urge.SilenceStreakScore);

        ShouldRespondResult? parsed = null;

        try
        {
            parsed = JsonSerializer.Deserialize<ShouldRespondResult>(raw, WebOptions);
            logger.LogInformation("Persona {PersonaName} decision urge={Urge:F2} respond={Respond} ({Chars} chars)",
                self.Name, urge.Total, parsed.Respond, raw.Length);
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

    /// <summary>
    /// Count consecutive assistant messages at the end of the conversation that were sent by the specified persona.
    /// </summary>
    /// <param name="history">Conversation history to scan from newest to oldest.</param>
    /// <param name="selfId">The persona's sender ID to match.</param>
    /// <returns>The number of consecutive messages at the end of <paramref name="history"/> whose <c>SenderType</c> is "assistant" and whose <c>SenderId</c> equals <paramref name="selfId"/>.</returns>
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

    /// <summary>
/// Builds a system prompt that instructs a persona to decide whether they should respond in a group chat.
/// </summary>
/// <param name="self">The persona whose decision the prompt is framing (name and bio are injected).</param>
/// <param name="participants">All group participants; other participants (excluding <paramref name="self"/>) are listed in the prompt with human/AI annotations.</param>
/// <returns>A prompt string describing the decision task, the persona's profile, and the other participants to be sent as the system message.</returns>
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

    /// <summary>
    /// Builds the user prompt sent to the LLM that summarizes recent conversation context and pressure signals, and asks whether the persona should respond now.
    /// </summary>
    /// <param name="messages">Recent messages with resolved sender display names, ordered from oldest to newest.</param>
    /// <param name="totalAiRoundsInGroup">Number of consecutive AI-only turns in the group used to adjust "autopilot" pressure text.</param>
    /// <param name="recentSelfMessageCount">Count of consecutive recent messages from the persona to discourage dominating the conversation.</param>
    /// <param name="self">The persona for whom the decision is being made; used to personalize the final decision question.</param>
    /// <param name="urge">Precomputed urgency scores (total and components) that influence guidance text in the prompt.</param>
    /// <returns>A single-string user prompt containing an XML-like recent conversation block, configurable pressure paragraphs, and an explicit JSON decision instruction.</returns>
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

        return $"""
# Recent conversation
{string.Join("\n\n", messages.Select(m => $"<message senderName=\"{SecurityElement.Escape(m.SenderName)}\" senderId=\"{m.Message.SenderId}\">\n{SecurityElement.Escape(m.Message.Content ?? "")}\n</message>"))}
{urgePressure}{pressure}{selfPressure}

# Decision
Should {self.Name} respond right now?
JSON object with: respond (boolean), instruction (string — guidance for your response if respond=true), reason (string — why or why not)
""";
    }
}

public readonly record struct ResponseUrge(double Total, double MentionScore, double QuestionScore, double SilenceStreakScore);

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
