using System.Runtime.CompilerServices;
using BackendTest.Fakes;
using Moq;
using Orleans.TestKit;
using PartyTown.Grains.Generation;

namespace BackendTest;

public class RouterGrainTest : TestKitBase
{

    private List<FakeEndpointGrain> SetupEndpoints(
        int count,
        JobComplexity supportedComplexities = JobComplexity.General,
        int[]? priorities = null,
        int[]? shares = null)
    {
        var endpoints = Enumerable.Range(0, count)
            .Select(_ => new FakeEndpointGrain(Guid.NewGuid(), supportedComplexities))
            .ToList();

        foreach (var ep in endpoints)
            ep.RegisterOn(Silo);

        var entries = endpoints.Select((ep, i) => new LlmProviderEntry
        {
            Priority = priorities?[i] ?? 0,
            Share = shares?[i] ?? Shares.Default,
            Id = ep.Id,
            Type = "ollama",
            BaseUrl = "http://fake",
            IsEnabled = true,
            SupportedComplexities = supportedComplexities,
        }).ToList();

        var configMock = Silo.AddProbe<ILlmProviderConfigGrain>(0);
        configMock
            .Setup(g => g.GetProvidersAsync())
            .ReturnsAsync(entries);
        configMock.Setup(g => g.GetPersonaRoutesAsync()).ReturnsAsync(new List<PersonaRoute>());

        return endpoints;
    }

    // TestKit probes are Castle proxies — GetPrimaryKey() throws on them. Compare by reference.
    private static FakeEndpointGrain FindEndpoint(
        List<FakeEndpointGrain> endpoints,
        ILlmEndpointGrain routedGrain)
        => endpoints.First(ep => ReferenceEquals(ep.Reference, routedGrain));

    [Fact]
    public async Task RouteAsync_KeepsDecisionsAndGenerationApart()
    {
        var talker = new FakeEndpointGrain(Guid.NewGuid(), JobComplexity.General);
        var decider = new FakeEndpointGrain(Guid.NewGuid(), JobComplexity.Decisions);
        talker.RegisterOn(Silo);
        decider.RegisterOn(Silo);
        Silo.AddProbe<ILlmProviderConfigGrain>(0)
            .Setup(g => g.GetProvidersAsync())
            .ReturnsAsync(new[] { talker, decider }.Select(ep => new LlmProviderEntry
            {
                Id = ep.Id,
                Type = "ollama",
                BaseUrl = "http://fake",
                SupportedComplexities = ep == talker ? JobComplexity.General : JobComplexity.Decisions,
            }).ToList());
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        Assert.Same(decider.Reference, await router.RouteAsync(JobComplexity.Decisions));
        Assert.Same(talker.Reference, await router.RouteAsync(JobComplexity.General));
    }

    [Fact]
    public async Task RouteAsync_ReturnsReferenceThatExposesRealEndpointMethods()
    {
        var endpoints = SetupEndpoints(1);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        var grain = await router.RouteAsync(JobComplexity.General);

        Assert.Same(endpoints[0].Reference, grain);

        var models = await grain.GetModelsAsync();
        Assert.Single(models);
        Assert.Equal(endpoints[0].Id, models[0].EndpointProviderGrainId);

        Assert.Equal(0, await grain.PressureAsync());

        var chunks = new List<LlmGenerationEvent>();
        await foreach (var e in grain.GenerateAsync(new LlmGenerationJob()))
        {
            chunks.Add(e);
        }

        Assert.NotEmpty(chunks);
        Assert.All(chunks, e => Assert.Equal(LlmGenerationEvent.ContentChunk, e.Type));

        // Pressure released after generation completes
        Assert.Equal(0, await grain.PressureAsync());
        Assert.Equal(1, endpoints[0].TotalJobsReceived);
    }

    private (FakeEndpointGrain Pool, FakeEndpointGrain Reserved) SetupReserved(
        Guid persona, bool enabled = true)
    {
        var pool = new FakeEndpointGrain(Guid.NewGuid(), JobComplexity.General);
        var reserved = new FakeEndpointGrain(Guid.NewGuid(), JobComplexity.General);
        pool.RegisterOn(Silo);
        reserved.RegisterOn(Silo);
        var config = Silo.AddProbe<ILlmProviderConfigGrain>(0);
        config.Setup(g => g.GetProvidersAsync()).ReturnsAsync(new List<LlmProviderEntry>
        {
            new()
            {
                Id = pool.Id, Type = "ollama", BaseUrl = "http://fake", IsEnabled = true,
                SupportedComplexities = JobComplexity.General,
            },
            new()
            {
                Id = reserved.Id, Type = "ollama", BaseUrl = "http://fake", IsEnabled = enabled,
                SupportedComplexities = JobComplexity.General, Reserved = true,
            },
        });
        config.Setup(g => g.GetPersonaRoutesAsync()).ReturnsAsync(new List<PersonaRoute>
        {
            new() { PersonaId = persona, Tier = JobComplexity.General, ProviderId = reserved.Id },
        });
        return (pool, reserved);
    }

