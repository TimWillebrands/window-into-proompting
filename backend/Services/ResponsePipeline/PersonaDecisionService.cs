using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.ResponsePipeline;

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
    /// Appraises whether the persona should respond based on the conversation history,
    /// participants, and (optionally) the scenario the chat is set in.
    /// <paramref name="repairHint"/> carries a Levelt-style speech-repair cue: when the
    /// persona shipped its previous message past the point of no return *and* a relevant
    /// new message was missed, the hint nudges the next decision toward acknowledgment.
    /// <paramref name="recollections"/> is the top-N salience-ranked Recollection snippets
    /// for this persona in this party (ADR 0015 recall) — rendered as a numbered "what you
    /// remember" block in the system prompt so the persona can naturally bring up past
    /// moments and pick one by index. Empty list = no block rendered.
    /// <paramref name="stances"/> is the anchor-scoped, latest-wins Stance lines (ADR 0016) —
    /// rendered as the ambient "# Where you stand" block. Unlike recollections it carries no
    /// selection mechanic: it is identity-adjacent orientation the persona speaks from.
    /// </summary>
    public async Task<ShouldRespondResult> ShouldRespondAsync(
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ParticipantView> participants,
        int totalAiRoundsInGroup,
        Func<string, string, bool, Task>? onEvent,
        CancellationToken cancellationToken,
        string? scenario = null,
        RepairHint? repairHint = null,
        IReadOnlyList<string>? recollections = null,
        IReadOnlyList<string>? stances = null)
    {
        var urge = UrgeMath.CalculateResponseUrge(self, history, totalAiRoundsInGroup);
        var recentSelfMessageCount = UrgeMath.CountRecentSelfMessages(history, self.Id);
        var participantIds = new HashSet<Guid>(participants.Select(p => p.Id));
        var recentMessages = history
            .Where(m => participantIds.Contains(m.SenderId) && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(8)
            // TODO: Store SenderName directly on ChatMessage to avoid this lookup. See: Option 1 (denormalize sender name into message)
            .Select(m => new ChatMessageWithSenderName(m, participants.First(p => p.Id == m.SenderId).Name));

        // Auto-respond threshold: if urge is very high (direct mention), skip LLM decision
        // — UNLESS a repair hint is pending. The auto-respond shortcut returns canned text
        // and bypasses the system-prompt repairBlock entirely, so a missed message would be
        // ignored and the persona would barrel through name-mentions without acknowledging
        // anything else that arrived during the in-flight generation. When repair is pending,
        // pay the LFM call so the prompt-level repair stanza (and the "(They said your name.)"
        // user-prompt cue below) both fire.
        if (urge.Total >= 0.9 && repairHint is null)
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
            new() { Role = "system", Content = ShouldRespondSystemPrompt(self, participants, scenario, repairHint, recollections, stances) },
            new() { Role = "user", Content =
                ShouldRespondUserPrompt(recentMessages, totalAiRoundsInGroup, recentSelfMessageCount, self, urge) }
        };

        var text = new StringBuilder();

        // Schema field order drives generation order. gutReaction first → the model engages
        // in-character before judging airtime. memoryToReference next — having just felt the
        // moment, the model decides whether a past memory belongs in it (and which one),
        // before drafting the literal reply. wouldSay then carries the sketch shaped by both;
        // respond derives last from whether wouldSay is non-empty.
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
                        ["memoryToReference"] = new JsonObject
                        {
                            // ADR 0015: pick by index, not by copy — the integer is the
                            // strengthening write's key. Strict mode requires every field in
                            // `required`; the nullable type-array form lets the model
                            // legitimately decline to pick.
                            ["type"] = new JsonArray("integer", "null")
                        },
                        ["wouldSay"] = new JsonObject { ["type"] = "string" },
                        ["respond"] = new JsonObject { ["type"] = "boolean" }
                    },
                    ["required"] = new JsonArray("gutReaction", "memoryToReference", "wouldSay", "respond")
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
                    logger.LogError("Persona {PersonaName} decision JSON repair failed: {Error}. Raw: {Raw}",
                        self.Name, ex.Message, raw);
                }
            }
        }

        // Fail closed: don't respond if we can't parse the decision. Embed a truncated
        // raw payload in Reason so the thought-log papertrail preserves it past log
        // rotation — without this, post-hoc forensics has nothing to work from.
        if (parsed is null)
        {
            var rawSnippet = raw.Length > 240 ? raw[..240] + "…" : raw;
            parsed = new ShouldRespondResult
            {
                Respond = false,
                Instruction = string.Empty,
                Reason = $"Fallback — unparseable decision. Raw: {rawSnippet}"
            };
        }

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

        // Clamp the memory pick to the surfaced list: an out-of-range index — or any pick
        // when nothing was surfaced — is model noise, not a usable strengthening key.
        if (parsed.MemoryToReference is int pick &&
            (pick < 1 || pick > (recollections?.Count ?? 0)))
        {
            parsed = parsed with { MemoryToReference = null };
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

    private static string ShouldRespondSystemPrompt(
        SelfView self,
        IReadOnlyList<ParticipantView> participants,
        string? scenario,
        RepairHint? repairHint,
        IReadOnlyList<string>? recollections,
        IReadOnlyList<string>? stances)
    {
        // The trailing grounding line counters small-model scene drift: gut reactions were
        // observed inventing a different time/place than the setting states ("another
        // Monday morning" in a Thursday-afternoon scene).
        var scenarioBlock = string.IsNullOrWhiteSpace(scenario)
            ? string.Empty
            : $"\n# Setting\n{scenario.Trim()}\nThis is where and when you are. React from inside this scene — don't invent a different time, place, or occasion.\n";

        // Levelt-style speech repair cue. Set when this persona finished an utterance
        // *after* a relevant new message arrived — they couldn't see it at the time.
        // Surfacing it here lets the persona acknowledge the miss naturally; whether
        // they actually do so is left to character (low-impulsivity = tends to repair).
        var repairBlock = repairHint is null
            ? string.Empty
            : $"\n# Note\nJust before you spoke, {repairHint.Value.MissedSenderName} said: \"{repairHint.Value.MissedContent}\". You weren't aware of this when you wrote your last message. Consider whether to acknowledge.\n";

        // ADR 0015: top-N salience-ranked Recollection snippets for this Persona in this
        // Party, across all Rooms. Numbered so the model picks by index (the strengthening
        // key) instead of copying text; beat-relevance is still judged in-context. Block is
        // omitted entirely when empty so it never reads as a void "you remember nothing".
        // The trailing line frames the memories as live salience, not archive — without it
        // weak models treat the list as reference material and never deploy it.
        var recollectionsBlock = recollections is null || recollections.Count == 0
            ? string.Empty
            : $"\n# What you remember\n{string.Join("\n", recollections.Select((s, i) => $"{i + 1}. {s}"))}\nThese are on your mind — part of how you walk into this moment, not a file you consult.\n";

        // ADR 0016: ambient Stance block. Identity-adjacent — who you are relative to who's
        // here and what's live in the message — so it sits right after the roster, no
        // selection mechanic. Omitted when nothing is anchored.
        var stanceBlock = StanceBlock.Render(stances);

        return $$"""
# You are: {{self.Name}}
{{(string.IsNullOrWhiteSpace(self.Bio) ? "(no bio)" : self.Bio)}}
{{scenarioBlock}}
# Other people in the room
{{string.Join("\n", participants.Where(p => p.Id != self.Id).Select(p => p.Driver switch
{
    DriverKind.User => $"- {p.Name} (human)",
    DriverKind.System => $"- {p.Name} (narrator)",
    _ => $"- {p.Name} (persona)",
}))}}
{{stanceBlock}}{{recollectionsBlock}}{{repairBlock}}
# What you're doing
You're hanging out in a casual group chat. Someone just spoke. Read it
the way YOU would — as {{self.Name}}, with your tastes, hangups, and mood.

First: what's your honest gut reaction? A quick thought, feeling, eye-roll,
laugh, disagreement, "huh, interesting" — whatever actually surfaces.
Always write this. Be specific, in your voice — not a meta-summary.

Second: would you actually say something out loud right now? Speak when
your reaction is worth airtime — when you have a take, a feeling, a quip,
a counterpoint, a question, a "yes and." Big personal news from someone
in the room — quitting a career, moving away, starting a venture, a loss —
is worth airtime by default: letting it land in silence reads as not
caring, and even one dry line in your voice beats saying nothing.
Pass when you're just nodding
along, when someone else is mid-flow, or when you've been doing all the
talking. Boring silence is worse than a small chime-in; constant interjection
is worse than letting the room breathe. Use judgement.

# Output (JSON)
- gutReaction: short, in-character first thought. Always written.
- memoryToReference: if "# What you remember" is shown above AND one
  of those memories touches this moment — the person speaking, what they
  said, what's visibly going on — put that memory's number (e.g. 2) here.
  Remembering is what makes you a friend instead of a stranger: when
  someone's news, plan, or life shows up in front of you and you remember
  it, that memory belongs in the moment. Set null only when no memory
  genuinely connects, or you already called it back a beat ago — a forced
  callback is worse than none, but ignoring what you plainly remember is
  worse than both. When set, that memory travels with you into the
  speaking phase and shapes what you actually type.
- wouldSay: what you'd actually type into the chat right now, OR ""
  (empty string) if you'd let it pass. This becomes your message verbatim
  if you speak — write it as the chat message itself, not as a description
  of what you'd say. Do NOT wrap the text in extra quotes; it is a chat
  message, not a quotation.
- respond: true iff wouldSay is non-empty.
""";
    }

    private static string ShouldRespondUserPrompt(
        IEnumerable<ChatMessageWithSenderName> messages,
        int totalAiRoundsInGroup,
        int recentSelfMessageCount,
        SelfView self,
        ResponseUrge urge)
    {
        var net = UrgeMath.CalculateNetPressure(urge, totalAiRoundsInGroup, recentSelfMessageCount);

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

        // Direct-address cue. Mention score isn't otherwise surfaced to the model: when
        // the auto-respond shortcut is bypassed (because of a pending repair hint), this
        // ensures the persona still feels the pull of being named and usually still speaks
        // — just *aware* of whatever they missed during the in-flight generation.
        var mentionCue = urge.MentionScore > 0
            ? "(They said your name.)\n"
            : string.Empty;

        var renderedMessages = string.Join(
            "\n\n",
            messages.Select(m => ChatMessageRenderer.Render(m.Message, m.SenderName)));

        return $"""
# Recent conversation
{renderedMessages}

{mentionCue}{nudge}

# Your turn ({self.Name})
React first (gutReaction). Then decide if it's worth saying out loud (wouldSay).

Output a single JSON object matching the schema. No prose before or after.
No markdown fences. No commentary. JSON only.
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

    /// <summary>
    /// The decision phase's pick — the 1-based index into the numbered "# What you
    /// remember" block (ADR 0015: pick by index, not by copy) — or null when nothing fit.
    /// The pipeline resolves it back to the recalled memory: the snippet travels to the
    /// speaking phase, the edge id keys the strengthening write. Null on the auto-respond
    /// shortcut (decision LLM never ran, so no memory was selected).
    /// </summary>
    [Id(3)]
    [JsonPropertyName("memoryToReference")]
    [JsonConverter(typeof(LenientMemoryIndexConverter))]
    public int? MemoryToReference { get; init; }
}

/// <summary>
/// Lenient reader for the decision's memory index: accepts an integer, an integer-shaped
/// string ("2"), or null. Anything else — typically a model pasting the memory text
/// despite the integer schema — reads as null instead of failing the whole decision parse
/// (which would fail closed and mute the persona).
/// </summary>
internal sealed class LenientMemoryIndexConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var n))
                    return n;
                if (reader.TryGetDouble(out var d))
                {
                    var rounded = Math.Round(d);
                    return rounded >= int.MinValue && rounded <= int.MaxValue ? (int)rounded : null;
                }
                return null;
            case JsonTokenType.String:
                return int.TryParse(reader.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is int v)
            writer.WriteNumberValue(v);
        else
            writer.WriteNullValue();
    }
}

public readonly record struct ChatMessageWithSenderName(ChatMessage Message, string SenderName);
