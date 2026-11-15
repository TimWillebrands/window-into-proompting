using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using PartyTown.Model;

namespace PartyTown.Grains.Generation;

public sealed record LlmCallResult(string Content, string Reasoning, ChatMessageMetadata? Attribution, JobComplexity Served);

public sealed class LlmCall(ILlmRouterGrain router, ILogger? logger = null, Guid? persona = null)
{
    public async Task<(ILlmEndpointGrain Endpoint, JobComplexity Served)> RouteAsync(
        IReadOnlyList<JobComplexity> tiers, CancellationToken ct, IReadOnlySet<ILlmEndpointGrain>? exclude = null)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                if (persona is { } id && await router.AssignedEndpointAsync(id, tiers[i], ct) is { } assigned
                    && exclude?.Contains(assigned) != true)
                    return (assigned, tiers[i]);
                var pooled = await router.RouteAsync(tiers[i], persona, ct);
                if (exclude?.Contains(pooled) == true)
                    throw new InvalidOperationException($"Every provider for {tiers[i]} already failed this call");
                return (pooled, tiers[i]);
            }
            catch (InvalidOperationException) when (i < tiers.Count - 1)
            {
                logger?.LogDebug("No provider for {Tier}; falling back to {Next}", tiers[i], tiers[i + 1]);
            }
        }
    }

    public async Task<LlmCallResult> StreamAsync(
        LlmGenerationJob job,
        IReadOnlyList<JobComplexity> tiers,
        CancellationToken ct,
        Func<LlmGenerationEvent, Task>? onChunk = null,
        Activity? span = null)
    {
        var failed = new HashSet<ILlmEndpointGrain>();
        Exception? failure = null;
        while (true)
        {
            ILlmEndpointGrain endpoint;
            JobComplexity served;
            try
            {
                (endpoint, served) = await RouteAsync(tiers, ct, failed);
            }
            catch (InvalidOperationException) when (failure is not null)
            {
                ExceptionDispatchInfo.Throw(failure);
                throw;
            }
            if (served != tiers[0]) span?.SetTag("llm.tier_fallback", true);
            var attribution = await AttributionAsync(endpoint, span);

            var content = new StringBuilder();
            var reasoning = new StringBuilder();
            var started = false;
            try
            {
                await foreach (var chunk in endpoint.GenerateAsync(job with { JobComplexity = served }, ct))
                {
                    started = true;
                    if (chunk.Type == LlmGenerationEvent.ContentChunk) content.Append(chunk.Data);
                    else if (chunk.Type == LlmGenerationEvent.ReasoningChunk) reasoning.Append(chunk.Data);
                    if (onChunk is not null) await onChunk(chunk);
                }
                return new LlmCallResult(content.ToString(), reasoning.ToString(), attribution, served);
            }
            catch (Exception ex) when (!started && !ct.IsCancellationRequested)
            {
                logger?.LogWarning(ex, "LLM stream failed before its first chunk; trying the next provider");
                span?.SetTag("llm.provider_fallback", true);
                failed.Add(endpoint);
                failure = ex;
            }
        }
    }

    public async Task<string?> CompleteAsync(
        LlmGenerationJob job, IReadOnlyList<JobComplexity> tiers, string label, CancellationToken ct, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (endpoint, served) = await RouteAsync(tiers, ct);
                var raw = (await endpoint.CompleteOneShotAsync(job with { JobComplexity = served }, ct)).Trim();
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
            return null;
        }
    }
}

public static class LlmTiers
{
    public static readonly JobComplexity[] General = { JobComplexity.General };
    public static readonly JobComplexity[] Voice = { JobComplexity.CharacterVoice, JobComplexity.General };
    public static readonly JobComplexity[] Thoughts = { JobComplexity.CharacterThoughts, JobComplexity.General };
    public static readonly JobComplexity[] ThoughtsOnly = { JobComplexity.CharacterThoughts };
    public static readonly JobComplexity[] All =
        { JobComplexity.General, JobComplexity.CharacterVoice, JobComplexity.CharacterThoughts, JobComplexity.Decisions };
}
