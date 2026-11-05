using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Services.SystemOne;

namespace BackendTest;

/// <summary>System One rides the Decisions provider tier: it routes, falls through and turns itself off with the config.</summary>
public class ProviderJudgeTest
{
    private const string Answer = """{"answers":{"x":{"type":"noul","noul":0.8}}}""";

    private static readonly Dictionary<string, SystemOneQuestion> Questions = new() { ["x"] = new NoulQuestion("x") };

    private readonly Mock<ILlmRouterGrain> _router = new();
    private readonly Mock<ILlmProviderConfigGrain> _config = new();

    private ProviderJudge Judge(bool emulate = false)
    {
        var grains = new Mock<IGrainFactory>();
        grains.Setup(g => g.GetGrain<ILlmRouterGrain>(0, null)).Returns(_router.Object);
        grains.Setup(g => g.GetGrain<ILlmProviderConfigGrain>(0, null)).Returns(_config.Object);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SYSTEM_ONE_EMULATE"] = emulate.ToString() })
            .Build();
        return new ProviderJudge(grains.Object,
            new LlmJudge(grains.Object, config, NullLogger<LlmJudge>.Instance),
            NullLogger<ProviderJudge>.Instance);
    }

    private async Task<ProviderJudge> JudgeWithProvidersAsync(params LlmProviderEntry[] providers)
    {
        _config.Setup(c => c.GetProvidersAsync()).ReturnsAsync(providers.ToList());
        var judge = Judge();
        await judge.RefreshAsync();
        return judge;
    }

    private static LlmProviderEntry Provider(
        JobComplexity tiers = JobComplexity.Decisions, bool enabled = true, bool reserved = false) => new()
    {
        Type = "ollama", BaseUrl = "http://a", SupportedComplexities = tiers, IsEnabled = enabled, Reserved = reserved,
    };

    private static Mock<ILlmEndpointGrain> Endpoint(string? answer = null, Exception? failure = null)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        var call = endpoint.Setup(e => e.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (failure is not null) call.ThrowsAsync(failure);
        else call.ReturnsAsync(answer!);
        return endpoint;
    }

    private void Routes(params Mock<ILlmEndpointGrain>[] endpoints)
    {
        var routes = _router.SetupSequence(r => r.RouteAsync(JobComplexity.Decisions, It.IsAny<CancellationToken>()));
        foreach (var endpoint in endpoints) routes = routes.ReturnsAsync(endpoint.Object);
    }

    [Fact]
    public async Task Ask_GoesToTheRoutedProviderAndParsesItsAnswer()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        var endpoint = Endpoint(Answer);
        Routes(endpoint);

        var answers = await judge.AskAsync("state", Questions, CancellationToken.None);

        Assert.Equal(0.8, answers.Noul("x", 0));
        endpoint.Verify(e => e.JudgeAsync("state", It.Is<string>(q => q.Contains("noul")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_FallsThroughToTheNextProviderWithinTheSameCall()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        Routes(Endpoint(failure: new HttpRequestException("down")), Endpoint(Answer));

        var answers = await judge.AskAsync("state", Questions, CancellationToken.None);

        Assert.Equal(0.8, answers.Noul("x", 0));
    }

    [Fact]
    public async Task Ask_GivesUpWhenTheRouterHasNothingNewLeft()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        var only = Endpoint(failure: new HttpRequestException("down"));
        Routes(only, only);

        await Assert.ThrowsAsync<HttpRequestException>(() => judge.AskAsync("state", Questions, CancellationToken.None));

        only.Verify(e => e.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_StopsWhenTheCallerCancels()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        using var cts = new CancellationTokenSource();
        var cancelled = new Mock<ILlmEndpointGrain>();
        cancelled.Setup(e => e.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<string>(new OperationCanceledException(cts.Token));
            });
        var next = Endpoint(Answer);
        Routes(cancelled, next);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => judge.AskAsync("state", Questions, cts.Token));

        next.Verify(e => e.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enabled_WithAnEnabledDecisionsProvider()
        => Assert.True((await JudgeWithProvidersAsync(Provider(), Provider(JobComplexity.General))).Enabled);

    [Fact]
    public async Task Enabled_NotWithoutOne()
        => Assert.False((await JudgeWithProvidersAsync(Provider(JobComplexity.General))).Enabled);

    [Fact]
    public async Task Enabled_NotWhenTheOnlyOneIsDisabled()
        => Assert.False((await JudgeWithProvidersAsync(Provider(enabled: false))).Enabled);

    [Fact]
    public async Task Enabled_NotWhenTheOnlyOneIsReserved_SinceThePoolCantReachIt()
        => Assert.False((await JudgeWithProvidersAsync(Provider(reserved: true))).Enabled);

    [Fact]
    public async Task Enabled_FollowsConfigChanges()
    {
        var judge = await JudgeWithProvidersAsync(Provider());

        _config.Setup(c => c.GetProvidersAsync()).ReturnsAsync(new List<LlmProviderEntry>());
        await judge.RefreshAsync();

        Assert.False(judge.Enabled);
    }

    [Fact]
    public async Task Enabled_KeepsTheLastAnswerWhenConfigCantBeRead()
    {
        var judge = await JudgeWithProvidersAsync(Provider());

        _config.Setup(c => c.GetProvidersAsync()).ThrowsAsync(new TimeoutException());
        await judge.RefreshAsync();

        Assert.True(judge.Enabled);
    }

    [Fact]
    public void Enabled_WithEmulationAlone()
        => Assert.True(Judge(emulate: true).Enabled);
}
