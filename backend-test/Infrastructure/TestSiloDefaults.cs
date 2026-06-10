using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans.Hosting;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest.Infrastructure;

/// <summary>
/// Shared silo wiring used by every in-proc <c>TestCluster</c> fixture: memory storage
/// providers for the journaled grains, memory streams, and the DI registrations
/// (<c>RaceTrigger</c>, <c>ResponsePipeline</c>, a mocked <see cref="IMemoryRepository"/>)
/// that <c>PersonaGrain</c> activation pulls through DI even when the test never
/// exercises them.
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
        {
            services.AddSingleton<RaceTrigger>();
            services.AddSingleton<ResponsePipeline>();
            services.AddSingleton<IMemoryRepository>(_ =>
            {
                var mock = new Mock<IMemoryRepository>();
                mock.Setup(m => m.RecallAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<RecalledMemory>());
                return mock.Object;
            });
        });

        return siloBuilder;
    }
}
