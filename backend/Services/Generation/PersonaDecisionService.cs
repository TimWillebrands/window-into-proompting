using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Generation;

/// <summary>
/// Per-persona decision service: each persona independently decides whether to respond.
///
/// Two axes are kept distinct:
///   • Frequency control — how often a persona speaks (round penalty, self-dominance,
///     chattiness-weighted chaos). Stays mathematical, prevents spam.
///   • Engagement register — when the persona DOES engage, the prompt asks them to
///     react in-character first ("gut reaction"), then judge whether it's worth airtime.
///     This stops the "no signal / no clear prompt" assistant-restraint failure mode where
///     personas decline cold-open user messages with bureaucratic justifications.
/// </summary>
public sealed class PersonaDecisionService(ILlmRouterGrain router, ILogger logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Calculates a response urge score (0.0 - 1.0) from mention detection, silence streak,
    /// question presence, cold-open (fresh user message into a quiet room), and chattiness-
    /// weighted chaos.
    /// </summary>
    public static ResponseUrge CalculateResponseUrge(
        GenerationParticipant self,
        IReadOnlyList<ChatMessage> history,
        int totalAiRoundsInGroup)
    {
        double mentionScore = 0;
        double questionScore = 0;
        double silenceStreakScore = 0;
        double coldOpenScore = 0;

        if (history.Count == 0)
            return new ResponseUrge(0, 0, 0, 0, 0, Random.Shared.NextDouble() * self.Chattiness);

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

        // Cold-open: a fresh user message landing in a quiet room (no AI activity yet).
        // Without this floor, "hiya" yields urge≈0 and every persona declines with a
        // politeness-flavoured justification. A bar with a new arrival should produce
        // at least one reaction.
        if (latest.SenderType == "user" && totalAiRoundsInGroup == 0)
            coldOpenScore = 0.5;

        // Chaos weighted by per-persona chattiness. Replaces pure random with
        // character-driven variability — a chatty Denise pings more than a brooding Vlad.
        var chaosScore = Random.Shared.NextDouble() * self.Chattiness;

        var total = Math.Min(1.0, mentionScore + questionScore + silenceStreakScore + coldOpenScore);
        return new ResponseUrge(total, mentionScore, questionScore, silenceStreakScore, coldOpenScore, chaosScore);
    }

    /// <summary>
    /// Appraises whether the persona should respond based on the conversation history,
    /// participants, and (optionally) the scenario the chat is set in.
    /// <paramref name="repairHint"/> carries a Levelt-style speech-repair cue: when the
    /// persona shipped its previous message past the point of no return *and* a relevant
    /// new message was missed, the hint nudges the next decision toward acknowledgment.
    /// </summary>
    public async Task<ShouldRespondResult> ShouldRespondAsync(
        GenerationParticipant self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<GenerationParticipant> participants,
        int totalAiRoundsInGroup,
        Func<string, string, bool, Task>? onEvent,
        CancellationToken cancellationToken,
        string? scenario = null,
        RepairHint? repairHint = null)
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
                Instruction = "React naturally — they spoke to you.",
                Reason = $"Heard my name (urge={urge.Total:F2}). Worth a reply."
            };
        }

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ShouldRespondSystemPrompt(self, participants, scenario, repairHint) },
            new() { Role = "user", Content =
                ShouldRespondUserPrompt(recentMessages, totalAiRoundsInGroup, recentSelfMessageCount, self, urge) }
        };

        var text = new StringBuilder();

        // Schema field order drives generation order. gutReaction first → the model engages
        // in-character before judging airtime. wouldSay next — the literal text they'd type
        // (or empty). respond derives last from whether wouldSay is non-empty. This inverts
        // the previous reason→respond order, which encouraged the model to write a justification
        // frame absorbing the assistant-restraint prior.
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
                        ["gutReaction"] = new JsonObject { ["type"] = "string" },
                        ["wouldSay"] = new JsonObject { ["type"] = "string" },
                        ["respond"] = new JsonObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JsonArray("gutReaction", "wouldSay", "respond")
                }
            }
        };

        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = JobComplexity.General,
            ResponseFormat = responseFormat.ToJsonString()
        };

        using var thinkSpan = Tracing.Persona.StartActivity("persona.think", ActivityKind.Internal);
        thinkSpan?.SetTag("persona.id", self.Id);
        thinkSpan?.SetTag("persona.name", self.Name);
        thinkSpan?.SetTag("urge.total", urge.Total);

        var generation = await router.RouteAsync(job.JobComplexity, cancellationToken);

        try
        {
            var attribution = await generation.GetAttributionAsync();
            thinkSpan?.SetTag("llm.provider", attribution?.Provider);
            thinkSpan?.SetTag("llm.model", attribution?.ModelName);
        }
        catch
        {
            // Attribution is best-effort instrumentation; don't fail the decision over it.
        }

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
        logger.LogInformation("Persona {PersonaName} response urge: total={Total:F2} mention={Mention:F2} question={Question:F2} silence={Silence:F2} coldOpen={ColdOpen:F2} chaos={Chaos:F2}",
            self.Name, urge.Total, urge.MentionScore, urge.QuestionScore, urge.SilenceStreakScore, urge.ColdOpenScore, urge.ChaosScore);

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
            logger.LogDebug("Persona {PersonaName} decision parse failed (will try cleanup): {Error}", self.Name, ex.Message);
        }

        if (parsed is null)
        {
            // Cleanup pipeline for LLM-produced JSON:
            //   1. Strip markdown code fences (```json … ```) that some models wrap output in.
            //   2. Escape raw control chars (newline, tab, etc.) found *inside* string values —
            //      JsonRepairSharp does not handle these, but the symptom appears often enough
            //      in multi-line `gutReaction` fields that we fix it before handing off.
            var cleaned = LlmJsonParsing.ExtractJsonPayload(raw);

            try
            {
                parsed = JsonSerializer.Deserialize<ShouldRespondResult>(cleaned, WebOptions);
            }
            catch
            {
                try
                {
                    var repaired = JsonRepair.RepairJson(cleaned, JsonRepair.InputType.LLM);
                    parsed = JsonSerializer.Deserialize<ShouldRespondResult>(repaired, WebOptions);
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Persona {PersonaName} decision JSON repair failed: {Error}. Raw: {Raw}",
                        self.Name, ex.Message, raw);
                }
            }
        }

        // Fail closed: don't respond if we can't parse the decision
        parsed ??= new ShouldRespondResult
        {
            Respond = false,
            Instruction = string.Empty,
            Reason = "Fallback — unparseable decision"
        };

        // Coherence guard: model can emit respond=true with an empty wouldSay, or vice-versa.
        // Trust the wouldSay payload — it's the actual text the persona would utter.
        if (parsed.Respond && string.IsNullOrWhiteSpace(parsed.Instruction))
        {
            parsed = parsed with { Respond = false };
        }
        else if (!parsed.Respond && !string.IsNullOrWhiteSpace(parsed.Instruction))
        {
            parsed = parsed with { Respond = true };
        }

        if (!parsed.Respond && parsed.Reason.StartsWith("Fallback"))
            logger.LogDebug("Persona {PersonaName} suppressed due to unparseable decision. Raw: {Raw}", self.Name, raw);

        thinkSpan?.SetTag("decision.respond", parsed.Respond);
        thinkSpan?.SetTag("decision.gut_reaction", parsed.Reason);

        if (onEvent is not null)
        {
            await onEvent(MessageStreamEvent.PersonaEvaluationComplete, JsonSerializer.Serialize(parsed, WebOptions), true);
        }

        return parsed;
    }

    public static int CountRecentSelfMessages(IReadOnlyList<ChatMessage> history, Guid selfId)
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
    /// Net pressure = pull (urge total + chaos bonus) minus push (round penalty +
    /// self-dominance penalty). Mirrors the bucketed nudge in the LLM-facing prompt.
    /// Centralised so the pre-gate (PersonaGrain short-circuit) and the prompt agree.
    /// </summary>
    public static double CalculateNetPressure(ResponseUrge urge, int totalAiRoundsInGroup, int recentSelfMessageCount)
    {
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
        return urge.Total - roundPenalty - selfPenalty + chaosBonus;
    }

    /// <summary>
    /// True when the math is decisive enough to skip the LLM decision call entirely
    /// (and skip reserving a thought-log slot). Requires no direct mention, no question,
    /// and net pressure deep in the "pass" bucket. Cuts the ~1480-thought-per-1480-msgs
    /// runaway visible in the UI when every persona deliberates on every cascade message.
    /// </summary>
    public static bool IsObviousSkip(ResponseUrge urge, int totalAiRoundsInGroup, int recentSelfMessageCount)
    {
        if (urge.MentionScore > 0 || urge.QuestionScore > 0)
            return false;
        var net = CalculateNetPressure(urge, totalAiRoundsInGroup, recentSelfMessageCount);
        return net < -0.4;
    }

    private static string ShouldRespondSystemPrompt(
        GenerationParticipant self,
        IReadOnlyList<GenerationParticipant> participants,
        string? scenario,
        RepairHint? repairHint)
    {
        var scenarioBlock = string.IsNullOrWhiteSpace(scenario)
            ? string.Empty
            : $"\n# Setting\n{scenario.Trim()}\n";

        // Levelt-style speech repair cue. Set when this persona finished an utterance
        // *after* a relevant new message arrived — they couldn't see it at the time.
        // Surfacing it here lets the persona acknowledge the miss naturally; whether
        // they actually do so is left to character (low-impulsivity = tends to repair).
        var repairBlock = repairHint is null
            ? string.Empty
            : $"\n# Note\nJust before you spoke, {repairHint.Value.MissedSenderName} said: \"{repairHint.Value.MissedContent}\". You weren't aware of this when you wrote your last message. Consider whether to acknowledge.\n";

        return $$"""
# You are: {{self.Name}}
{{(string.IsNullOrWhiteSpace(self.Bio) ? "(no bio)" : self.Bio)}}
{{scenarioBlock}}
# Other people in the room
{{string.Join("\n", participants.Where(p => p.Id != self.Id).Select(p =>
    p.IsUser
        ? $"- {p.Name} (human)"
        : $"- {p.Name}: {p.Bio ?? "no bio"}"))}}
{{repairBlock}}
# What you're doing
You're hanging out in a casual group chat. Someone just spoke. Read it
the way YOU would — as {{self.Name}}, with your tastes, hangups, and mood.

First: what's your honest gut reaction? A quick thought, feeling, eye-roll,
laugh, disagreement, "huh, interesting" — whatever actually surfaces.
Always write this. Be specific, in your voice — not a meta-summary.

Second: would you actually say something out loud right now? Speak when
your reaction is worth airtime — when you have a take, a feeling, a quip,
a counterpoint, a question, a "yes and." Pass when you're just nodding
along, when someone else is mid-flow, or when you've been doing all the
talking. Boring silence is worse than a small chime-in; constant interjection
is worse than letting the room breathe. Use judgement.

# Output (JSON)
- gutReaction: short, in-character first thought. Always written.
- wouldSay: what you'd actually type into the chat right now, OR ""
  (empty string) if you'd let it pass. This becomes your message verbatim
  if you speak — write it as the chat message itself, not as a description
  of what you'd say.
- respond: true iff wouldSay is non-empty.
""";
    }

    private static string ShouldRespondUserPrompt(
        IEnumerable<ChatMessageWithSenderName> messages,
        int totalAiRoundsInGroup,
        int recentSelfMessageCount,
        GenerationParticipant self,
        ResponseUrge urge)
    {
        var net = CalculateNetPressure(urge, totalAiRoundsInGroup, recentSelfMessageCount);

        // Math nudge framed as a social cue (room-state) rather than a directive — so the
        // model reads it as context, not as a command from the system that overrides character.
        var nudge = net switch
        {
            >= 0.7 => "(Room: people are looking at you — there's space and a pull to chime in.)",
            >= 0.4 => "(Room: there's an opening if you've got something. No pressure either way.)",
            >= 0.0 => "(Room: chatter's flowing — speak if it's worth airtime, otherwise let it ride.)",
            >= -0.4 => "(Room: someone else just had the floor. Lean toward letting it breathe.)",
            _ => "(Room: you've been dominating, or it's clearly not your moment. Pass unless directly addressed.)"
        };

        var renderedMessages = string.Join(
            "\n\n",
            messages.Select(m => ChatMessageRenderer.Render(m.Message, m.SenderName)));

        return $"""
# Recent conversation
{renderedMessages}

{nudge}

# Your turn ({self.Name})
React first (gutReaction). Then decide if it's worth saying out loud (wouldSay).
JSON only.
""";
    }
}

