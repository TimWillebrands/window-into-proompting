using System.Diagnostics;
using System.Net.Http.Json;
using PartyTown.Grains.Generation;

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

/// <summary>Captures manual AI Observability generations without affecting LLM request handling.</summary>
public sealed class AiObservabilityCapture(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<AiObservabilityCapture> logger) : IAiObservabilityCapture
{
    public async Task CaptureGenerationAsync(
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
        var projectToken = configuration["POSTHOG_PROJECT_TOKEN"];
        var host = configuration["POSTHOG_HOST"];
        if (string.IsNullOrWhiteSpace(projectToken) || string.IsNullOrWhiteSpace(host))
        {
            if (environment.IsDevelopment())
            {
                var variable = string.IsNullOrWhiteSpace(projectToken) ? "POSTHOG_PROJECT_TOKEN" : "POSTHOG_HOST";
                logger.LogError(
                    "{Variable} variable required by PostHog is missing or un-configured, this causes events to be silently missed. This error stops appearing once {Variable} is configured",
                    variable);
            }

            return;
        }

        try
        {
            var activity = Activity.Current;
            var sessionId = SessionId(activity) ?? $"process-{Environment.ProcessId}";
            var traceId = activity?.TraceId.ToString();
            if (string.IsNullOrWhiteSpace(traceId))
                traceId = Guid.NewGuid().ToString("N");

            var properties = new Dictionary<string, object?>
            {
                ["distinct_id"] = sessionId,
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

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{host.TrimEnd('/')}/i/v0/e/")
            {
                Content = JsonContent.Create(new { api_key = projectToken, @event = "$ai_generation", properties })
            };
            using var response = await httpClientFactory.CreateClient().SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                logger.LogDebug("PostHog AI Observability capture returned {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "PostHog AI Observability capture failed");
        }
    }

    private static string? SessionId(Activity? activity)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            if (current.GetBaggageItem("posthog.ai.session_id") is { Length: > 0 } sessionId)
                return sessionId;
            if (current.GetTagItem("chat_group.id") is { } chatGroupId)
                return $"chat-{chatGroupId}";
        }

        return null;
    }
}
