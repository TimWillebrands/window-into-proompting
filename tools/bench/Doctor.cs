using PartyTown.Grains.Generation;

namespace PartyTown.Bench;

/// <summary>
/// Verifies a Bench Session end-to-end (ADR 0011): which providers are configured (from the
/// seeded <see cref="ILlmProviderConfigGrain"/>), which models are actually reachable through
/// the real <see cref="ILlmRouterGrain"/>, and which <see cref="JobComplexity"/> tiers have
/// coverage. Reports "LIVE" vs "prompt-capture only" so an agent knows whether full probes
/// will route to a model. Endpoint <c>GetModelsAsync</c> already swallows errors → empty list,
/// so this never throws on an unreachable provider; a timeout guards a hung provider.
/// </summary>
public static class Doctor
{
    public static async Task RunAsync(IGrainFactory grains, string memoryStatus)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        Console.WriteLine("\n=== bench doctor ===\n");

        // Mirrors the LLM LIVE/tier-0 report below: Program already tried to bring up the bench's
        // ephemeral AGE container and handed us the verdict.
        Console.WriteLine($"memory: {memoryStatus}\n");

        var config = grains.GetGrain<ILlmProviderConfigGrain>(0);
        List<LlmProviderEntry> providers;
        try
        {
            providers = await config.GetProvidersAsync().WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("  provider config grain timed out (15s) — silo unhealthy.");
            return;
        }

        Console.WriteLine($"Configured providers ({providers.Count}):");
        if (providers.Count == 0)
            Console.WriteLine("  (none — check benchsettings.json Llm:Providers)");
        foreach (var p in providers)
        {
            var enabled = p.IsEnabled ? "" : " [disabled]";
            Console.WriteLine($"  · {p.Type} {p.BaseUrl} model={p.ModelName ?? "(none)"} complexities={p.SupportedComplexities}{enabled}");
        }

        IReadOnlyList<LlmModel> models = Array.Empty<LlmModel>();
        try
        {
            var router = grains.GetGrain<ILlmRouterGrain>(0);
            models = await router.GetModelsAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n  model probe timed out (15s) — provider unreachable or slow.");
        }

        Console.WriteLine($"\nReachable models ({models.Count}):");
        if (models.Count == 0)
            Console.WriteLine("  (none reachable)");
        foreach (var group in models.GroupBy(m => m.ProviderType))
        {
            Console.WriteLine($"  {group.Key}:");
            foreach (var m in group)
                Console.WriteLine($"    · {m.Name}  complexities={m.SupportedComplexities}");
        }

        Console.WriteLine("\nComplexity coverage (reachable models supporting each tier):");
        var uncovered = new List<JobComplexity>();
        foreach (JobComplexity tier in Enum.GetValues<JobComplexity>())
        {
            var supporting = models.Count(m => m.SupportedComplexities.HasFlag(tier));
            var mark = supporting > 0 ? "✓" : "✗";
            if (supporting == 0) uncovered.Add(tier);
            Console.WriteLine($"  {mark} {tier}: {supporting} model(s)");
        }
        if (uncovered.Any(t => t != JobComplexity.General))
        {
            Console.WriteLine("  Note: seeded providers default to General only — probes needing the");
            Console.WriteLine($"  uncovered tier(s) ({string.Join(", ", uncovered)}) require updated entries first.");
        }

        Console.WriteLine();
        if (models.Count > 0)
            Console.WriteLine(">>> bench session LIVE — probes will route to a model.");
        else
            Console.WriteLine(">>> no providers reachable — prompt-capture only (tier-0 mode).");
        Console.WriteLine();
    }
}
