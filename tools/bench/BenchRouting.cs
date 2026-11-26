using PartyTown.Grains.Generation;

namespace PartyTown.Bench;

public static class BenchRouting
{
    public const string ModelVariable = "BENCH_ANTHROPIC_MODEL";

    private const JobComplexity Tiers = JobComplexity.General | JobComplexity.CharacterVoice | JobComplexity.CharacterThoughts;

    private static readonly JobComplexity[] Shown = { JobComplexity.General, JobComplexity.CharacterVoice, JobComplexity.CharacterThoughts };

    public static string? AnthropicModel()
        => Environment.GetEnvironmentVariable(ModelVariable) is { Length: > 0 } model ? model.Trim() : null;

    public static string? AnthropicKey()
        => Environment.GetEnvironmentVariable("CLAUDE_API_KEY") is { Length: > 0 } claude ? claude.Trim()
            : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") is { Length: > 0 } anthropic ? anthropic.Trim()
            : null;

    public static async Task UseAnthropicAsync(IGrainFactory grains, string model, string apiKey)
    {
        var config = grains.GetGrain<ILlmProviderConfigGrain>(0);
        foreach (var provider in await config.GetProvidersAsync())
        {
            if ((provider.SupportedComplexities & Tiers) == 0) continue;
            var rest = provider.SupportedComplexities & ~Tiers;
            await config.UpdateProviderAsync(rest == 0
                ? provider with { IsEnabled = false }
                : provider with { SupportedComplexities = rest });
        }

        await config.AddProviderAsync(new LlmProviderEntry
        {
            Type = "anthropic",
            BaseUrl = AnthropicRequest.DefaultBaseUrl,
            ApiKey = apiKey,
            ModelName = model,
            SupportedComplexities = Tiers,
        });
    }

    public static async Task<string> DescribeAsync(IGrainFactory grains)
    {
        var providers = (await grains.GetGrain<ILlmProviderConfigGrain>(0).GetProvidersAsync())
            .Where(p => p.IsEnabled && !p.Reserved)
            .ToList();
        return string.Join(", ", Shown.Select(tier =>
        {
            var serving = providers
                .Where(p => p.SupportedComplexities.HasFlag(tier))
                .OrderBy(p => p.Priority)
                .Select(p => $"{p.Type} {p.ModelName ?? "(no model)"}")
                .Distinct()
                .ToList();
            return $"{tier}={(serving.Count == 0 ? "none" : string.Join(" | ", serving))}";
        }));
    }
}
