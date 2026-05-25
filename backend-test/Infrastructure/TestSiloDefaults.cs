using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace BackendTest.Infrastructure;

/// <summary>
/// Shared silo wiring used by every in-proc <c>TestCluster</c> fixture: memory storage
/// providers for the journaled grains, memory streams, and a no-op <see cref="IMemoryRepository"/>
/// so <c>PersonaGrain</c> can activate without the real graph DB.
/// </summary>
internal static class TestSiloDefaults
{
    public static ISiloBuilder ConfigureDefaults(this ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("parties")
            .AddMemoryGrainStorage("personas")
            .AddMemoryGrainStorage("urls")
            .AddMemoryGrainStorage("PubSubStore")
            .AddStateStorageBasedLogConsistencyProvider("PartyStateStorage")
            .AddMemoryStreams("party-streams");

        siloBuilder.ConfigureServices(services =>
            services.AddSingleton<IMemoryRepository, NoopMemoryRepository>());

        return siloBuilder;
    }
}

/// <summary>
/// Test stub. <c>PersonaGrain</c> takes <see cref="IMemoryRepository"/> in its primary ctor,
/// so the silo must have a registration even when the test never exercises the memory path.
/// </summary>
internal sealed class NoopMemoryRepository : IMemoryRepository
{
    public Task<MemoryCaptureResult> CaptureMomentAsync(
        Guid partyId, Guid roomId, int messageId,
        IReadOnlyList<ParticipantSnapshot> presentParticipants,
        IReadOnlyList<ChatMessage> recentContext, CancellationToken ct)
        => Task.FromResult(new MemoryCaptureResult(false, 0, 0));

    public Task<IReadOnlyList<string>> RecallRecentSnippetsAsync(
        Guid personaId, Guid partyId, int limit, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}
