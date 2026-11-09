using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenAI.Chat;
using PartyTown.Observability;
using PartyTown.Services.SystemOne;

namespace PartyTown.Grains.Generation;

public static class LlmEndpointGrainUtils
{
    /// <summary>Failures that won't fix themselves on retry: bad key, no credit, unknown model, host down.</summary>
    public static bool IsHardFailure(Exception ex) => ex switch
    {
        // The SDK's retry policy aggregates connection failures, each a status-less ClientResultException.
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsHardFailure),
        System.ClientModel.ClientResultException { Status: 401 or 402 or 403 or 404 } => true,
        System.ClientModel.ClientResultException { InnerException: HttpRequestException } => true,
        HttpRequestException => true,
        Anthropic.Exceptions.AnthropicUnauthorizedException or Anthropic.Exceptions.AnthropicForbiddenException or Anthropic.Exceptions.AnthropicIOException => true,
        // Claude reports an empty balance as a 400, not a 402.
        Anthropic.Exceptions.AnthropicBadRequestException { ApiMessage: { } message } => message.Contains("credit balance", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>The provider said slow down (a free model's quota, a busy key): it will fix itself, just not this minute.</summary>
    public static bool IsRateLimited(Exception ex) => ex switch
    {
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsRateLimited),
        System.ClientModel.ClientResultException { Status: 429 } => true,
        HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests } => true,
        Anthropic.Exceptions.AnthropicRateLimitException => true,
        _ => false,
    };

    /// <summary>Failures that should route callers elsewhere for a while: hard ones and rate limits.</summary>
    public static bool Benches(Exception ex) => IsHardFailure(ex) || IsRateLimited(ex);

