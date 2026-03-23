using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Configuration;
using PartyTown.Logging;

namespace PartyTown.Grains.Generation;

public class OpenRouterEndpointGrain(
    IOptions<LlmOptions> llmOptions,
    IHttpClientFactory httpClientFactory,
    ILogger<OpenRouterEndpointGrain> logger
) : Grain, IOpenRouterEndpointGrain
{
    private const string BaseUrl = "https://openrouter.ai/api/v1";

    private int GrainIndex => (int)this.GetPrimaryKeyLong();
    private LlmProviderConfig Config => llmOptions.Value.Providers
        .Where(p => p.Type == "openrouter")
        .ElementAt(GrainIndex);
    private string ProviderDescription => "openrouter";

    public async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationParams parameters,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var _ = logger.BeginGenerationScope(parameters.Model, ProviderDescription);
        logger.LogDebug("LLM API call starting: {Model}", parameters.Model);
        var sw = Stopwatch.StartNew();

        var chatClient = new ChatClient(
            parameters.Model,
            new ApiKeyCredential(Config.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(BaseUrl) });

        var messages = ToOpenAiChatMessages(parameters);
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
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        const string url = "https://openrouter.ai/api/frontend/models/find?context=128000&fmt=cards&input_modalities=text&max_price=0&order=top-weekly";

        try
        {
            logger.LogDebug("Fetching models from OpenRouter");

            using var httpClient = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var models = new List<LlmModel>();

            if (!document.RootElement.TryGetProperty("data", out var dataNode) ||
                !dataNode.TryGetProperty("models", out var modelsNode) ||
                modelsNode.ValueKind != JsonValueKind.Array)
            {
                return models;
            }

            foreach (var modelNode in modelsNode.EnumerateArray())
            {
                if (!modelNode.TryGetProperty("endpoint", out var endpointNode) ||
                    !endpointNode.TryGetProperty("model_variant_permaslug", out var idNode))
                {
                    continue;
                }

                var id = idNode.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var name = modelNode.TryGetProperty("short_name", out var nameNode)
                    ? nameNode.GetString() ?? id
                    : id;

                var description = modelNode.TryGetProperty("description", out var descNode)
                    ? descNode.GetString()
                    : null;

                int? contextLength = null;
                if (modelNode.TryGetProperty("context_length", out var contextNode) && contextNode.TryGetInt32(out var ctx))
                {
                    contextLength = ctx;
                }

                models.Add(new LlmModel
                {
                    Name = id,
                    EndpointProviderGrainId = GrainIndex,
                    ProviderType = "openrouter",
                    ProviderDescription = ProviderDescription,
                    Description = description ?? $"OpenRouter model {name}",
                    ContextLength = contextLength,
                });
            }

            logger.LogDebug("Fetched {Count} models from OpenRouter", models.Count);
            return models;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenRouterEndpointGrain failed to list models");
            return [];
        }
    }

    private static IEnumerable<ChatMessage> ToOpenAiChatMessages(LlmGenerationParams parameters)
    {
        return parameters.Messages.Select(msg =>
        {
            ChatMessage mapped = msg.Role switch
            {
                "system" => new SystemChatMessage(msg.Content) { ParticipantName = msg.Name },
                "assistant" => new AssistantChatMessage(msg.Content) { ParticipantName = msg.Name },
                _ => new UserChatMessage(msg.Content) { ParticipantName = msg.Name }
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
