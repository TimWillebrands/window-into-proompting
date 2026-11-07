namespace PartyTown.Grains.Generation;

/// <summary>Has the router ping every provider on a timer, so Offline is known before a call hits it (ADR 0040).</summary>
public sealed class ProviderAvailabilityService(IGrainFactory grains, ILogger<ProviderAvailabilityService> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                await grains.GetGrain<ILlmRouterGrain>(0).CheckAvailabilityAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Provider availability check failed; trying again next round");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try { return await timer.WaitForNextTickAsync(stoppingToken); }
        catch (OperationCanceledException) { return false; }
    }
}
