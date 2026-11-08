using BackendTest.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Orleans.Hosting;
using Orleans.TestingHost;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;
using PartyTown.Observability;
using PartyTown.Services.SystemOne;

namespace BackendTest;

/// <summary>Endpoint grains aren't reentrant, so only a real cluster shows a decision getting past a stuck generation.</summary>
public class EndpointInterleavingTest : IAsyncLifetime
{
    private static readonly FakeSocksProxy Sidecar = new() { Reply = null };
    private static readonly FakeSocksProxy Answering = new() { Body = """{"answers":{"x":{"type":"noul","noul":0.8}}}""" };

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
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Tailnets:home"] = Sidecar.Url,
                        ["Tailnets:shared"] = Answering.Url,
                    })
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
    public async Task APersonasDecisions_ReachTheirOwnModel_ThroughItsTailnet()
    {
        var config = _cluster.GrainFactory.GetGrain<ILlmProviderConfigGrain>(0);
        var mac = await config.AddProviderAsync(new LlmProviderEntry
        {
            Type = "ollama", BaseUrl = "http://mac:8091", ModelName = "d1", Tailnet = "shared", Reserved = true,
        });
        var vlad = Guid.NewGuid();
        await config.SetPersonaRoutesAsync(vlad, new List<PersonaRoute> { new() { Tier = JobComplexity.Decisions, ProviderId = mac.Id } });
        var judge = new ProviderJudge(_cluster.GrainFactory,
            new LlmJudge(_cluster.GrainFactory, NullLogger<LlmJudge>.Instance),
            NullLogger<ProviderJudge>.Instance);
        await judge.RefreshAsync();

        var answers = await judge.For(vlad).AskAsync("state",
            new Dictionary<string, SystemOneQuestion> { ["x"] = new NoulQuestion("x") }, CancellationToken.None);

        Assert.Equal(0.8, answers.Noul("x", 0));
        lock (Answering.Requests) Assert.Contains(Answering.Requests, r => r.StartsWith("POST /v1/systemone ") && r.Contains("\"model\":\"d1\""));
    }

    [Fact]
    public async Task SavingAProviderWhoseEndpointIsAsleep_DoesNotDeadlock()
    {
        var config = _cluster.GrainFactory.GetGrain<ILlmProviderConfigGrain>(0);
        var provider = await config.AddProviderAsync(new LlmProviderEntry { Type = "ollama", BaseUrl = "http://mac:8091", ModelName = "d1" });

        var saved = await config.UpdateProviderAsync(provider with { SupportedComplexities = JobComplexity.Decisions })
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(JobComplexity.Decisions, saved.SupportedComplexities);
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
