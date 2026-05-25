using Microsoft.Extensions.Hosting;
using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.Services.Seeding;

/// <summary>
/// Ensures the singleton <see cref="Narrator"/> library Persona exists, and that every
/// existing Party has a Narrator-Participant. Run on host startup. See ADR 0012.
/// </summary>
/// <remarks>
/// Both steps are idempotent: re-running is a no-op when the system is already in the desired
/// state, which makes this safe to run on every startup (no migration version tracking needed).
/// </remarks>
public sealed class NarratorSeeder
{
    public async Task RunAsync(IGrainFactory grains, CancellationToken ct)
    {
        var personaRoot = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        if (!await personaRoot.HasPersonaId(Narrator.PersonaId))
        {
            await personaRoot.AddPersona(
                Narrator.PersonaId,
                Narrator.DisplayName,
                Narrator.SystemPrompt,
                Narrator.Bio);
        }

        // Back-fill: re-assert each Party's participant list. PartyGrain.SetParticipants
        // enforces the Narrator-present invariant and short-circuits when nothing changed,
        // so this single call covers both parties that never had a Narrator and parties
        // that already do.
        var partyRoot = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        var parties = await partyRoot.GetAll();
        // Bounded parallelism: a fleet boot with many parties would otherwise spawn one
        // concurrent SetParticipants call per party, each touching the journal store.
        // 8 keeps the storage backend honest without serialising the back-fill.
        await Parallel.ForEachAsync(
            parties,
            new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct },
            async (party, _) =>
            {
                await grains.GetGrain<IPartyGrain>(party.Id)
                    .SetParticipants(party.Participants ?? new List<PartyParticipant>());
            });
    }
}

/// <summary>
/// Hosted-service wrapper that runs <see cref="NarratorSeeder"/> once at app start. The
/// back-fill is fire-and-forget so it does not block the host from accepting traffic; an
/// uncaught failure logs but does not crash the silo (the next boot retries).
/// </summary>
public sealed class NarratorSeederHostedService(
    IGrainFactory grains,
    ILogger<NarratorSeederHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await new NarratorSeeder().RunAsync(grains, cancellationToken);
                logger.LogInformation("Narrator seed + back-fill complete");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Narrator seeding failed");
            }
        }, cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
