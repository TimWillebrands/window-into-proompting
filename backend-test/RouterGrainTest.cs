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
        JobComplexity supportedComplexities = JobComplexity.General)
    {
        var endpoints = Enumerable.Range(0, count)
            .Select(_ => new FakeEndpointGrain(Guid.NewGuid(), supportedComplexities))
            .ToList();

        foreach (var ep in endpoints)
            ep.RegisterOn(Silo);

        var entries = endpoints.Select(ep => new LlmProviderEntry
        {
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

        return endpoints;
    }

    // TestKit probes are Castle proxies — GetPrimaryKey() throws on them. Compare by reference.
    private static FakeEndpointGrain FindEndpoint(
        List<FakeEndpointGrain> endpoints,
        ILlmEndpointGrain routedGrain)
        => endpoints.First(ep => ReferenceEquals(ep.Reference, routedGrain));

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
    public async Task RouteAsync_PrefersLowestPressureEndpoint()
    {
        var endpoints = SetupEndpoints(2);
        endpoints[0].HoldSlot();
        endpoints[0].HoldSlot();
        endpoints[0].HoldSlot();

        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);
        var routed = await router.RouteAsync(JobComplexity.General);

        Assert.Same(endpoints[1].Reference, routed);
        Assert.Equal(3, await endpoints[0].PressureAsync());
        Assert.Equal(0, await endpoints[1].PressureAsync());
    }

    [Fact]
    public async Task RouteAsync_DistributesJobsAcrossEndpoints()
    {
        const int jobCount = 30;
        const int endpointCount = 3;

        var endpoints = SetupEndpoints(endpointCount);
        var router = await Silo.CreateGrainAsync<LlmRouterGrain>(0);

        var routed = 0;
        for (var i = 0; i < jobCount; i++)
        {
            var grain = await router.RouteAsync(JobComplexity.General);
            var ep = FindEndpoint(endpoints, grain);
            ep.HoldSlot();
            routed++;

            // Simulate completions every 5 jobs: drain the busiest endpoint
            if (routed % 5 == 0)
            {
                var pressures = await Task.WhenAll(
                    endpoints.Select(async e => new { ep = e, pressure = await e.PressureAsync() }));
                var busiest = pressures
                    .Where(x => x.pressure > 0)
                    .OrderByDescending(x => x.pressure)
                    .FirstOrDefault();
                busiest?.ep.ReleaseSlot();
            }
        }

        Assert.Equal(jobCount, endpoints.Sum(ep => ep.AssignedSlots));
        Assert.All(endpoints, ep => Assert.True(ep.AssignedSlots > 0,
            $"Endpoint {ep.Id} received no jobs"));

        // Tie-breaking by source order means ep[0] wins every tie, skewing its count slightly
        // above fair share. Allow 50% headroom to catch real imbalances without false failures.
        var fairShare = (int)Math.Ceiling((double)jobCount / endpointCount);
        var maxAllowed = (int)Math.Ceiling(fairShare * 1.5);
        Assert.All(endpoints, ep => Assert.True(ep.AssignedSlots <= maxAllowed,
            $"Endpoint {ep.Id} received {ep.AssignedSlots} jobs, exceeding 1.5x fair share of {maxAllowed}"));
    }
}
