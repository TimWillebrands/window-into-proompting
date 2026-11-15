using Microsoft.Extensions.Hosting;
using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.Services.Seeding;

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

        var partyRoot = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        var parties = await partyRoot.GetAll();
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
