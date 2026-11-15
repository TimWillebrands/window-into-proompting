using PartyTown.Grains;

namespace PartyTown.World;

public sealed class WorldPulseService(IGrainFactory grains, ILogger<WorldPulseService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var worlds = await grains.GetGrain<IPartyRootGrain>(Guid.Empty).GetWorldsAsync();
                await Task.WhenAll(worlds.Where(w => !w.Stopped).Select(w => grains.GetGrain<IWorldClockGrain>(w.Id).PulseAsync()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "World pulse failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
