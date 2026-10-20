using PartyTown.Grains;

namespace PartyTown.World;

/// <summary>Heartbeat for every Party's World clock (ADR 0018) — no grain timers or reminder table needed.</summary>
public sealed class WorldPulseService(IGrainFactory grains, ILogger<WorldPulseService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the silo finish starting before the first pulse.
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                var parties = await grains.GetGrain<IPartyRootGrain>(Guid.Empty).GetAll();
                await Task.WhenAll(parties.Select(p => grains.GetGrain<IWorldClockGrain>(p.Id).PulseAsync()));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "World pulse failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
