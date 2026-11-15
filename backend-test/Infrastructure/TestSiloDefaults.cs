using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans.Hosting;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest.Infrastructure;

internal static class TestSiloDefaults
{
    public static ISiloBuilder ConfigureDefaults(this ISiloBuilder siloBuilder)
    {
        siloBuilder
            .AddMemoryGrainStorage("parties")
            .AddMemoryGrainStorage("personas")
            .AddMemoryGrainStorage("urls")
            .AddMemoryGrainStorage("imports")
            .AddMemoryGrainStorage("PubSubStore")
            .AddStateStorageBasedLogConsistencyProvider("PartyStateStorage")
            .AddMemoryStreams("party-streams");

        siloBuilder.ConfigureServices(services =>
        {
            services.AddSingleton<PartyTown.Services.SystemOne.ISystemOneJudge>(new MenuJudge(() => null));
            services.AddSingleton<RaceTrigger>();
            services.AddSingleton<ResponsePipeline>();
            services.AddSingleton<IMemoryRepository>(_ =>
            {
                var mock = new Mock<IMemoryRepository>();
                mock.Setup(m => m.RecallAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                        It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
                    .ReturnsAsync(Array.Empty<RecalledMemory>());
                mock.Setup(m => m.RecallStancesAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<Guid>>(),
                        It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<StanceLine>());
                mock.Setup(m => m.ListStancesAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(Array.Empty<StanceRecord>());
                return mock.Object;
            });
        });

        return siloBuilder;
    }
}
