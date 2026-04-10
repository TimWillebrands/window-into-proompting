using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using OpenAI.Chat;

namespace PartyTown.Grains.Generation;

public static class LlmEndpointGrainUtils
{
    /// <summary>
    /// Streams LLM generation events produced by the provided ChatClient for the given generation job.
    /// </summary>
    /// <param name="logger">Logger used for informational and warning messages.</param>
    /// <param name="parameters">The generation job containing messages and model parameters.</param>
    /// <param name="chatClient">The chat client used to request streaming completions.</param>
    /// <param name="cancellationToken">Token to cancel the async enumeration.</param>
    /// <returns>
    /// An async sequence of <see cref="LlmGenerationEvent"/> items containing emitted content chunks and error events.
    /// The sequence yields content chunk events for received text parts and yields generation error events when the model refuses the request or finishes with a non-Stop reason (in which case the sequence terminates immediately).
    /// </returns>
    public static async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        ILogger logger,
        LlmGenerationJob parameters,
        ChatClient chatClient,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

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

        sw.Stop();
        logger.LogInformation("LLM API call completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        logger.LogDebug("Received {ChunkCount} chunks", chunkCount);
    }

    /// <summary>
    /// Converts the job's message list into OpenAI-compatible chat messages.
    /// </summary>
    /// <param name="parameters">The generation job containing a sequence of messages with role, content, and name.</param>
    /// <returns>An enumerable of <see cref="ChatMessage"/> where each input message is mapped by role: "system" → <see cref="SystemChatMessage"/>, "assistant" → <see cref="AssistantChatMessage"/>, and all other roles → <see cref="UserChatMessage"/>; each produced message has <see cref="ChatMessage.ParticipantName"/> set to the input message's name.</returns>
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

    /// <summary>
    /// Constructs a <see cref="ChatCompletionOptions"/> based on the job's model parameters.
    /// </summary>
    /// <param name="parameters">The generation job whose <see cref="LlmGenerationJob.ModelParameters"/> are used to populate options.</param>
    /// <returns>
    /// A <see cref="ChatCompletionOptions"/> with <see cref="ChatCompletionOptions.Temperature"/> set from the model parameters (or null if unspecified) and <see cref="ChatCompletionOptions.ResponseFormat"/> configured when a valid JSON-schema response format is provided.
    /// </returns>
    private static ChatCompletionOptions ToChatCompletionOptions(LlmGenerationJob parameters)
    {
        var mp = parameters.ModelParameters;
        var options = new ChatCompletionOptions
        {
            Temperature = mp?.Temperature is null ? null : (float)mp.Temperature.Value
        };

        if (TryGetJsonSchemaResponseFormat(mp?.ResponseFormat, out var responseFormat))
        {
            options.ResponseFormat = responseFormat;
        }

        return options;
    }

    /// <summary>
    /// Parses a JSON response-format specification and, when it denotes a `json_schema` format, creates a corresponding `ChatResponseFormat`.
    /// </summary>
    /// <param name="responseFormatJson">A JSON string describing the response format. Expected shape: a top-level object with `"type": "json_schema"` and a `"json_schema"` object containing a required `"name"` (string) and `"schema"` (any JSON value); an optional `"strict"` (boolean) may also be present.</param>
    /// <param name="responseFormat">When the method returns `true`, contains the constructed `ChatResponseFormat` for the specified JSON schema.</param>
    /// <returns>`true` if the input was a valid `json_schema` definition and `responseFormat` was created; `false` otherwise.</returns>
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
