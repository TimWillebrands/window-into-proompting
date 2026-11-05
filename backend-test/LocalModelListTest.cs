using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using Orleans.TestKit;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>A local server's models can be picked whichever list it serves, so a decision model needs no typing in.</summary>
public class LocalModelListTest : TestKitBase
{
    private sealed class Server(Dictionary<string, string> paths) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(paths.TryGetValue(request.RequestUri!.AbsolutePath, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private async Task<IReadOnlyList<LlmModel>> ListAsync(Dictionary<string, string> paths)
    {
        var id = Guid.NewGuid();
        var entry = new LlmProviderEntry { Id = id, Type = "ollama", BaseUrl = "http://mac:8091", ModelName = "m" };
        Silo.AddProbe<ILlmProviderConfigGrain>(0)
            .Setup(c => c.GetProvidersAsync())
            .ReturnsAsync(new List<LlmProviderEntry> { entry });
        Silo.AddService(new Tailnets(new ConfigurationBuilder().Build()));
        Silo.AddService(Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient(new Server(paths))));
        Silo.AddService(Mock.Of<IAiObservabilityCapture>());
        var grain = await Silo.CreateGrainAsync<OllamaEndpointGrain>(id);
        return await grain.GetModelsAsync();
    }

    [Fact]
    public async Task Ollama_ListsThroughItsTags()
    {
        var models = await ListAsync(new() { ["/api/tags"] = """{"models":[{"name":"llama3.2"}]}""" });

        Assert.Equal("llama3.2", Assert.Single(models).Name);
    }

    [Fact]
    public async Task AnyOtherServer_ListsThroughOpenAiModels()
    {
        var models = await ListAsync(new() { ["/v1/models"] = """{"object":"list","data":[{"id":"d1","object":"model"}]}""" });

        Assert.Equal("d1", Assert.Single(models).Name);
    }

    [Fact]
    public async Task AServerWithNeither_ListsNothing()
        => Assert.Empty(await ListAsync(new()));
}
