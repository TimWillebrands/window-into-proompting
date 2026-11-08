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
    private readonly List<PersonaRoute> _routes = new();
    private static readonly Guid Vlad = Guid.NewGuid();

    private void AssignVlad(Mock<ILlmEndpointGrain>? endpoint)
    {
        _routes.Add(new PersonaRoute { PersonaId = Vlad, Tier = JobComplexity.Decisions, ProviderId = Guid.NewGuid() });
        _router.Setup(r => r.AssignedEndpointAsync(Vlad, JobComplexity.Decisions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint?.Object);
    }

    private ProviderJudge Judge()
    {
        var grains = new Mock<IGrainFactory>();
        grains.Setup(g => g.GetGrain<ILlmRouterGrain>(0, null)).Returns(_router.Object);
        grains.Setup(g => g.GetGrain<ILlmProviderConfigGrain>(0, null)).Returns(_config.Object);
        return new ProviderJudge(grains.Object, new LlmJudge(grains.Object, NullLogger<LlmJudge>.Instance), NullLogger<ProviderJudge>.Instance);
    }

    private async Task<ProviderJudge> JudgeWithProvidersAsync(params LlmProviderEntry[] providers)
    {
        _config.Setup(c => c.GetProvidersAsync()).ReturnsAsync(providers.ToList());
        _config.Setup(c => c.GetPersonaRoutesAsync()).ReturnsAsync(_routes);
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

    /// <summary>The General tier answers like the emulated judge would: plain JSON probabilities.</summary>
    private Mock<ILlmEndpointGrain> EmulationAnswers(string json = """{"x":0.3}""")
    {
        var llm = new Mock<ILlmEndpointGrain>();
        llm.Setup(e => e.CompleteOneShotAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(json);
        _router.Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>())).ReturnsAsync(llm.Object);
        return llm;
    }

    [Fact]
    public async Task Ask_EmulatesWithAnLlm_WhenTheRouterHasNothingNewLeft()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        var only = Endpoint(failure: new HttpRequestException("402 Payment Required"));
        Routes(only, only);
        EmulationAnswers();

        var answers = await judge.AskAsync("state", Questions, CancellationToken.None);

        Assert.Equal(0.3, answers.Noul("x", 0));
        only.Verify(e => e.JudgeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ask_EmulatesWithAnLlm_WhenEveryDecisionModelIsOffline()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        _router.Setup(r => r.RouteAsync(JobComplexity.Decisions, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No model-providers available for job complexity Decisions"));
        EmulationAnswers();

        Assert.Equal(0.3, (await judge.AskAsync("state", Questions, CancellationToken.None)).Noul("x", 0));
    }

    [Fact]
    public async Task Ask_SurfacesTheEmulationsFailure_WhenThatFailsToo()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        var only = Endpoint(failure: new HttpRequestException("down"));
        Routes(only, only);
        EmulationAnswers().Setup(e => e.CompleteOneShotAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("haiku too"));

        await Assert.ThrowsAsync<TimeoutException>(() => judge.AskAsync("state", Questions, CancellationToken.None));
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
    public async Task For_AsksThePersonasOwnModel_NotThePool()
    {
        var own = Endpoint(Answer);
        AssignVlad(own);
        var judge = await JudgeWithProvidersAsync(Provider());

        await judge.For(Vlad).AskAsync("state", Questions, CancellationToken.None);

        own.Verify(e => e.JudgeAsync("state", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task For_FallsBackToThePool_WhenTheirModelFails()
    {
        AssignVlad(Endpoint(failure: new HttpRequestException("down")));
        var judge = await JudgeWithProvidersAsync(Provider());
        Routes(Endpoint(Answer));

        var answers = await judge.For(Vlad).AskAsync("state", Questions, CancellationToken.None);

        Assert.Equal(0.8, answers.Noul("x", 0));
    }

    [Fact]
    public async Task For_UsesThePool_WhenTheirModelIsBenchedOrOff()
    {
        AssignVlad(null);
        var judge = await JudgeWithProvidersAsync(Provider());
        Routes(Endpoint(Answer));

        var answers = await judge.For(Vlad).AskAsync("state", Questions, CancellationToken.None);

        Assert.Equal(0.8, answers.Noul("x", 0));
    }

    [Fact]
    public async Task For_WithoutARoute_UsesThePool()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        Routes(Endpoint(Answer));

        var answers = await judge.For(Guid.NewGuid()).AskAsync("state", Questions, CancellationToken.None);

        Assert.Equal(0.8, answers.Noul("x", 0));
    }

    [Fact]
    public async Task For_AReservedModelAlone_AnswersForThatPersona()
    {
        AssignVlad(Endpoint(Answer));
        var judge = await JudgeWithProvidersAsync(Provider(reserved: true));

        Assert.Equal(0.8, (await judge.For(Vlad).AskAsync("state", Questions, CancellationToken.None)).Noul("x", 0));
    }

    [Fact]
    public async Task For_AReservedModelAlone_EmulatesWhenItFails_WithNoPoolToFallBackTo()
    {
        AssignVlad(Endpoint(failure: new HttpRequestException("down")));
        var judge = await JudgeWithProvidersAsync(Provider(reserved: true));
        EmulationAnswers();

        Assert.Equal(0.3, (await judge.For(Vlad).AskAsync("state", Questions, CancellationToken.None)).Noul("x", 0));
        _router.Verify(r => r.RouteAsync(JobComplexity.Decisions, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void For_LeavesOtherJudgesAlone()
    {
        var other = Mock.Of<ISystemOneJudge>();

        Assert.Same(other, other.For(Vlad));
    }

    [Fact]
    public async Task Ask_EmulatesWithoutAPoolDecisionsProvider()
    {
        foreach (var provider in new[] { Provider(JobComplexity.General), Provider(enabled: false), Provider(reserved: true) })
        {
            var judge = await JudgeWithProvidersAsync(provider);
            EmulationAnswers();

            Assert.Equal(0.3, (await judge.AskAsync("state", Questions, CancellationToken.None)).Noul("x", 0));
        }
        _router.Verify(r => r.RouteAsync(JobComplexity.Decisions, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ask_FollowsConfigChanges()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        _config.Setup(c => c.GetProvidersAsync()).ReturnsAsync(new List<LlmProviderEntry>());
        await judge.RefreshAsync();
        EmulationAnswers();

        Assert.Equal(0.3, (await judge.AskAsync("state", Questions, CancellationToken.None)).Noul("x", 0));
        _router.Verify(r => r.RouteAsync(JobComplexity.Decisions, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Ask_KeepsTheLastProvidersWhenConfigCantBeRead()
    {
        var judge = await JudgeWithProvidersAsync(Provider());
        _config.Setup(c => c.GetProvidersAsync()).ThrowsAsync(new TimeoutException());
        await judge.RefreshAsync();
        Routes(Endpoint(Answer));

        Assert.Equal(0.8, (await judge.AskAsync("state", Questions, CancellationToken.None)).Noul("x", 0));
    }
}
