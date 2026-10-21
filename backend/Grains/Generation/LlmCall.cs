using System.Diagnostics;
using System.Text;
using PartyTown.Model;

namespace PartyTown.Grains.Generation;

/// <summary>A finished streamed call: accumulated text, the endpoint's attribution, the tier that served it.</summary>
public sealed record LlmCallResult(string Content, string Reasoning, ChatMessageMetadata? Attribution, JobComplexity Served);

/// <summary>
/// One LLM call over the router seam: tier fallback, best-effort attribution, stream accumulation.
/// Callers bring the job and their tier order; they no longer route or loop chunks themselves.
/// </summary>
public sealed class LlmCall(ILlmRouterGrain router, ILogger? logger = null)
{
    /// <summary>Route to the first tier with a provider; <paramref name="tiers"/> is preference order.
    /// Throws the last tier's <see cref="InvalidOperationException"/> when none has one.</summary>
    public async Task<(ILlmEndpointGrain Endpoint, JobComplexity Served)> RouteAsync(
        IReadOnlyList<JobComplexity> tiers, CancellationToken ct)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                return (await router.RouteAsync(tiers[i], ct), tiers[i]);
            }
            catch (InvalidOperationException) when (i < tiers.Count - 1)
            {
                logger?.LogDebug("No provider for {Tier}; falling back to {Next}", tiers[i], tiers[i + 1]);
            }
        }
    }

    /// <summary>Route, stream and accumulate. <paramref name="onChunk"/> sees every raw chunk;
    /// the job is re-tagged with the tier that served it; provider/model land on <paramref name="span"/>.</summary>
    public async Task<LlmCallResult> StreamAsync(
        LlmGenerationJob job,
        IReadOnlyList<JobComplexity> tiers,
        CancellationToken ct,
        Func<LlmGenerationEvent, Task>? onChunk = null,
        Activity? span = null)
    {
        var (endpoint, served) = await RouteAsync(tiers, ct);
        if (served != tiers[0]) span?.SetTag("llm.tier_fallback", true);
        var attribution = await AttributionAsync(endpoint, span);

        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        await foreach (var chunk in endpoint.GenerateAsync(job with { JobComplexity = served }, ct))
        {
            if (chunk.Type == LlmGenerationEvent.ContentChunk) content.Append(chunk.Data);
            else if (chunk.Type == LlmGenerationEvent.ReasoningChunk) reasoning.Append(chunk.Data);
            if (onChunk is not null) await onChunk(chunk);
        }
        return new LlmCallResult(content.ToString(), reasoning.ToString(), attribution, served);
    }

    /// <summary>
    /// One-shot completion for batch work: re-routes each attempt (so the router can bench a failing
    /// provider), treats empty content as transient, backs off 5s × attempt. Returns null when every attempt failed.
    /// </summary>
    public async Task<string?> CompleteAsync(
        LlmGenerationJob job, IReadOnlyList<JobComplexity> tiers, string label, CancellationToken ct, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (endpoint, served) = await RouteAsync(tiers, ct);
                var raw = (await endpoint.CompleteOneShotAsync(job with { JobComplexity = served }, ct)).Trim();
                // reasoning models occasionally spend the whole turn thinking and return no content
                if (raw.Length > 0 || attempt >= attempts) return raw;
                logger?.LogWarning("LLM call {Label}: empty content, attempt {Attempt}", label, attempt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < attempts)
            {
                logger?.LogWarning(ex, "LLM call {Label} failed, attempt {Attempt}", label, attempt);
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger?.LogWarning(ex, "LLM call {Label} failed after {Attempts} attempts", label, attempts);
                return null;
            }
        }
    }

    private static async Task<ChatMessageMetadata?> AttributionAsync(ILlmEndpointGrain endpoint, Activity? span)
    {
        try
        {
            var attribution = await endpoint.GetAttributionAsync();
            span?.SetTag("llm.provider", attribution?.Provider);
            span?.SetTag("llm.model", attribution?.ModelName);
            return attribution;
        }
        catch
        {
            // Attribution is instrumentation; never fail a call over it.
            return null;
        }
    }
}

/// <summary>Tier orders: rest-path work degrades in cost, never in function; race-path work never leaves its tier.</summary>
public static class LlmTiers
{
    public static readonly JobComplexity[] General = { JobComplexity.General };
    public static readonly JobComplexity[] Voice = { JobComplexity.CharacterVoice, JobComplexity.General };
    public static readonly JobComplexity[] Thoughts = { JobComplexity.CharacterThoughts, JobComplexity.General };
    public static readonly JobComplexity[] ThoughtsOnly = { JobComplexity.CharacterThoughts };
}
