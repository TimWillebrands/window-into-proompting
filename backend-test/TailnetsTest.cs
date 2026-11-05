using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using BackendTest.Fakes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Configuration;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>A provider on a Tailnet goes through that Tailnet's sidecar, and a dead path fails over like a dead host.</summary>
public class TailnetsTest
{
    private const string Completion =
        """{"id":"x","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","logprobs":null,"message":{"role":"assistant","content":"hi"}}]}""";

    private const string Stream =
        """
        data: {"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{"role":"assistant","content":"hi"},"finish_reason":null}]}

        data: {"id":"x","object":"chat.completion.chunk","created":1,"model":"m","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

        data: [DONE]


        """;

    private static readonly LlmGenerationJob Job = new()
    {
        Messages = new List<LlmChatMessage> { new() { Role = "user", Content = "hello" } },
    };

    private static Tailnets Configured(params (string Name, string Url)[] tailnets) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(tailnets.ToDictionary(t => $"Tailnets:{t.Name}", t => (string?)t.Url))
        .Build());

    private static ChatClient Chat(HttpClient? http, string baseUrl = "http://gpu-box:11434")
    {
        var options = new OpenAIClientOptions { Endpoint = new Uri($"{baseUrl}/v1") };
        if (http is not null) options.Transport = new HttpClientPipelineTransport(http);
        return new ChatClient("m", new ApiKeyCredential("ollama"), options);
    }

    private static Task<string> CompleteAsync(ChatClient client) => LlmEndpointGrainUtils.CompleteOneShotAsync(
        NullLogger.Instance, Job, client, () => { }, Mock.Of<IAiObservabilityCapture>(), "p", "m", "http://gpu-box");

    private static IAsyncEnumerable<LlmGenerationEvent> Generate(ChatClient client) => LlmEndpointGrainUtils.GenerateAsync(
        NullLogger.Instance, Job, client, () => { }, Mock.Of<IAiObservabilityCapture>(), "p", "m", "http://gpu-box");

    private static string ClosedPortUrl(string scheme)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return $"{scheme}://127.0.0.1:{port}";
    }

    [Fact]
    public async Task OneShot_GoesThroughTheSidecar_LeavingTheNameForItToResolve()
    {
        await using var sidecar = new FakeSocksProxy { Body = Completion };

        var text = await CompleteAsync(Chat(Configured(("home", sidecar.Url)).ClientFor("home")));

        Assert.Equal("hi", text);
        Assert.Equal("gpu-box", Assert.Single(sidecar.Connects));
    }

    [Fact]
    public async Task Streaming_GoesThroughTheSidecar()
    {
        await using var sidecar = new FakeSocksProxy { Body = Stream.ReplaceLineEndings("\n"), ContentType = "text/event-stream" };

        var events = await Generate(Chat(Configured(("home", sidecar.Url)).ClientFor("home"))).ToListAsync();

        Assert.Equal("hi", Assert.Single(events).Data);
        Assert.Equal("gpu-box", Assert.Single(sidecar.Connects));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoTailnet_MeansConnectDirectly(string? tailnet)
        => Assert.Null(Configured(("home", "socks5://sidecar:1055")).ClientFor(tailnet));

    [Fact]
    public void Names_AreWhatConfigListsSorted()
        => Assert.Equal(new[] { "home", "work" }, Configured(("work", "socks5://b:1055"), ("home", "socks5://a:1055")).Names);

    [Fact]
    public void Names_AreEmptyWhenNothingIsConfigured()
        => Assert.Empty(Configured().Names);

    [Fact]
    public async Task HostDown_IsAHardFailure()
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => CompleteAsync(Chat(null, ClosedPortUrl("http"))));

        Assert.True(LlmEndpointGrainUtils.IsHardFailure(ex));
    }

    [Fact]
    public async Task SidecarDown_IsAHardFailure()
    {
        var http = Configured(("home", ClosedPortUrl("socks5"))).ClientFor("home");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => CompleteAsync(Chat(http)));

        Assert.True(LlmEndpointGrainUtils.IsHardFailure(ex));
    }

    [Fact]
    public async Task PeerUnreachable_IsAHardFailure()
    {
        await using var sidecar = new FakeSocksProxy { Reply = FakeSocksProxy.HostUnreachable };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => CompleteAsync(Chat(Configured(("home", sidecar.Url)).ClientFor("home"))));

        Assert.True(LlmEndpointGrainUtils.IsHardFailure(ex));
    }

    [Fact]
    public async Task TailnetNotConfigured_FailsRatherThanConnectingDirectly()
    {
        await using var sidecar = new FakeSocksProxy { Body = Completion };

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => CompleteAsync(Chat(Configured(("home", sidecar.Url)).ClientFor("work"))));

        Assert.True(LlmEndpointGrainUtils.IsHardFailure(ex));
        Assert.Contains("'work' is not configured", ex.Message);
        Assert.Empty(sidecar.Connects);
    }

    [Fact]
    public async Task SleepingPeer_TimesOutInsteadOfHanging()
    {
        await using var sidecar = new FakeSocksProxy { Reply = null };
        var http = Configured(("home", sidecar.Url)).ClientFor("home")!;

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => http.GetAsync("http://gpu-box:11434/api/tags").WaitAsync(TimeSpan.FromSeconds(30)));

        Assert.IsNotType<TimeoutException>(ex);
        Assert.True(LlmEndpointGrainUtils.IsHardFailure(ex));
    }

    [Fact]
    public async Task StreamDyingOnADeadPath_TellsTheRouter()
    {
        var reported = new List<Exception>();
        var http = Configured(("home", ClosedPortUrl("socks5"))).ClientFor("home");

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var _ in LlmEndpointGrainUtils.ReportingHardFailures(Generate(Chat(http)), ex =>
            {
                reported.Add(ex);
                return Task.CompletedTask;
            })) { }
        });

        Assert.Single(reported);
    }

    [Fact]
    public void UnrelatedAggregates_AreNotHardFailures()
    {
        var ex = new AggregateException(new TimeoutException(), new InvalidOperationException());

        Assert.False(LlmEndpointGrainUtils.IsHardFailure(ex));
    }
}
