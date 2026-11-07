using System.Diagnostics;
using PartyTown.Grains.Generation;
using PostHog;

namespace PartyTown.Observability;

/// <summary>Tokens one LLM call used; null when the provider didn't say.</summary>
public sealed record LlmUsage(long? Input, long? Output, long? CacheRead = null, long? CacheWrite = null, long? Reasoning = null);

/// <summary>One LLM call as PostHog's <c>$ai_generation</c>; unset values are left out of the event.</summary>
public sealed record LlmGeneration(
    IReadOnlyList<LlmChatMessage> Input,
    string Output,
    string Provider,
    string Model,
    string BaseUrl,
    bool Streamed)
{
    public double? Temperature { get; init; }
    /// <summary>The job tier, shown as the span name.</summary>
    public string? Tier { get; init; }
    public TimeSpan? Latency { get; init; }
    public TimeSpan? TimeToFirstToken { get; init; }
    public LlmUsage? Usage { get; init; }
    /// <summary>Why the call failed; null when it succeeded or was cancelled.</summary>
    public string? Error { get; init; }
    public int? HttpStatus { get; init; }
    /// <summary>The caller stopped it (a lost race, a closed room); not an error.</summary>
    public bool Cancelled { get; init; }

    /// <summary>How a call ended: cancelled by its caller, failed with a reason, or done.</summary>
    public LlmGeneration Ended(Exception? failure, bool completed, CancellationToken cancellationToken)
    {
        if (completed) return this;
        // No failure but not done: the caller stopped reading. A cancel it didn't ask for is a timeout.
        if (failure is null || cancellationToken.IsCancellationRequested) return this with { Cancelled = true };
        return this with
        {
            Error = failure is OperationCanceledException ? "timed out" : failure.Message,
            HttpStatus = StatusOf(failure),
        };
    }

    private static int? StatusOf(Exception ex) => ex switch
    {
        System.ClientModel.ClientResultException { Status: > 0 } result => result.Status,
        HttpRequestException { StatusCode: { } status } => (int)status,
        AggregateException aggregate => aggregate.InnerExceptions.Select(StatusOf).FirstOrDefault(s => s is not null),
        _ => null,
    };
}

public interface IAiObservabilityCapture
{
    Task CaptureGenerationAsync(LlmGeneration generation, CancellationToken cancellationToken = default);
}

/// <summary>Queues manual AI Observability generations on the shared PostHog client; off with it, and never blocks an LLM call.</summary>
public sealed class AiObservabilityCapture(IPostHogClient posthog) : IAiObservabilityCapture
{
    public Task CaptureGenerationAsync(LlmGeneration generation, CancellationToken cancellationToken = default)
    {
        var activity = Activity.Current;
        var sessionId = SessionId(activity) ?? $"process-{Environment.ProcessId}";
        var traceId = activity?.TraceId.ToString();
        if (string.IsNullOrWhiteSpace(traceId))
            traceId = Guid.NewGuid().ToString("N");

        var properties = new Dictionary<string, object>
        {
            // Personas generate on their own, so no human to attribute this to.
            ["$process_person_profile"] = false,
            ["$ai_trace_id"] = traceId,
            ["$ai_session_id"] = sessionId,
            ["$ai_model"] = generation.Model,
            ["$ai_provider"] = generation.Provider,
            ["$ai_input"] = generation.Input.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
            ["$ai_output_choices"] = string.IsNullOrEmpty(generation.Output)
                ? Array.Empty<object>()
                : new object[] { new { role = "assistant", content = generation.Output } },
            ["$ai_base_url"] = generation.BaseUrl,
            ["$ai_stream"] = generation.Streamed,
            ["$ai_is_error"] = generation.Error is not null,
        };
        Add(properties, "$ai_temperature", generation.Temperature);
        Add(properties, "$ai_span_name", generation.Tier);
        Add(properties, "$ai_latency", generation.Latency?.TotalSeconds);
        Add(properties, "$ai_time_to_first_token", generation.TimeToFirstToken?.TotalSeconds);
        Add(properties, "$ai_input_tokens", generation.Usage?.Input);
        Add(properties, "$ai_output_tokens", generation.Usage?.Output);
        Add(properties, "$ai_cache_read_input_tokens", generation.Usage?.CacheRead);
        Add(properties, "$ai_cache_creation_input_tokens", generation.Usage?.CacheWrite);
        Add(properties, "$ai_reasoning_tokens", generation.Usage?.Reasoning);
        Add(properties, "$ai_error", generation.Error);
        Add(properties, "$ai_http_status", generation.HttpStatus);
        if (generation.Cancelled) properties["cancelled"] = true;

        posthog.Capture(sessionId, "$ai_generation", properties);
        return Task.CompletedTask;
    }

    private static void Add(Dictionary<string, object> properties, string key, object? value)
    {
        if (value is not null) properties[key] = value;
    }

    private static string? SessionId(Activity? activity)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (current.GetTagItem("chat_group.id") is { } chatGroupId)
                return $"chat-{chatGroupId}";
        }

        return null;
    }
}
