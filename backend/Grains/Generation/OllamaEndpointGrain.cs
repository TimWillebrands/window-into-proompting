using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Configuration;
using PartyTown.Logging;

namespace PartyTown.Grains.Generation;

public class OllamaEndpointGrain(
    IOptions<LlmOptions> llmOptions,
    IHttpClientFactory httpClientFactory,
    ILogger<OllamaEndpointGrain> logger
) : Grain, IOllamaEndpointGrain
{
    private int GrainIndex => (int)this.GetPrimaryKeyLong();
    private LlmProviderConfig Config => llmOptions.Value.Providers
        .Where(p => p.Type == "ollama")
        .ElementAt(GrainIndex);
    private string ProviderDescription => $"ollama[{Config.BaseUrl}]";

    private OpenAIClientOptions OpenAiOptions => new()
    {
        Endpoint = new Uri($"{Config.BaseUrl.TrimEnd('/')}/v1")
    };

    public async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationParams parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var _ = logger.BeginGenerationScope(parameters.Model, ProviderDescription);
        logger.LogDebug("LLM API call starting: {Model}", parameters.Model);
        var sw = Stopwatch.StartNew();

        var chatClient = new ChatClient(
            parameters.Model,
            new ApiKeyCredential("ollama"),
            OpenAiOptions);

        IEnumerable<ChatMessage> messages = ToOpenAiChatMessages(parameters);

        var completionOptions = ToChatCompletionOptions(parameters);

        var chunkCount = 0;

        await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, completionOptions, cancellationToken))
        {
            foreach (var part in update.ContentUpdate ?? [])
            {
                if (!string.IsNullOrWhiteSpace(part.Text))
                {
                    chunkCount++;
                    yield return new LlmGenerationEvent("message", part.Text);
                }
            }

            if (!string.IsNullOrWhiteSpace(update.RefusalUpdate))
            {
                logger.LogWarning("LLM refused request: {Refusal}", update.RefusalUpdate);
                yield return new LlmGenerationEvent("error", update.RefusalUpdate);
            }

            if (update.FinishReason is { } finishReason && finishReason != ChatFinishReason.Stop)
            {
                logger.LogWarning("LLM finished with non-stop reason: {Reason}", finishReason);
                yield return new LlmGenerationEvent("error", finishReason.ToString());
                yield break;
            }
        }

        sw.Stop();
        logger.LogInformation("LLM API call completed: {Model} in {ElapsedMs}ms", parameters.Model, sw.ElapsedMilliseconds);
        logger.LogDebug("Received {ChunkCount} chunks", chunkCount);

        yield break;
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching models from Ollama at {BaseUrl}", Config.BaseUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var http = httpClientFactory.CreateClient();
            var url = $"{Config.BaseUrl.TrimEnd('/')}/api/tags";
            var httpResponse = await http.GetAsync(url, cts.Token);
            httpResponse.EnsureSuccessStatusCode();

            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("Ollama at {BaseUrl} returned empty response for /api/tags", Config.BaseUrl);
                return [];
            }

            var response = JsonSerializer.Deserialize<OllamaTagsResponse>(content);

            var list = (response?.Models ?? [])
                .Select(item => new LlmModel
                {
                    Name = item.Name,
                    EndpointProviderGrainId = GrainIndex,
                    ProviderType = "ollama",
                    ProviderDescription = ProviderDescription,
                    Description = $"Ollama model {item.Name}",
                })
                .ToList();

            logger.LogDebug("Fetched {Count} models from Ollama", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaEndpointGrain failed to list models from {BaseUrl}", Config.BaseUrl);
            return [];
        }
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelEntry> Models { get; init; } = [];
    }

    private sealed class OllamaModelEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }

    private static IEnumerable<ChatMessage> ToOpenAiChatMessages(LlmGenerationParams parameters)
    {
        return parameters.Messages.Select(msg =>
        {
            ChatMessage mapped = msg.Role switch
            {
                "system" => new SystemChatMessage(msg.Content)
                {
                    ParticipantName = msg.Name
                },
                "assistant" => new AssistantChatMessage(msg.Content)
                {
                    ParticipantName = msg.Name
                },
                _ => new UserChatMessage(msg.Content)
                {
                    ParticipantName = msg.Name
                }
            };
            return mapped;
        });
    }

    private static ChatCompletionOptions ToChatCompletionOptions(LlmGenerationParams parameters)
    {
        var options = new ChatCompletionOptions
        {
            EndUserId = parameters.UserId,
            Temperature = parameters.Temperature is null ? null : (float)parameters.Temperature.Value
        };

        if (TryGetJsonSchemaResponseFormat(parameters.ResponseFormat, out var responseFormat))
        {
            options.ResponseFormat = responseFormat;
        }

        return options;
    }

    private static bool TryGetJsonSchemaResponseFormat(string? responseFormatJson, out ChatResponseFormat responseFormat)
    {
        responseFormat = null!;
        if (string.IsNullOrWhiteSpace(responseFormatJson)) return false;

        var responseFormatNode = JsonNode.Parse(responseFormatJson) as JsonObject;
        if (responseFormatNode is null) return false;

        var formatType = responseFormatNode["type"]?.GetValue<string>();
        if (!string.Equals(formatType, "json_schema", StringComparison.OrdinalIgnoreCase)) return false;

        if (responseFormatNode["json_schema"] is not JsonObject schemaRoot) return false;

        var name = schemaRoot["name"]?.GetValue<string>();
        var schema = schemaRoot["schema"];
        if (string.IsNullOrWhiteSpace(name) || schema is null) return false;

        var strict = schemaRoot["strict"]?.GetValue<bool>();
        responseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            name,
            BinaryData.FromString(schema.ToJsonString()),
            jsonSchemaIsStrict: strict);

        return true;
    }
}