    [Fact]
    public async Task RouteAsync_NeverPicksAReservedProvider()
    {
        var (pool, _) = SetupReserved(Guid.NewGuid());
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        for (var i = 0; i < 5; i++)
            Assert.Same(pool.Reference, await router.RouteAsync(JobComplexity.General));
    }

    [Fact]
    public async Task RouteAsync_NeverWaitsOnAReservedProvider()
    {
        var (_, reserved) = SetupReserved(Guid.NewGuid());
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        await router.RouteAsync(JobComplexity.General);

        Mock.Get(reserved.Reference).Verify(g => g.PressureAsync(), Times.Never);
    }

    [Fact]
    public async Task AssignedEndpoint_ReturnsTheProviderForThatPersonaAndTierOnly()
    {
        var persona = Guid.NewGuid();
        var (_, reserved) = SetupReserved(persona);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        Assert.Same(reserved.Reference, await router.AssignedEndpointAsync(persona, JobComplexity.General));
        Assert.Null(await router.AssignedEndpointAsync(persona, JobComplexity.CharacterVoice));
        Assert.Null(await router.AssignedEndpointAsync(Guid.NewGuid(), JobComplexity.General));
    }

    [Fact]
    public async Task AssignedEndpoint_FallsBackToThePoolWhenDisabled()
    {
        var persona = Guid.NewGuid();
        SetupReserved(persona, enabled: false);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        Assert.Null(await router.AssignedEndpointAsync(persona, JobComplexity.General));
    }

    [Fact]
    public async Task AssignedEndpoint_FallsBackToThePoolWhileBenched()
    {
        var persona = Guid.NewGuid();
        var (_, reserved) = SetupReserved(persona);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        await router.ReportFailureAsync(reserved.Id, "quota");

        Assert.Null(await router.AssignedEndpointAsync(persona, JobComplexity.General));
    }

