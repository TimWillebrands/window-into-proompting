using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using PartyTown.Configuration;
using PartyTown.Logging;

namespace PartyTown.Grains.Generation;

public class OllamaEndpointGrain(
    IOptions<LlmOptions> llmOptions,
    ILogger<OllamaEndpointGrain> logger
) : Grain, IOllamaEndpointGrain
{
    private int ProviderGrainIndex => Convert.ToInt32(this.GetGrainId().GetIntegerKey());
    private string ProviderDescription => $"ollama[{Options.BaseUrl}]";
    private OllamaOptions Options
    {
        get
        {
            var options = llmOptions.Value.Providers[ProviderGrainIndex];
            if (options is OllamaOptions ollamaOptions)
            {
                return ollamaOptions;
            }
            throw new InvalidOperationException($"Provider {ProviderGrainIndex} is not an Ollama provider");
        }
    }

    private OpenAIClientOptions OpenAiOptions => new()
    {
        Endpoint = new Uri($"{Options.BaseUrl.TrimEnd('/')}/v1")
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
        var modelClient = new OpenAIModelClient(new ApiKeyCredential("ollama"), OpenAiOptions);

        try
        {
            logger.LogDebug("Fetching models from Ollama");

            var response = await modelClient.GetModelsAsync(cancellationToken);
            var list = response.Value
                .Select(item => new LlmModel
                {
                    Name = item.Id,
                    EndpointProviderGrainId = ProviderGrainIndex,
                    ProviderType = "ollama",
                    ProviderDescription = ProviderDescription,
                    Description = $"Ollama model {item.Id}",
                })
                .ToList();

            logger.LogDebug("Fetched {Count} models from Ollama", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaEndpointGrain failed to list models");
            return [];
        }
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

    private static bool TryGetJsonSchemaResponseFormat(JsonObject? responseFormatNode, out ChatResponseFormat responseFormat)
    {
        responseFormat = null!;
        if (responseFormatNode is null)
        {
            return false;
        }

        var formatType = responseFormatNode["type"]?.GetValue<string>();
        if (!string.Equals(formatType, "json_schema", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (responseFormatNode["json_schema"] is not JsonObject schemaRoot)
        {
            return false;
        }

        var name = schemaRoot["name"]?.GetValue<string>();
        var schema = schemaRoot["schema"];
        if (string.IsNullOrWhiteSpace(name) || schema is null)
        {
            return false;
        }

        var strict = schemaRoot["strict"]?.GetValue<bool>();

        responseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            name,
            BinaryData.FromString(schema.ToJsonString()),
            jsonSchemaIsStrict: strict);

        return true;
    }
}