public readonly record struct ResponseUrge(
    double Total,
    double MentionScore,
    double QuestionScore,
    double SilenceStreakScore,
    double ColdOpenScore,
    double ChaosScore);

/// <summary>
/// One-shot Levelt-style speech-repair cue. Set by the race in <c>PersonaGrain</c> when a
/// persona shipped its message past the point of no return *and* a relevant new message
/// arrived during the in-flight generation. Consumed by the next <see cref="PersonaDecisionService.ShouldRespondAsync"/>
/// call (the call site clears it regardless of decision outcome — see <c>PersonaGrain</c>).
/// In-memory only; not persisted across grain deactivation.
/// </summary>
public readonly record struct RepairHint(
    int MissedMessageId,
    string MissedSenderName,
    string MissedContent);

[GenerateSerializer, Alias(nameof(ShouldRespondResult))]
public sealed record class ShouldRespondResult
{
    [Id(0)]
    [JsonPropertyName("respond")]
    public bool Respond { get; init; }

    /// <summary>
    /// The literal text the persona would type into the chat — empty when declining.
    /// Serialized as "wouldSay" in the LLM-facing JSON schema; field name retained as
    /// Instruction so the downstream <c>turnInstruction</c> seed and frontend
    /// <c>appraisal.instruction</c> consumer keep working.
    /// </summary>
    [Id(1)]
    [JsonPropertyName("wouldSay")]
    public string Instruction { get; init; } = string.Empty;

    /// <summary>
    /// In-character first reaction — always written. Serialized as "gutReaction" in the
    /// LLM-facing JSON schema; field name retained as Reason so the thought-log UI
    /// (which reads <c>appraisal.reason</c>) keeps surfacing it without a frontend change.
    /// </summary>
    [Id(2)]
    [JsonPropertyName("gutReaction")]
    public string Reason { get; init; } = string.Empty;
}

public readonly record struct ChatMessageWithSenderName(ChatMessage Message, string SenderName);