    [Fact]
    public async Task RouteAsync_PrefersLowerPriorityNumber()
    {
        var endpoints = SetupEndpoints(2, priorities: new[] { 10, 0 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        Assert.Same(endpoints[1].Reference, await router.RouteAsync(JobComplexity.General));
    }

    [Fact]
    public async Task RouteAsync_SkipsBenchedProviderButFallsBackWhenAlone()
    {
        var endpoints = SetupEndpoints(2, priorities: new[] { 0, 10 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        await router.ReportFailureAsync(endpoints[0].Id, "402 out of credit");

        Assert.Same(endpoints[1].Reference, await router.RouteAsync(JobComplexity.General));

        await router.ReportFailureAsync(endpoints[1].Id, "host down");
        // Everyone benched: least-bad still routes rather than throwing.
        Assert.Same(endpoints[0].Reference, await router.RouteAsync(JobComplexity.General));
    }

    [Fact]
    public async Task RouteAsync_LeavesOfflineOut_EvenWhenItsAllThereIs()
    {
        var endpoints = SetupEndpoints(2, priorities: new[] { 0, 10 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        await router.ReportOfflineAsync(endpoints[0].Id, "connection refused");
        await router.ReportFailureAsync(endpoints[1].Id, "402 out of credit");

        // Benched still beats nothing; Offline never routes, so the caller can try its next tier.
        Assert.Same(endpoints[1].Reference, await router.RouteAsync(JobComplexity.General));
        await router.ReportOfflineAsync(endpoints[1].Id, "connection refused");
        await Assert.ThrowsAsync<InvalidOperationException>(() => router.RouteAsync(JobComplexity.General));
    }

    [Fact]
    public async Task CheckAvailability_MarksAnUnreachableProviderOffline()
    {
        var endpoints = SetupEndpoints(2, priorities: new[] { 0, 10 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        endpoints[0].Unreachable = "Connection refused (cachyos:11434)";

        await router.CheckAvailabilityAsync();

        Assert.Same(endpoints[1].Reference, await router.RouteAsync(JobComplexity.General));
        var offline = Assert.Single(await router.GetAvailabilityAsync(), a => a.ProviderId == endpoints[0].Id);
        Assert.Equal((ProviderState.Offline, "Connection refused (cachyos:11434)"), (offline.State, offline.Reason));
    }

    [Fact]
    public async Task CheckAvailability_TakesAProviderBackAsSoonAsItAnswers_BenchIncluded()
    {
        var endpoints = SetupEndpoints(2, priorities: new[] { 0, 10 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        await router.ReportFailureAsync(endpoints[0].Id, "timed out mid-call");
        endpoints[0].Unreachable = "down";
        await router.CheckAvailabilityAsync();

        endpoints[0].Unreachable = null;
        await router.CheckAvailabilityAsync();

        Assert.Same(endpoints[0].Reference, await router.RouteAsync(JobComplexity.General));
        Assert.All(await router.GetAvailabilityAsync(), a => Assert.Equal(ProviderState.Ready, a.State));
    }

    [Fact]
    public async Task CheckAvailability_KeepsABenchOnAProviderThatNeverWentAway()
    {
        var endpoints = SetupEndpoints(2, priorities: new[] { 0, 10 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        await router.ReportFailureAsync(endpoints[0].Id, "404 model not found");

        await router.CheckAvailabilityAsync();

        Assert.Same(endpoints[1].Reference, await router.RouteAsync(JobComplexity.General));
        var benched = Assert.Single(await router.GetAvailabilityAsync(), a => a.ProviderId == endpoints[0].Id);
        Assert.Equal((ProviderState.Benched, "404 model not found"), (benched.State, benched.Reason));
        Assert.NotNull(benched.Until);
    }

    [Fact]
    public async Task CheckAvailability_TreatsAPingThatThrowsAsOffline()
    {
        var endpoints = SetupEndpoints(1);
        Mock.Get(endpoints[0].Reference)
            .Setup(g => g.PingAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("silo said no"));
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        await router.CheckAvailabilityAsync();

        Assert.Equal(ProviderState.Offline, Assert.Single(await router.GetAvailabilityAsync()).State);
    }

    [Fact]
    public async Task AssignedEndpoint_FallsBackToThePoolWhileOffline()
    {
        var persona = Guid.NewGuid();
        var (_, reserved) = SetupReserved(persona);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        await router.ReportOfflineAsync(reserved.Id, "asleep");

        Assert.Null(await router.AssignedEndpointAsync(persona, JobComplexity.General));
    }

    [Fact]
    public async Task GenerateAsync_ConcurrentCalls_IncrementsPressure()
    {
        var endpoints = SetupEndpoints(1);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        var grain = await router.RouteAsync(JobComplexity.General);

        // Start an enumeration but don't drain it — pressure stays elevated until disposal.
        var enumerator = grain
            .GenerateAsync(new LlmGenerationJob())
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, await grain.PressureAsync());

        await enumerator.DisposeAsync();
        Assert.Equal(0, await grain.PressureAsync());
    }

    [Fact]
    public async Task RouteAsync_KeepsAPersonaOnOneProvider()
    {
        SetupEndpoints(3);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        var persona = Guid.NewGuid();

        var first = await router.RouteAsync(JobComplexity.General, persona);

        for (var i = 0; i < 10; i++)
            Assert.Same(first, await router.RouteAsync(JobComplexity.General, persona));
    }

    [Fact]
    public async Task RouteAsync_SplitsPersonasRoughlyByShare()
    {
        var endpoints = SetupEndpoints(2, shares: new[] { 75, 25 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        var onTheBigOne = 0;
        for (var i = 0; i < 400; i++)
            if (ReferenceEquals(endpoints[0].Reference, await router.RouteAsync(JobComplexity.General, Persona(i)))) onTheBigOne++;

        Assert.InRange(onTheBigOne, 260, 340);
    }

    [Fact]
    public async Task RouteAsync_OnlyMovesThePersonasOfAProviderThatGoesAway()
    {
        var endpoints = SetupEndpoints(3);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        var personas = Enumerable.Range(0, 60).Select(Persona).ToList();
        var before = new Dictionary<Guid, ILlmEndpointGrain>();
        foreach (var p in personas) before[p] = await router.RouteAsync(JobComplexity.General, p);

        await router.ReportOfflineAsync(endpoints[0].Id, "asleep");
        foreach (var p in personas.Where(p => !ReferenceEquals(before[p], endpoints[0].Reference)))
            Assert.Same(before[p], await router.RouteAsync(JobComplexity.General, p));

        // And they come home when it's back, so their prompt cache is where they left it.
        await router.CheckAvailabilityAsync();
        foreach (var p in personas)
            Assert.Same(before[p], await router.RouteAsync(JobComplexity.General, p));
    }

    [Fact]
    public async Task RouteAsync_PriorityStillBeatsShare()
    {
        var endpoints = SetupEndpoints(2, priorities: new[] { 0, 10 }, shares: new[] { 1, 100 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        for (var i = 0; i < 20; i++)
            Assert.Same(endpoints[0].Reference, await router.RouteAsync(JobComplexity.General, Persona(i)));
    }

    [Fact]
    public async Task RouteAsync_WorkNobodyOwnsGoesToTheBiggestShare()
    {
        var endpoints = SetupEndpoints(3, shares: new[] { 30, 50, 20 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        Assert.Same(endpoints[1].Reference, await router.RouteAsync(JobComplexity.General));
    }

    [Fact]
    public async Task RouteAsync_Decisions_TakeTurnsByShare_EvenForOnePersona()
    {
        var endpoints = SetupEndpoints(2, JobComplexity.Decisions, shares: new[] { 75, 25 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        var persona = Guid.NewGuid();

        var picks = new List<ILlmEndpointGrain>();
        for (var i = 0; i < 40; i++) picks.Add(await router.RouteAsync(JobComplexity.Decisions, persona));

        Assert.Equal(30, picks.Count(p => ReferenceEquals(p, endpoints[0].Reference)));
        // Spread through the run, not thirty of one and then ten of the other.
        Assert.Contains(picks.Take(4), p => ReferenceEquals(p, endpoints[1].Reference));
    }

    [Fact]
    public async Task RouteAsync_Decisions_OnlyTheBestStepTakesTurns()
    {
        var endpoints = SetupEndpoints(3, JobComplexity.Decisions, priorities: new[] { 0, 0, 10 });
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        for (var i = 0; i < 10; i++)
            Assert.NotSame(endpoints[2].Reference, await router.RouteAsync(JobComplexity.Decisions));

        await router.ReportFailureAsync(endpoints[0].Id, "timed out");
        for (var i = 0; i < 10; i++)
            Assert.Same(endpoints[1].Reference, await router.RouteAsync(JobComplexity.Decisions));
    }

    [Fact]
    public async Task GetAssignments_Decisions_AreBalancedUnlessPinned()
    {
        var persona = Guid.NewGuid();
        SetupEndpoints(2, JobComplexity.Decisions);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        var assignments = await router.GetAssignmentsAsync(new List<Guid> { persona });

        var decisions = Assert.Single(assignments, a => a.PersonaId == persona && a.Tier == JobComplexity.Decisions);
        Assert.Equal((null, true), (decisions.ProviderId, decisions.Balanced));
        // Nothing handles General here, so there is nothing to balance or land on.
        var general = Assert.Single(assignments, a => a.PersonaId == persona && a.Tier == JobComplexity.General);
        Assert.Equal((null, false), (general.ProviderId, general.Balanced));
    }

    [Fact]
    public async Task RouteAsync_NeverAsksAProviderHowBusyItIs()
    {
        var endpoints = SetupEndpoints(2);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        await router.RouteAsync(JobComplexity.General, Guid.NewGuid());

        Assert.All(endpoints, ep => Mock.Get(ep.Reference).Verify(g => g.PressureAsync(), Times.Never));
    }

    [Fact]
    public async Task GetAssignments_SaysWhereEachPersonaLandsAndWhatTheyArePinnedTo()
    {
        var pinned = Guid.NewGuid();
        var free = Guid.NewGuid();
        var (pool, reserved) = SetupReserved(pinned);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        var assignments = await router.GetAssignmentsAsync(new List<Guid> { pinned, free });

        ModelAssignment At(Guid persona, JobComplexity tier)
            => Assert.Single(assignments, a => a.PersonaId == persona && a.Tier == tier);
        Assert.Equal((reserved.Id, reserved.Id), (At(pinned, JobComplexity.General).ProviderId, At(pinned, JobComplexity.General).PinnedTo));
        Assert.Equal((pool.Id, null), (At(free, JobComplexity.General).ProviderId, At(free, JobComplexity.General).PinnedTo));
        // Nobody handles Voice here, and Guid.Empty is the work nobody owns.
        Assert.Null(At(free, JobComplexity.CharacterVoice).ProviderId);
        Assert.Equal(pool.Id, At(Guid.Empty, JobComplexity.General).ProviderId);
    }

    [Fact]
    public async Task GetAssignments_ShowsThePoolWhileAPinIsBenched()
    {
        var persona = Guid.NewGuid();
        var (pool, reserved) = SetupReserved(persona);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        await router.ReportFailureAsync(reserved.Id, "quota");

        var general = Assert.Single(
            await router.GetAssignmentsAsync(new List<Guid> { persona }),
            a => a.PersonaId == persona && a.Tier == JobComplexity.General);

        Assert.Equal((pool.Id, reserved.Id), (general.ProviderId, general.PinnedTo));
    }

    // Fixed ids, so a split test can't flake on an unlucky draw.
    private static Guid Persona(int i) => new(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
