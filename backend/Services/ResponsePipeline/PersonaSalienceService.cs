using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JsonRepairSharp;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;


namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Stop-signal salience classifier. When a new message lands at a persona that already
/// has an in-flight generation, the race in <c>PersonaGrain</c> needs to know whether the
/// new info is worth interrupting for. This service answers that question with a single
/// LLM call routed through <see cref="JobComplexity.CharacterThoughts"/>.
///
/// CONTRACT: <c>JobComplexity.CharacterThoughts</c> MUST be wired to a fast/cheap model
/// (e.g. LFM2). The race math assumes this call is cheap relative to the generation it
/// might cancel. Configure provider capability via <c>LlmProviderEntry.SupportedComplexities</c>.
///
/// Failure mode is "let it ride": on routing failure, parse failure, or any exception,
/// return salience=0 so the in-flight generation continues. Conservative default —
/// preserves current behavior rather than introducing a new failure surface.
/// </summary>
public sealed class PersonaSalienceService(
    ILlmRouterGrain router,
    ILogger logger,
    PartyTown.Services.SystemOne.ISystemOneJudge? judge = null)
{
    private static readonly IReadOnlyDictionary<string, string?> Kinds = new Dictionary<string, string?>
    {
        ["redirect"] = "the new message changes the topic so the reply in progress would seem odd",
        ["contradict"] = "the new message contradicts what the reply in progress assumes",
        ["tangent"] = "on-topic, but the reply in progress still works",
        ["irrelevant"] = "unrelated to the reply in progress",
    };

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Score how strongly <paramref name="newMessage"/> should interrupt the in-flight
    /// utterance described by <paramref name="inFlightGutReaction"/> /
    /// <paramref name="inFlightWouldSay"/> / <paramref name="inFlightGeneratedSoFar"/>.
    /// Returns 0.0 (irrelevant) to 1.0 (must-redirect).
    /// </summary>
    public async Task<SalienceScore> ScoreAsync(
        SelfView self,
        string inFlightGutReaction,
        string inFlightWouldSay,
        string inFlightGeneratedSoFar,
        ChatMessage newMessage,
        string newMessageSenderName,
        CancellationToken ct)
    {
        using var span = Tracing.Persona.StartActivity("persona.salience", ActivityKind.Internal);
        span?.SetTag("persona.id", self.Id);
        span?.SetTag("persona.name", self.Name);
        span?.SetTag("new_message.sender", newMessageSenderName);

        // Interruption is pure weighing: System One answers it in one calibrated pass when available.
        if (judge is { Enabled: true })
        {
            try
            {
                var state = UserPrompt(self.Name, inFlightGutReaction, inFlightWouldSay, inFlightGeneratedSoFar,
                    newMessage.Content ?? string.Empty, newMessageSenderName);
                var answers = await judge.AskAsync(state, new Dictionary<string, PartyTown.Services.SystemOne.SystemOneQuestion>
                {
                    ["interrupt"] = new PartyTown.Services.SystemOne.NoulQuestion(
                        $"If {self.Name} finishes and sends the in-flight message unchanged, it will look out of touch, wrong or rude given the new message."),
                    ["kind"] = new PartyTown.Services.SystemOne.ChoiceQuestion(
                        "How does the new message relate to the in-flight message?", Kinds),
                }, ct);
                if (answers.Nouls.ContainsKey("interrupt"))
                {
                    var kind = answers.Choices.TryGetValue("kind", out var dist) && dist.Count > 0
                        ? dist.MaxBy(kv => kv.Value).Key
                        : "tangent";
                    var scored = new SalienceScore(Math.Clamp(answers.Noul("interrupt", 0), 0, 1), kind);
                    span?.SetTag("salience.source", "system-one");
                    span?.SetTag("salience.value", scored.Value);
                    span?.SetTag("salience.kind", scored.Kind);
                    return scored;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Persona {PersonaName} System One salience failed — falling back to LLM", self.Name);
            }
        }

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = SystemPrompt(self.Name) },
            new() { Role = "user", Content = UserPrompt(
                self.Name,
                inFlightGutReaction,
                inFlightWouldSay,
                inFlightGeneratedSoFar,
                newMessage.Content ?? string.Empty,
                newMessageSenderName) }
        };

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "salience",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["salience"] = new JsonObject { ["type"] = "number" },
                        ["kind"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("redirect", "contradict", "tangent", "irrelevant")
                        }
                    },
                    ["required"] = new JsonArray("salience", "kind")
                }
            }
        };

        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = JobComplexity.CharacterThoughts,
            ResponseFormat = responseFormat.ToJsonString()
        };

        // Race path stays on the cheap tier; any failure lets the in-flight draft ride —
        // an unavailable cheap model must never cancel a generation.
        string content;
        try
        {
            content = (await new LlmCall(router, logger).StreamAsync(job, LlmTiers.ThoughtsOnly, ct, span: span)).Content;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Persona {PersonaName} salience call failed — defaulting to 0", self.Name);
            span?.SetTag("salience.fallback", ex is InvalidOperationException ? "routing" : "generation");
            return SalienceScore.LetItRide;
        }

        var raw = content.Trim();
        var parsed = TryParse(raw);
        if (parsed is null)
        {
            logger.LogWarning("Persona {PersonaName} salience parse failed — defaulting to 0. Raw: {Raw}", self.Name, raw);
            span?.SetTag("salience.fallback", "parse");
            return SalienceScore.LetItRide;
        }

        // Clamp defensively — strict schema should already enforce 0..1 but cheap models
        // sometimes hallucinate magnitudes. Race math depends on a clean unit interval.
        var clamped = Math.Clamp(parsed.Value.Salience, 0.0, 1.0);
        var result = new SalienceScore(clamped, parsed.Value.Kind);
        span?.SetTag("salience.value", result.Value);
        span?.SetTag("salience.kind", result.Kind);
        return result;
    }

    private static SalienceParsed? TryParse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        SalienceParsed? Attempt(string candidate)
        {
            try { return JsonSerializer.Deserialize<SalienceParsed>(candidate, WebOptions); }
            catch { return null; }
        }

        var direct = Attempt(raw);
        if (direct is not null) return direct;

        var cleaned = LlmJsonParsing.ExtractJsonPayload(raw);
        var afterCleanup = Attempt(cleaned);
        if (afterCleanup is not null) return afterCleanup;

        try
        {
            var repaired = PartyTown.Services.Memory.SafeJsonRepair.Repair(cleaned);
            return Attempt(repaired);
        }
        catch
        {
            return null;
        }
    }

    private static string SystemPrompt(string personaName) => $$"""
You are a salience classifier for {{personaName}}, a persona who is mid-utterance
in a group chat. A new message just arrived. Decide how strongly the new message
should interrupt or redirect what {{personaName}} is saying right now.

Output a JSON object:
- salience: number between 0.0 and 1.0
  - 0.0 = the new message is unrelated; let {{personaName}} finish.
  - 0.3 = tangentially related; safe to continue.
  - 0.7 = directly relevant; finishing the current message will look out of touch.
  - 1.0 = makes the in-flight message wrong, contradictory, or rude to ship.
- kind: one of
  - "redirect"   — new message changes topic in a way that makes the in-flight reply odd
  - "contradict" — new message contradicts the in-flight reply's premise
  - "tangent"    — new message is on-topic but doesn't break the in-flight reply
  - "irrelevant" — new message is unrelated to the in-flight reply

JSON only. No prose.
""";

    private static string UserPrompt(
        string personaName,
        string gutReaction,
        string wouldSay,
        string generatedSoFar,
        string newMessageContent,
        string newMessageSender) => $"""
# In-flight utterance from {personaName}
Gut reaction: {Trim(gutReaction)}
Planned message: {Trim(wouldSay)}
Generated so far: {Trim(generatedSoFar)}

# New message just arrived
From: {newMessageSender}
Content: {Trim(newMessageContent)}

# Your turn
JSON only.
""";

    private static string Trim(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(empty)" : s.Trim();

    private readonly record struct SalienceParsed
    {
        [JsonPropertyName("salience")]
        public double Salience { get; init; }

        [JsonPropertyName("kind")]
        public string Kind { get; init; }
    }
}

/// <summary>
/// Result of <see cref="PersonaSalienceService.ScoreAsync"/>. <see cref="Value"/> is the
/// numeric salience used by the race math; <see cref="Kind"/> is logged for telemetry
/// and may drive future tuning.
/// </summary>
public readonly record struct SalienceScore(double Value, string Kind)
{
    /// <summary>Conservative fallback for routing/parse/exception failures: do not cancel.</summary>
    public static SalienceScore LetItRide { get; } = new(0.0, "irrelevant");
}
