using BackendTest.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Orleans.Hosting;
using Orleans.TestingHost;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>Endpoint grains aren't reentrant, so only a real cluster shows a decision getting past a stuck generation.</summary>
public class EndpointInterleavingTest : IAsyncLifetime
{
    private static readonly FakeSocksProxy Sidecar = new() { Reply = null };

    private TestCluster _cluster = null!;

    private sealed class Silo : ISiloConfigurator
    {
        public void Configure(ISiloBuilder silo)
        {
            silo.AddMemoryGrainStorage("parties");
            silo.ConfigureServices(services =>
            {
                services.AddHttpClient();
                services.AddSingleton(Mock.Of<IAiObservabilityCapture>());
                services.AddSingleton(Options.Create(new LlmOptions()));
                services.AddSingleton(new Tailnets(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Tailnets:home"] = Sidecar.Url })
                    .Build()));
            });
        }
    }

    public async Task InitializeAsync()
    {
        var builder = new TestClusterBuilder(1);
        builder.AddSiloBuilderConfigurator<Silo>();
        _cluster = builder.Build();
        await _cluster.DeployAsync();
    }

    public async Task DisposeAsync() => await _cluster.DisposeAsync();

    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(20);
        return condition();
    }

    private int Dials
    {
        get { lock (Sidecar.Connects) return Sidecar.Connects.Count; }
    }

    [Fact]
    public async Task ADecision_DoesNotQueueBehindAGenerationOnTheSameProvider()
    {
        var provider = await _cluster.GrainFactory.GetGrain<ILlmProviderConfigGrain>(0).AddProviderAsync(new LlmProviderEntry
        {
            Type = "ollama", BaseUrl = "http://gpu-box:11434", ModelName = "m", Tailnet = "home",
        });
        var endpoint = _cluster.GrainFactory.GetGrain<IOllamaEndpointGrain>(provider.Id);
        using var stop = new CancellationTokenSource();
        var before = Dials;

        var generation = endpoint.CompleteOneShotAsync(new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage> { new() { Role = "user", Content = "hello" } },
        }, stop.Token);
        Assert.True(await EventuallyAsync(() => Dials == before + 1), "the generation never reached the sidecar");

        var pressure = await endpoint.PressureAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        var decision = endpoint.JudgeAsync("state", """{"x":{"type":"noul","instructions":"x"}}""", stop.Token);
        var reached = await EventuallyAsync(() => Dials == before + 2);

        await stop.CancelAsync();
        await Task.WhenAll(generation, decision).ContinueWith(_ => { });

        Assert.Equal(1, pressure);
        Assert.True(reached, "the decision queued behind the generation");
    }
}
