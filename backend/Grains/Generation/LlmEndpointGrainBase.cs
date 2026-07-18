using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using OpenAI.Chat;

namespace PartyTown.Grains.Generation;

public static class LlmEndpointGrainUtils
{
    public static async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        ILogger logger,
        LlmGenerationJob parameters,
        ChatClient chatClient,
        Action onFinished,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var messages = ToOpenAiChatMessages(parameters);
        var completionOptions = ToChatCompletionOptions(parameters);
        var chunkCount = 0;

        try
        {
            await foreach (var update in chatClient.CompleteChatStreamingAsync(messages, completionOptions, cancellationToken))
            {
                foreach (var part in update.ContentUpdate ?? [])
                {
                    if (!string.IsNullOrWhiteSpace(part.Text))
                    {
                        chunkCount++;
                        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, part.Text);
                    }
                }

                if (!string.IsNullOrWhiteSpace(update.RefusalUpdate))
                {
                    logger.LogWarning("LLM refused request: {Refusal}", update.RefusalUpdate);
                    yield return new LlmGenerationEvent(LlmGenerationEvent.GenerationError, update.RefusalUpdate);
                }

                if (update.FinishReason is { } finishReason && finishReason != ChatFinishReason.Stop)
                {
                    logger.LogWarning("LLM finished with non-stop reason: {Reason}", finishReason);
                    yield return new LlmGenerationEvent(LlmGenerationEvent.GenerationError, finishReason.ToString());
                    yield break;
                }
            }
        }
        finally
        {
            onFinished();
            sw.Stop();
            logger.LogInformation("LLM API call completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            logger.LogDebug("Received {ChunkCount} chunks", chunkCount);
        }
    }

    public static async Task<string> CompleteOneShotAsync(
        ILogger logger,
        LlmGenerationJob parameters,
        ChatClient chatClient,
        Action onFinished,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var messages = ToOpenAiChatMessages(parameters);
        var completionOptions = ToChatCompletionOptions(parameters);

        try
        {
            var completion = await chatClient.CompleteChatAsync(messages, completionOptions, cancellationToken);
            var text = string.Concat(completion.Value.Content.Select(part => part.Text ?? string.Empty));

            if (completion.Value.FinishReason != ChatFinishReason.Stop)
                logger.LogWarning("LLM one-shot finished with non-stop reason: {Reason}", completion.Value.FinishReason);

            return text;
        }
        finally
        {
            onFinished();
            sw.Stop();
            logger.LogInformation("LLM one-shot completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }

    private static IEnumerable<ChatMessage> ToOpenAiChatMessages(LlmGenerationJob parameters)
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

    private static ChatCompletionOptions ToChatCompletionOptions(LlmGenerationJob parameters)
    {
        var mp = parameters.ModelParameters;
        var options = new ChatCompletionOptions
        {
            Temperature = mp?.Temperature is null ? null : (float)mp.Temperature.Value
        };

        // ResponseFormat can be set on either LlmGenerationJob (top-level, the modern
        // shape used by structured-output callers like PersonaDecisionService and the
        // import scene map) or LlmModelParameters (legacy). Top-level takes precedence.
        // Historical footgun: pre-fix, only the nested ModelParameters slot was wired
        // through, so all callers using the top-level field had their JSON schemas
        // silently dropped — output happened to mostly parse because JsonRepair caught
        // it, but strict schemas weren't actually enforced.
        var responseFormatJson = parameters.ResponseFormat ?? mp?.ResponseFormat;
        if (TryGetJsonSchemaResponseFormat(responseFormatJson, out var responseFormat))
        {
            options.ResponseFormat = responseFormat;
        }

        return options;
    }

    private static bool TryGetJsonSchemaResponseFormat(string? responseFormatJson, out ChatResponseFormat responseFormat)
    {
        responseFormat = null!;
        if (string.IsNullOrWhiteSpace(responseFormatJson)) return false;

        if (JsonNode.Parse(responseFormatJson) is not JsonObject responseFormatNode) return false;

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
