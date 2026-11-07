using System.ClientModel;
using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Moq;
using Orleans.TestKit;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>A local provider is Offline while it can't be reached, and only a connection failure says so (ADR 0040).</summary>
public class ProviderAvailabilityTest : TestKitBase
{
    private sealed class Server(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(answer(request));
    }

    private async Task<OllamaEndpointGrain> OllamaAsync(HttpMessageHandler server, string? tailnet = null)
    {
        var id = Guid.NewGuid();
        var entry = new LlmProviderEntry { Id = id, Type = "ollama", BaseUrl = "http://cachyos:11434", ModelName = "d1:3b", Tailnet = tailnet };
        Silo.AddProbe<ILlmProviderConfigGrain>(0)
            .Setup(c => c.GetProvidersAsync())
            .ReturnsAsync(new List<LlmProviderEntry> { entry });
        Silo.AddService(new Tailnets(new ConfigurationBuilder().Build()));
        Silo.AddService(Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient(server)));
        Silo.AddService(Mock.Of<IAiObservabilityCapture>());
        return await Silo.CreateGrainAsync<OllamaEndpointGrain>(id);
    }

    [Fact]
    public async Task Ollama_IsReachable_WhenItListsItsModels()
    {
        var grain = await OllamaAsync(new Server(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"models":[{"name":"d1:3b"}]}""", Encoding.UTF8, "application/json"),
        }));

        Assert.Null(await grain.PingAsync());
    }

    [Fact]
    public async Task Ollama_SaysWhy_WhenTheBoxIsDown()
    {
        var grain = await OllamaAsync(new Server(_ => throw new HttpRequestException("Connection refused (cachyos:11434)")));

        Assert.Equal("Connection refused (cachyos:11434)", await grain.PingAsync());
    }

    [Fact]
    public async Task Ollama_OnAMissingTailnet_IsUnreachable_NeverDirect()
    {
        var direct = new Server(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var grain = await OllamaAsync(direct, tailnet: "home");

        Assert.Contains("Tailnet 'home' is not configured", await grain.PingAsync());
    }

    public static TheoryData<Exception, bool> Failures => new()
    {
        { new HttpRequestException("Connection refused"), true },
        { new HttpRequestException("Response status code does not indicate success: 402 (Payment Required).", null, HttpStatusCode.PaymentRequired), false },
        { new ClientResultException("Connection refused", innerException: new HttpRequestException("Connection refused")), true },
        { new AggregateException(new ClientResultException("Retry failed")), true },
        { new TaskCanceledException("judge timed out"), false },
        { new InvalidOperationException("model not found"), false },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Unreachable_IsAConnectionThatNeverHappened_NotARefusal(Exception failure, bool unreachable)
        => Assert.Equal(unreachable, LlmEndpointGrainUtils.IsUnreachable(failure));
}
