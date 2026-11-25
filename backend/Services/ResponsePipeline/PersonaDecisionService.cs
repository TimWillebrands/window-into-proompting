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

public sealed class PersonaDecisionService(ILlmRouterGrain router, ILogger logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public async Task<ShouldRespondResult> ShouldRespondAsync(
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<ParticipantView> participants,
        int totalAiRoundsInGroup,
        Func<string, string, bool, Task>? onEvent,
        CancellationToken cancellationToken,
        RepairHint? repairHint = null,
        IReadOnlyList<string>? recollections = null,
        IReadOnlyList<string>? stances = null,
        bool isDirect = false,
        string? presence = null,
        bool windingDown = false,
        bool deliberate = false,
        bool inPerson = false)
    {
        var urge = UrgeMath.CalculateResponseUrge(self, history, totalAiRoundsInGroup, directlyAddressed: isDirect);
        var recentSelfMessageCount = UrgeMath.CountRecentSelfMessages(history, self.Id);
        var participantIds = new HashSet<Guid>(participants.Select(p => p.Id));
        var recentMessages = history
            .Where(m => participantIds.Contains(m.SenderId) && !string.IsNullOrWhiteSpace(m.Content))
            .TakeLast(8)
            .Select(m => new ChatMessageWithSenderName(m, participants.First(p => p.Id == m.SenderId).Name));

        if (urge.Total >= 0.9 && repairHint is null && !windingDown && !deliberate)
        {
            logger.LogInformation("Persona {PersonaName} auto-responding (urge={Urge:F2}, mention={Mention:F2})", self.Name, urge.Total, urge.MentionScore);
            return new ShouldRespondResult
            {
                Respond = true,
                Instruction = "React naturally — they spoke to you.",
                Reason = $"Heard my name (urge={urge.Total:F2}). Worth a reply.",
                Shortcut = true,
            };
        }

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ShouldRespondSystemPrompt(self, participants, repairHint, recollections, stances, presence, inPerson) },
            new() { Role = "user", Content =
                ShouldRespondUserPrompt(recentMessages, totalAiRoundsInGroup, recentSelfMessageCount, self, urge) }
        };


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

        var call = await new LlmCall(router, logger, self.Id).StreamAsync(job, LlmTiers.General, cancellationToken,
            onEvent is null ? null : chunk => onEvent(MessageStreamEvent.PersonaEvaluationStreaming, chunk.Data, false),
            thinkSpan);

        var raw = call.Content.Trim();
        logger.LogInformation("Persona {PersonaName} response urge: total={Total:F2} mention={Mention:F2} question={Question:F2} silence={Silence:F2} coldOpen={ColdOpen:F2} chaos={Chaos:F2}",
            self.Name, urge.Total, urge.MentionScore, urge.QuestionScore, urge.SilenceStreakScore, urge.ColdOpenScore, urge.ChaosScore);

        var parsed = LlmJsonParsing.TryDeserialize<ShouldRespondResult>(raw, WebOptions);
        if (parsed is null)
            logger.LogError("Persona {PersonaName} decision JSON unparseable after repair. Raw: {Raw}", self.Name, raw);
        else
            logger.LogInformation("Persona {PersonaName} decision urge={Urge:F2} respond={Respond} ({Chars} chars)",
                self.Name, urge.Total, parsed.Respond, raw.Length);

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

        if (parsed.Respond && string.IsNullOrWhiteSpace(parsed.Instruction))
        {
            parsed = parsed with { Respond = false };
        }
        else if (!parsed.Respond && !string.IsNullOrWhiteSpace(parsed.Instruction))
        {
            parsed = parsed with { Respond = true };
        }

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
        RepairHint? repairHint,
        IReadOnlyList<string>? recollections,
        IReadOnlyList<string>? stances,
        string? presence = null,
        bool inPerson = false)
    {
        var repairBlock = repairHint is null
            ? string.Empty
            : $"\n# Note\nJust before you spoke, {repairHint.Value.MissedSenderName} said: \"{repairHint.Value.MissedContent}\". You weren't aware of this when you wrote your last message. Consider whether to acknowledge.\n";

        var recollectionsBlock = recollections is null || recollections.Count == 0
            ? string.Empty
            : $"\n# What you remember\n{string.Join("\n", recollections.Select((s, i) => $"{i + 1}. {s}"))}\nThese are on your mind — part of how you walk into this moment, not a file you consult.\n";

        var stanceBlock = StanceBlock.Render(stances);

        return $$"""
# You are: {{self.Name}}
{{(string.IsNullOrWhiteSpace(self.Bio) ? "(no bio)" : self.Bio)}}

# Other people in the room
{{string.Join("\n", participants.Where(p => p.Id != self.Id).Select(p => Roster.Line(p)))}}
{{stanceBlock}}{{recollectionsBlock}}{{repairBlock}}
{{(string.IsNullOrWhiteSpace(presence) ? string.Empty : presence.Trim() + "\n")}}
# What you're doing
{{(inPerson ? InPersonScene(self.Name) : $"You're hanging out in a casual group chat. Someone just spoke. Read it\nthe way YOU would — as {self.Name}, with your tastes, hangups, and mood.")}}

First: what's your honest gut reaction? A quick thought, feeling, eye-roll,
laugh, disagreement, "huh, interesting" — whatever actually surfaces.
Always write this. Be specific, in your voice — not a meta-summary. Plain words,
the way you'd think them; most reactions are small and ordinary.

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
- wouldSay: {{(inPerson ? "the words you'd actually say out loud right now" : "what you'd actually type into the chat right now")}}, OR ""
  (empty string) if you'd let it pass. This becomes {{(inPerson ? "what you say" : "your message")}} verbatim
  if you speak — write the words themselves, not a description
  of what you'd say. Do NOT wrap the text in extra quotes; it is {{(inPerson ? "speech" : "a chat\n  message")}}, not a quotation. Don't open it with someone's name, and don't
  repeat anything you already said (see "What you said lately").
- respond: true iff wouldSay is non-empty.
""";
    }

    private static string InPersonScene(string name) => $"""
        You're here in person with the people around you, and someone just said something
        out loud. Take it in the way YOU would — as {name}, with your tastes, hangups, and
        mood, and with where you are: who's here, whether they belong, what hour it is.
        """;

    private static string ShouldRespondUserPrompt(
        IEnumerable<ChatMessageWithSenderName> messages,
        int totalAiRoundsInGroup,
        int recentSelfMessageCount,
        SelfView self,
        ResponseUrge urge)
    {
        var net = UrgeMath.CalculateNetPressure(urge, totalAiRoundsInGroup, recentSelfMessageCount);

        var nudge = net switch
        {
            >= 0.7 => "(Room: people are looking at you — there's space and a pull to chime in.)",
            >= 0.4 => "(Room: there's an opening if you've got something. No pressure either way.)",
            >= 0.0 => "(Room: chatter's flowing — speak if it's worth airtime, otherwise let it ride.)",
            >= -0.4 => "(Room: someone else just had the floor. Lean toward letting it breathe.)",
            _ => "(Room: you've been dominating, or it's clearly not your moment. Pass unless directly addressed.)"
        };

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

    [Id(1)]
    [JsonPropertyName("wouldSay")]
    public string Instruction { get; init; } = string.Empty;

    [Id(2)]
    [JsonPropertyName("gutReaction")]
    public string Reason { get; init; } = string.Empty;

    [Id(3)]
    [JsonPropertyName("memoryToReference")]
    [JsonConverter(typeof(LenientMemoryIndexConverter))]
    public int? MemoryToReference { get; init; }

    [Id(4)]
    [JsonIgnore]
    public bool Shortcut { get; init; }
}

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
