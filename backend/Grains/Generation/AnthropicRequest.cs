using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Core;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace PartyTown.Grains.Generation;

/// <summary>Maps a provider-neutral job onto the Claude Messages API.</summary>
public static class AnthropicRequest
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    public const int OneShotMaxTokens = 16000;
    public const int StreamMaxTokens = 64000;

    /// <summary>Marks a later system message, which Claude only takes as a user turn on every model.</summary>
    public const string DirectionPrefix = "[Stage direction for you, not said aloud]\n";

    private const string ContinueTurn = "Continue.";

    private static readonly string[] UnsupportedSchemaKeywords =
    {
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "minLength", "maxLength",
    };

    public sealed record Turn(string Role, string Content);

    /// <summary>Leading system → top-level system; later system → direction; roles merged; always starts and ends on user.</summary>
    public static (string? System, IReadOnlyList<Turn> Turns) ToTurns(IReadOnlyList<LlmChatMessage> messages)
    {
        var leading = messages.TakeWhile(m => m.Role == "system").Select(m => m.Content).ToList();
        var turns = new List<Turn>();
        foreach (var message in messages.Skip(leading.Count).Where(m => !string.IsNullOrWhiteSpace(m.Content)))
        {
            var role = message.Role == "assistant" ? "assistant" : "user";
            var content = message.Role == "system" ? DirectionPrefix + message.Content : message.Content;
            if (turns.Count > 0 && turns[^1].Role == role)
                turns[^1] = turns[^1] with { Content = $"{turns[^1].Content}\n\n{content}" };
            else
                turns.Add(new Turn(role, content));
        }

        // A leading assistant turn is rejected and a trailing one reads as a prefill, a 400 on current models.
        if (turns.Count == 0 || turns[0].Role == "assistant") turns.Insert(0, new Turn("user", ContinueTurn));
        if (turns[^1].Role == "assistant") turns.Add(new Turn("user", ContinueTurn));

        var system = string.Join("\n\n", leading.Where(s => !string.IsNullOrWhiteSpace(s)));
        return (system.Length == 0 ? null : system, turns);
    }

    /// <summary>The job's JSON schema in the subset structured outputs accepts, or null when it has none.</summary>
    public static JsonObject? ToSchema(string? responseFormatJson)
    {
        if (string.IsNullOrWhiteSpace(responseFormatJson)) return null;
        if (JsonNode.Parse(responseFormatJson) is not JsonObject format) return null;
        if (!string.Equals(format["type"]?.GetValue<string>(), "json_schema", StringComparison.OrdinalIgnoreCase)) return null;
        if (format["json_schema"]?["schema"] is not JsonObject schema) return null;

        var copy = schema.DeepClone().AsObject();
        Normalise(copy);
        return copy;
    }

    private static void Normalise(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array) Normalise(item);
            return;
        }
        if (node is not JsonObject schema) return;

        foreach (var keyword in UnsupportedSchemaKeywords) schema.Remove(keyword);

        if (schema["type"] is JsonArray types)
        {
            var anyOf = new JsonArray(types.Select(t => (JsonNode?)new JsonObject { ["type"] = t?.GetValue<string>() }).ToArray());
            schema.Remove("type");
            schema["anyOf"] = anyOf;
        }

        if (schema["properties"] is not null || schema["type"]?.GetValue<string>() == "object")
            schema["additionalProperties"] = false;

        foreach (var (_, child) in schema.ToList()) Normalise(child);
    }

    /// <summary>Temperature is never sent: the API version this SDK speaks rejects sampling parameters.</summary>
    public static MessageCreateParams Build(LlmGenerationJob job, string model, string? effort, int maxTokens)
    {
        var (system, turns) = ToTurns(job.Messages);
        var schema = ToSchema(job.ResponseFormat ?? job.ModelParameters?.ResponseFormat);

        // Optional fields stay unset: assigning null serialises an explicit null, which the API rejects.
        var request = new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = turns.Select(ToMessage).ToList(),
        };
        if (system is not null) request = request with { System = system };
        if (OutputConfigFor(effort, schema) is { } output) request = request with { OutputConfig = output };
        if (HasRefusalFallback(model))
            request = request with
            {
                Betas = new List<ApiEnum<string, AnthropicBeta>> { AnthropicBeta.ServerSideFallback2026_07_01 },
                Fallbacks = new Default(),
            };
        return request;
    }

    private static BetaMessageParam ToMessage(Turn turn) => turn.Role == "assistant"
        ? new BetaAssistantMessageParam { Content = turn.Content }
        : new BetaUserMessageParam { Content = turn.Content };

    /// <summary>Haiku has no server-side refusal fallback.</summary>
    public static bool HasRefusalFallback(string model) => !model.Contains("haiku", StringComparison.OrdinalIgnoreCase);

    private static BetaOutputConfig? OutputConfigFor(string? effort, JsonObject? schema)
    {
        Effort? level = effort switch
        {
            "low" => Effort.Low,
            "medium" => Effort.Medium,
            "high" => Effort.High,
            _ => null,
        };
        if (level is null && schema is null) return null;

        var config = new BetaOutputConfig();
        if (level is { } set) config = config with { Effort = set };
        if (schema is not null)
            config = config with
            {
                Format = new BetaJsonOutputFormat
                {
                    Schema = schema.ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value)),
                },
            };
        return config;
    }
}