    /// <summary>Never reached the server: a down box or a dead Tailnet path, not a refusal.</summary>
    public static bool IsUnreachable(Exception ex) => ex switch
    {
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsUnreachable),
        System.ClientModel.ClientResultException { Status: 0 } => true,
        System.ClientModel.ClientResultException { InnerException: HttpRequestException inner } => IsUnreachable(inner),
        HttpRequestException { StatusCode: null } => true,
        Anthropic.Exceptions.AnthropicIOException => true,
        _ => false,
    };

    private static readonly TimeSpan JudgeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>One System One call, captured like a generation; decisions sit on the beat path, so it gets a short deadline.</summary>
    public static async Task<string> JudgeAsync(
        ILogger logger,
        HttpClient http,
        string url,
        string? apiKey,
        string state,
        string questionsJson,
        Action onFinished,
        IAiObservabilityCapture aiObservability,
        string provider,
        string model,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(JudgeTimeout);
        var sw = Stopwatch.StartNew();
        var response = string.Empty;
        var completed = false;
        Exception? failure = null;

        try
        {
            var request = HttpSystemOneJudge.BuildRequest(model, state, JsonNode.Parse(questionsJson)!);
            response = await HttpSystemOneJudge.PostAsync(http, url, apiKey, request, provider, logger, deadline.Token);
            completed = true;
            return response;
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            var input = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = questionsJson },
                new() { Role = "user", Content = state },
            };
            var generation = new LlmGeneration(input, response, provider, model, baseUrl, Streamed: false)
            {
                Tier = nameof(JobComplexity.Decisions),
                Latency = sw.Elapsed,
                Usage = completed ? JudgeUsage(response) : null,
            };
            await aiObservability.CaptureGenerationAsync(generation.Ended(failure, completed, cancellationToken), CancellationToken.None);
            onFinished();
        }
    }

    /// <summary>The <c>/systemone</c> answer's own token count; null when it has none.</summary>
    public static LlmUsage? JudgeUsage(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            if (!doc.RootElement.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
            long? Count(string name) => usage.TryGetProperty(name, out var n) && n.TryGetInt64(out var v) ? v : null;
            return new LlmUsage(Count("input_tokens"), Count("output_tokens"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Pass a stream through, telling the router when it dies of a hard failure or a rate limit.</summary>
    public static async IAsyncEnumerable<LlmGenerationEvent> ReportingHardFailures(
        IAsyncEnumerable<LlmGenerationEvent> inner,
        Func<Exception, Task> report,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var e = inner.GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool has;
            try
            {
                has = await e.MoveNextAsync();
            }
            catch (Exception ex) when (Benches(ex))
            {
                await report(ex);
                throw;
            }
            if (!has) yield break;
            yield return e.Current;
        }
    }

    public static async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        ILogger logger,
        LlmGenerationJob parameters,
        ChatClient chatClient,
        Action onFinished,
        IAiObservabilityCapture aiObservability,
        string provider,
        string model,
        string baseUrl,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var messages = ToOpenAiChatMessages(parameters);
        var completionOptions = ToChatCompletionOptions(parameters);
        var chunkCount = 0;
        var content = new StringBuilder();
        var completed = false;
        TimeSpan? firstToken = null;
        LlmUsage? usage = null;
        Exception? failure = null;
        string? stopProblem = null;

        try
        {
            for (var attempt = 0; attempt < 2 && chunkCount == 0; attempt++)
            {
                if (attempt > 0) logger.LogWarning("LLM stream produced no content; retrying once");
                await using var updates = chatClient
                    .CompleteChatStreamingAsync(messages, completionOptions, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);
                while (true)
                {
                    // A catch can't wrap a yield, so failures are caught around the read alone.
                    try
                    {
                        if (!await updates.MoveNextAsync()) break;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        throw;
                    }
                    var update = updates.Current;
                    if (update.Usage is { } tokens) usage = UsageOf(tokens);

                    foreach (var part in update.ContentUpdate ?? [])
                    {
                        if (!string.IsNullOrEmpty(part.Text))
                        {
                            chunkCount++;
                            firstToken ??= sw.Elapsed;
                            content.Append(part.Text);
                            yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, part.Text);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(update.RefusalUpdate))
                    {
                        logger.LogWarning("LLM refused request: {Refusal}", update.RefusalUpdate);
                        stopProblem = $"refusal: {update.RefusalUpdate}";
                        yield return new LlmGenerationEvent(LlmGenerationEvent.GenerationError, update.RefusalUpdate);
                    }

                    if (update.FinishReason is { } finishReason && finishReason != ChatFinishReason.Stop)
                    {
                        logger.LogWarning("LLM finished with non-stop reason: {Reason}", finishReason);
                        stopProblem = $"finish reason: {finishReason}";
                        yield return new LlmGenerationEvent(LlmGenerationEvent.GenerationError, finishReason.ToString());
                        yield break;
                    }
                }
            }

            completed = stopProblem is null;
        }
        finally
        {
            var generation = new LlmGeneration(parameters.Messages, content.ToString(), provider, model, baseUrl, Streamed: true)
            {
                Temperature = parameters.ModelParameters?.Temperature,
                Tier = parameters.JobComplexity.ToString(),
                Latency = sw.Elapsed,
                TimeToFirstToken = firstToken,
                Usage = usage,
            };
            generation = stopProblem is not null
                ? generation with { Error = stopProblem }
                : generation.Ended(failure, completed, cancellationToken);
            await aiObservability.CaptureGenerationAsync(generation, CancellationToken.None);
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
        IAiObservabilityCapture aiObservability,
        string provider,
        string model,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var messages = ToOpenAiChatMessages(parameters);
        var completionOptions = ToChatCompletionOptions(parameters);
        var text = string.Empty;
        var completed = false;
        LlmUsage? usage = null;
        Exception? failure = null;

        try
        {
            // Cheap models occasionally return an empty completion; one retry recovers nearly all of them.
            for (var attempt = 0; attempt < 2 && string.IsNullOrWhiteSpace(text); attempt++)
            {
                if (attempt > 0) logger.LogWarning("LLM one-shot returned empty content; retrying once");
                var completion = await chatClient.CompleteChatAsync(messages, completionOptions, cancellationToken);
                text = string.Concat(completion.Value.Content.Select(part => part.Text ?? string.Empty));
                if (completion.Value.Usage is { } tokens) usage = UsageOf(tokens);

                if (completion.Value.FinishReason != ChatFinishReason.Stop)
                    logger.LogWarning("LLM one-shot finished with non-stop reason: {Reason}", completion.Value.FinishReason);
            }

            completed = true;
            return text;
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            var generation = new LlmGeneration(parameters.Messages, text, provider, model, baseUrl, Streamed: false)
            {
                Temperature = parameters.ModelParameters?.Temperature,
                Tier = parameters.JobComplexity.ToString(),
                Latency = sw.Elapsed,
                Usage = usage,
            };
            await aiObservability.CaptureGenerationAsync(generation.Ended(failure, completed, cancellationToken), CancellationToken.None);
            onFinished();
            sw.Stop();
            logger.LogInformation("LLM one-shot completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }

    private static LlmUsage UsageOf(ChatTokenUsage usage) => new(
        usage.InputTokenCount,
        usage.OutputTokenCount,
        CacheRead: usage.InputTokenDetails?.CachedTokenCount,
        Reasoning: usage.OutputTokenDetails?.ReasoningTokenCount);

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
