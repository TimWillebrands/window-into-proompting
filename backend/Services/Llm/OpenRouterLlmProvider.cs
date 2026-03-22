using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Configuration;
using PartyTown.Logging;
using System.ClientModel;

namespace PartyTown.Services.Llm;

public sealed class OpenRouterLlmProvider(
    IOptions<LlmOptions> options,
    OpenRouterModelsClient modelsClient,
    ILogger<OpenRouterLlmProvider> logger) : ILlmProvider
{
    private readonly LlmOptions _options = options.Value;

    public string Id => "openrouter";

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Fetching models from OpenRouter");
        var models = await modelsClient.LoadFreeModelsAsync(cancellationToken);
        logger.LogDebug("Fetched {Count} models from OpenRouter", models.Count);
        return models;
    }

    public async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationParams parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using (logger.BeginGenerationScope(parameters.Model, Id))
        {
            var apiKey = _options.OpenRouterApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                logger.LogError("OPENROUTER_API_KEY is not configured");
                throw new InvalidOperationException("OPENROUTER_API_KEY is not configured.");
            }

            logger.LogDebug("LLM API call starting: {Model}", parameters.Model);
            var sw = Stopwatch.StartNew();

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri("https://openrouter.ai/api/v1")
            };

            var chatClient = new ChatClient(
                parameters.Model,
                new ApiKeyCredential(apiKey),
                clientOptions);

            var messages = OpenAiChatMapper.ToChatMessages(parameters.Messages);
            var completionOptions = OpenAiChatMapper.ToChatCompletionOptions(parameters);

            var chunkCount = 0;

            await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, completionOptions, cancellationToken))
            {
                foreach (var part in update.ContentUpdate ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(part.Text))
                    {
                        chunkCount++;
                        yield return new LlmGenerationEvent { Type = "message", Data = part.Text };
                    }
                }

                if (!string.IsNullOrWhiteSpace(update.RefusalUpdate))
                {
                    logger.LogWarning("LLM refused request: {Refusal}", update.RefusalUpdate);
                    yield return new LlmGenerationEvent { Type = "error", Data = update.RefusalUpdate };
                }

                if (update.FinishReason is { } finishReason && finishReason != ChatFinishReason.Stop)
                {
                    logger.LogWarning("LLM finished with non-stop reason: {Reason}", finishReason);
                    yield return new LlmGenerationEvent { Type = "error", Data = finishReason.ToString() };
                    yield break;
                }
            }

            sw.Stop();
            logger.LogInformation("LLM API call completed: {Model} in {ElapsedMs}ms", parameters.Model, sw.ElapsedMilliseconds);
            logger.LogDebug("Received {ChunkCount} chunks", chunkCount);

            yield break;
        }
    }
}
