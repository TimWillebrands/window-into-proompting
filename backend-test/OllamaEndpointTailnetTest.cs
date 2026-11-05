using BackendTest.Fakes;
using Microsoft.Extensions.Configuration;
using Moq;
using Orleans.TestKit;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>The endpoint grain honours its provider's Tailnet for both generation and the model list.</summary>
public class OllamaEndpointTailnetTest : TestKitBase
{
    private const string Reply =
        """{"id":"x","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","logprobs":null,"message":{"role":"assistant","content":"hi"}}],"models":[{"name":"gemma"}]}""";

    private static readonly LlmGenerationJob Job = new()
    {
        Messages = new List<LlmChatMessage> { new() { Role = "user", Content = "hello" } },
    };

    private async Task<OllamaEndpointGrain> GrainOnTailnetAsync(string sidecarUrl, string? tailnet)
    {
        var id = Guid.NewGuid();
        var entry = new LlmProviderEntry { Id = id, Type = "ollama", BaseUrl = "http://gpu-box:11434", ModelName = "m", Tailnet = tailnet };
        Silo.AddProbe<ILlmProviderConfigGrain>(0)
            .Setup(c => c.GetProvidersAsync())
            .ReturnsAsync(new List<LlmProviderEntry> { entry });
        Silo.AddService(new Tailnets(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tailnets:home"] = sidecarUrl })
            .Build()));
        Silo.AddService(Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()));
        Silo.AddService(Mock.Of<IAiObservabilityCapture>());
        return await Silo.CreateGrainAsync<OllamaEndpointGrain>(id);
    }

    [Fact]
    public async Task Generation_GoesThroughTheProvidersTailnet()
    {
        await using var sidecar = new FakeSocksProxy { Body = Reply };
        var grain = await GrainOnTailnetAsync(sidecar.Url, "home");

        Assert.Equal("hi", await grain.CompleteOneShotAsync(Job));
        Assert.Equal("gpu-box", Assert.Single(sidecar.Connects));
    }

    [Fact]
    public async Task ModelList_GoesThroughTheProvidersTailnet()
    {
        await using var sidecar = new FakeSocksProxy { Body = Reply };
        var grain = await GrainOnTailnetAsync(sidecar.Url, "home");

        Assert.Equal("gemma", Assert.Single(await grain.GetModelsAsync()).Name);
        Assert.Equal("gpu-box", Assert.Single(sidecar.Connects));
    }

    [Fact]
    public async Task WithoutATailnet_TheSidecarIsNotUsed()
    {
        await using var sidecar = new FakeSocksProxy { Body = Reply };
        var grain = await GrainOnTailnetAsync(sidecar.Url, tailnet: null);

        await grain.GetModelsAsync();

        Assert.Empty(sidecar.Connects);
    }
}
