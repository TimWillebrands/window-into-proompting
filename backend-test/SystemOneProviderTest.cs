using System.Net;
using System.Text;
using BackendTest.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Orleans.TestKit;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>A decision call uses the provider's URL, key, Tailnet, bench and capture, the same as a generation.</summary>
public class SystemOneProviderTest : TestKitBase
{
    private const string Answer = """{"answers":{"x":{"type":"noul","noul":0.8}}}""";
    private const string Questions = """{"x":{"type":"noul","instructions":"x"}}""";

    private readonly Mock<IAiObservabilityCapture> _capture = new();

    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Answer, Encoding.UTF8, "application/json"),
            });
        }
    }

    private Mock<ILlmRouterGrain> Arrange(LlmProviderEntry entry, HttpMessageHandler? direct = null, string sidecar = "socks5://127.0.0.1:1")
    {
        Silo.AddProbe<ILlmProviderConfigGrain>(0)
            .Setup(c => c.GetProvidersAsync())
            .ReturnsAsync(new List<LlmProviderEntry> { entry });
        Silo.AddService(new Tailnets(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tailnets:home"] = sidecar })
            .Build()));
        Silo.AddService(Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient(direct ?? new Capture())));
        Silo.AddService(_capture.Object);
        return Silo.AddProbe<ILlmRouterGrain>(0);
    }

    private static LlmProviderEntry Entry(string type, string baseUrl, string? tailnet = null, string? apiKey = null) => new()
    {
        Id = Guid.NewGuid(), Type = type, BaseUrl = baseUrl, ModelName = "clef", Tailnet = tailnet, ApiKey = apiKey,
    };

    [Fact]
    public async Task Ollama_AsksThroughItsTailnet_WithTheProvidersModel()
    {
        await using var sidecar = new FakeSocksProxy { Body = Answer };
        var entry = Entry("ollama", "http://gpu-box:8091", "home");
        Arrange(entry, sidecar: sidecar.Url);
        var grain = await Silo.CreateGrainAsync<OllamaEndpointGrain>(entry.Id);

        Assert.Equal(Answer, await grain.JudgeAsync("state", Questions));

        Assert.Equal("gpu-box", Assert.Single(sidecar.Connects));
        var request = Assert.Single(sidecar.Requests);
        Assert.StartsWith("POST /v1/systemone ", request);
        Assert.Contains("\"model\":\"clef\"", request);
        Assert.Contains("\"state\":\"state\"", request);
        Assert.Contains("\"noul\"", request);
    }

    [Fact]
    public async Task Ollama_WithoutATailnet_ConnectsDirectly()
    {
        var direct = new Capture();
        var entry = Entry("ollama", "http://gpu-box:8091");
        Arrange(entry, direct);
        var grain = await Silo.CreateGrainAsync<OllamaEndpointGrain>(entry.Id);

        await grain.JudgeAsync("state", Questions);

        Assert.Equal("http://gpu-box:8091/v1/systemone", direct.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task OpenRouter_AsksItsBaseUrlWithItsKey()
    {
        var direct = new Capture();
        var entry = Entry("openrouter", "https://openrouter.ai/api/v1", apiKey: "sk-or");
        Arrange(entry, direct);
        var grain = await Silo.CreateGrainAsync<OpenRouterEndpointGrain>(entry.Id);

        await grain.JudgeAsync("state", Questions);

        Assert.Equal("https://openrouter.ai/api/v1/systemone", direct.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer sk-or", direct.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Failure_BenchesTheProvider_AndStillRethrows()
    {
        var entry = Entry("ollama", "http://gpu-box:8091", "work");
        var router = Arrange(entry);
        var grain = await Silo.CreateGrainAsync<OllamaEndpointGrain>(entry.Id);

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => grain.JudgeAsync("state", Questions));

        router.Verify(r => r.ReportFailureAsync(entry.Id, It.Is<string>(m => m.Contains("'work'"))), Times.Once);
    }

    [Fact]
    public async Task Cancelling_DoesNotBenchTheProvider()
    {
        await using var sidecar = new FakeSocksProxy { Reply = null };
        var entry = Entry("ollama", "http://gpu-box:8091", "home");
        var router = Arrange(entry, sidecar: sidecar.Url);
        var grain = await Silo.CreateGrainAsync<OllamaEndpointGrain>(entry.Id);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grain.JudgeAsync("state", Questions, cts.Token));

        router.Verify(r => r.ReportFailureAsync(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Calls_CountTowardsPressure_AndReleaseIt()
    {
        var entry = Entry("ollama", "http://gpu-box:8091");
        Arrange(entry);
        var grain = await Silo.CreateGrainAsync<OllamaEndpointGrain>(entry.Id);

        await grain.JudgeAsync("state", Questions);

        Assert.Equal(0, await grain.PressureAsync());
    }

    [Fact]
    public async Task Calls_AreCapturedLikeGenerations()
    {
        var entry = Entry("ollama", "http://gpu-box:8091");
        Arrange(entry);
        var grain = await Silo.CreateGrainAsync<OllamaEndpointGrain>(entry.Id);

        await grain.JudgeAsync("state", Questions);

        _capture.Verify(c => c.CaptureGenerationAsync(
            It.Is<IReadOnlyList<LlmChatMessage>>(m => m.Any(x => x.Content == "state")),
            Answer, "ollama", "clef", "http://gpu-box:8091", null, false, false, It.IsAny<CancellationToken>()), Times.Once);
    }
}

/// <summary>System One's env chain becomes Decisions providers once, in its old order, and Control Panel owns them after.</summary>
public class DecisionProviderSeedTest : TestKitBase
{
    private static LlmOptions OptionsFrom(params (string Key, string Value)[] env)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(env.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build());
        services.AddOptions();
        services.AddLlmProviderOptions();
        return services.BuildServiceProvider().GetRequiredService<IOptions<LlmOptions>>().Value;
    }

    [Fact]
    public void Env_KeepsTheOldOrder_LocalThenTypeSafeThenJevOnOpenRouter()
    {
        var seeded = OptionsFrom(("SYSTEM_ONE_LOCAL_URL", "http://box:8091"), ("TYPESAFE_API_KEY", "ts"), ("OPENROUTER_API_KEY", "or")).DecisionProviders;

        Assert.Equal(new[] { "clef-flash", "jev-latest", "~typesafe/jev-latest" }, seeded.Select(p => p.ModelName));
        Assert.Equal(new[] { 0, 1, 2 }, seeded.Select(p => p.Priority));
        Assert.All(seeded, p => Assert.Equal(JobComplexity.Decisions, p.SupportedComplexities));
        Assert.Equal("https://openrouter.ai/api/v1", seeded[2].BaseUrl);
    }

    [Fact]
    public void Env_RespectsTheOpenRouterSwitch()
        => Assert.Empty(OptionsFrom(("OPENROUTER_API_KEY", "or"), ("SYSTEM_ONE_OPENROUTER", "false")).DecisionProviders);

    [Fact]
    public void Env_WithNothingSet_SeedsNothing()
        => Assert.Empty(OptionsFrom().DecisionProviders);

    private static IOptions<LlmOptions> WithJev() => Options.Create(new LlmOptions
    {
        DecisionProviders = new List<LlmProviderEntry>
        {
            new() { Type = "openrouter", BaseUrl = "https://openrouter.ai/api/v1", ModelName = "jev", SupportedComplexities = JobComplexity.Decisions },
        },
    });

    [Fact]
    public async Task Activation_SeedsThem_WithRealIds()
    {
        Silo.AddService(WithJev());
        var config = await Silo.CreateGrainAsync<LlmProviderConfigGrain>(0);

        var jev = Assert.Single(await config.GetProvidersAsync());

        Assert.Equal("jev", jev.ModelName);
        Assert.NotEqual(Guid.Empty, jev.Id);
    }

    [Fact]
    public async Task ExistingWorld_KeepsItsProviders_AndGainsDecisions()
    {
        var state = Silo.StorageManager.GetStorage<LlmProviderConfigState>("llm-providers");
        state.State = new LlmProviderConfigState
        {
            SeededFromOptions = true,
            Providers = new List<LlmProviderEntry> { new() { Id = Guid.NewGuid(), Type = "ollama", BaseUrl = "http://a", ModelName = "existing" } },
        };
        Silo.AddService(WithJev());
        var config = await Silo.CreateGrainAsync<LlmProviderConfigGrain>(0);

        Assert.Equal(new[] { "existing", "jev" }, (await config.GetProvidersAsync()).Select(p => p.ModelName));
    }

    [Fact]
    public async Task ADeletedDecisionsProvider_StaysDeleted()
    {
        var state = Silo.StorageManager.GetStorage<LlmProviderConfigState>("llm-providers");
        state.State = new LlmProviderConfigState { SeededFromOptions = true, DecisionsSeeded = true };
        Silo.AddService(WithJev());
        var config = await Silo.CreateGrainAsync<LlmProviderConfigGrain>(0);

        Assert.Empty(await config.GetProvidersAsync());
    }

    [Fact]
    public async Task PersonaRoutes_RejectTheDecisionsTier_SinceSystemOneHasNoPersona()
    {
        Silo.AddService(Options.Create(new LlmOptions()));
        Silo.AddProbe<ILlmRouterGrain>(0);
        var config = await Silo.CreateGrainAsync<LlmProviderConfigGrain>(0);

        await Assert.ThrowsAsync<ArgumentException>(() => config.SetPersonaRoutesAsync(
            Guid.NewGuid(), new List<PersonaRoute> { new() { Tier = JobComplexity.Decisions, ProviderId = Guid.NewGuid() } }));
    }
}
