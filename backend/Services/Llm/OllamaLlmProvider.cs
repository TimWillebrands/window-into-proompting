using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Models;
using PartyTown.Configuration;
using PartyTown.Logging;
using System.ClientModel;

namespace PartyTown.Services.Llm;

public sealed class OllamaLlmProvider(IOptions<LlmOptions> options, ILogger<OllamaLlmProvider> logger) : ILlmProvider
{
    private readonly LlmOptions _options = options.Value;

    public string Id => "ollama";

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri($"{_options.OllamaBaseUrl.TrimEnd('/')}/v1")
        };

        var modelClient = new OpenAIModelClient(new ApiKeyCredential("ollama"), options);

        try
        {
            logger.LogDebug("Fetching models from Ollama");

            var response = await modelClient.GetModelsAsync(cancellationToken);
            var list = response.Value
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new LlmModel
                {
                    Id = item.Id,
                    Name = item.Id,
                    Provider = "ollama",
                    Description = $"Ollama model {item.Id}"
                })
                .ToList();

            logger.LogDebug("Fetched {Count} models from Ollama", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaLlmProvider failed to list models");
            return [];
        }
    }

    public async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationParams parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using (logger.BeginGenerationScope(parameters.Model, Id))
        {
            logger.LogDebug("LLM API call starting: {Model}", parameters.Model);
            var sw = Stopwatch.StartNew();

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri($"{_options.OllamaBaseUrl.TrimEnd('/')}/v1")
            };

            var chatClient = new ChatClient(
                parameters.Model,
                new ApiKeyCredential("ollama"),
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
