using System.Diagnostics;
using PartyTown.Grains.Generation;
using PostHog;

namespace PartyTown.Observability;

public interface IAiObservabilityCapture
{
    Task CaptureGenerationAsync(
        IReadOnlyList<LlmChatMessage> messages,
        string output,
        string provider,
        string model,
        string baseUrl,
        double? temperature,
        bool streamed,
        bool isError,
        CancellationToken cancellationToken = default);
}

/// <summary>Queues manual AI Observability generations on the shared PostHog client; off with it, and never blocks an LLM call.</summary>
public sealed class AiObservabilityCapture(IPostHogClient posthog) : IAiObservabilityCapture
{
    public Task CaptureGenerationAsync(
        IReadOnlyList<LlmChatMessage> messages,
        string output,
        string provider,
        string model,
        string baseUrl,
        double? temperature,
        bool streamed,
        bool isError,
        CancellationToken cancellationToken = default)
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
            ["$ai_model"] = model,
            ["$ai_provider"] = provider,
            ["$ai_input"] = messages.Select(message => new { role = message.Role, content = message.Content }).ToArray(),
            ["$ai_output_choices"] = string.IsNullOrEmpty(output)
                ? Array.Empty<object>()
                : new object[] { new { role = "assistant", content = output } },
            ["$ai_base_url"] = baseUrl,
            ["$ai_stream"] = streamed,
            ["$ai_is_error"] = isError,
        };
        if (temperature is not null)
            properties["$ai_temperature"] = temperature.Value;

        posthog.Capture(sessionId, "$ai_generation", properties);
        return Task.CompletedTask;
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
