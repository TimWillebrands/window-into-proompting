using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World;

namespace BackendTest;

/// <summary>Tier fallback, accumulation and the JSON parse ladder — the bits every LLM caller used to copy.</summary>
public class LlmCallTest
{
    private static async IAsyncEnumerable<LlmGenerationEvent> Chunks(
        IEnumerable<LlmGenerationEvent> events, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in events)
        {
            await Task.Yield();
            yield return e;
        }
    }

    private static Mock<ILlmEndpointGrain> Endpoint(Action<LlmGenerationJob>? seen = null, bool attributionThrows = false)
    {
        var ep = new Mock<ILlmEndpointGrain>();
        ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
            {
                seen?.Invoke(job);
                return Chunks(new[]
                {
                    new LlmGenerationEvent(LlmGenerationEvent.ReasoningChunk, "hm"),
                    new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "Hel"),
                    new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "lo"),
                }, ct);
            });
        if (attributionThrows)
            ep.Setup(e => e.GetAttributionAsync()).ThrowsAsync(new Exception("down"));
        else
            ep.Setup(e => e.GetAttributionAsync()).ReturnsAsync(new ChatMessageMetadata { Provider = "p", ModelName = "m" });
        return ep;
    }

    [Fact]
    public async Task Route_ForAPersona_PrefersTheirAssignedProvider()
    {
        var persona = Guid.NewGuid();
        var assigned = Endpoint().Object;
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.AssignedEndpointAsync(persona, JobComplexity.CharacterVoice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned);

        var (endpoint, served) = await new LlmCall(router.Object, persona: persona).RouteAsync(LlmTiers.Voice, default);

        Assert.Same(assigned, endpoint);
        Assert.Equal(JobComplexity.CharacterVoice, served);
        router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WorldLlm_For_RoutesAsThatPersona()
    {
        var persona = Guid.NewGuid();
        var assigned = new Mock<ILlmEndpointGrain>();
        assigned.Setup(e => e.CompleteOneShotAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>())).ReturnsAsync("hi");
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.AssignedEndpointAsync(persona, JobComplexity.CharacterVoice, It.IsAny<CancellationToken>()))
            .ReturnsAsync(assigned.Object);
        var grains = new Mock<IGrainFactory>();
        grains.Setup(g => g.GetGrain<ILlmRouterGrain>(0, null)).Returns(router.Object);
        var llm = new WorldLlm(grains.Object, NullLogger<WorldLlm>.Instance);

        var text = await llm.For(persona).CompleteAsync(JobComplexity.CharacterVoice, "s", "u", default);

        Assert.Equal("hi", text);
        router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Route_ForAPersonaWithoutAssignment_UsesThePool()
    {
        var pool = Endpoint().Object;
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(JobComplexity.CharacterVoice, It.IsAny<CancellationToken>())).ReturnsAsync(pool);

        var (endpoint, _) = await new LlmCall(router.Object, persona: Guid.NewGuid()).RouteAsync(LlmTiers.Voice, default);

        Assert.Same(pool, endpoint);
    }

    [Fact]
    public async Task Route_WithoutAPersona_NeverAsksForAnAssignment()
    {
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>())).ReturnsAsync(Endpoint().Object);

        await new LlmCall(router.Object).RouteAsync(LlmTiers.General, default);

        router.Verify(r => r.AssignedEndpointAsync(It.IsAny<Guid>(), It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Stream_FallsBackDownTheTierList_AndTagsTheJobWithTheServedTier()
    {
        LlmGenerationJob? job = null;
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(JobComplexity.CharacterVoice, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no voice tier"));
        router.Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Endpoint(j => job = j).Object);
        var chunks = new List<string>();

        var result = await new LlmCall(router.Object).StreamAsync(
            new LlmGenerationJob(), LlmTiers.Voice, CancellationToken.None,
            c => { chunks.Add(c.Data); return Task.CompletedTask; });

        Assert.Equal(("Hello", "hm", JobComplexity.General), (result.Content, result.Reasoning, result.Served));
        Assert.Equal(JobComplexity.General, job!.JobComplexity);
        Assert.Equal(new[] { "hm", "Hel", "lo" }, chunks);
        Assert.Equal("m", result.Attribution?.ModelName);
    }

    [Fact]
    public async Task Route_LastTierFailureSurfaces_AttributionFailureDoesNot()
    {
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("nothing"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LlmCall(router.Object).StreamAsync(new LlmGenerationJob(), LlmTiers.ThoughtsOnly, CancellationToken.None));

        var ok = new Mock<ILlmRouterGrain>();
        ok.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Endpoint(attributionThrows: true).Object);
        var result = await new LlmCall(ok.Object).StreamAsync(new LlmGenerationJob(), LlmTiers.General, CancellationToken.None);
        Assert.Equal("Hello", result.Content);
        Assert.Null(result.Attribution);
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> Broken(
        int afterChunks, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < afterChunks; i++)
        {
            await Task.Yield();
            yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "Hel");
        }
        await Task.Yield();
        throw new HttpRequestException("connection refused");
    }

    private static Mock<ILlmEndpointGrain> BrokenEndpoint(int afterChunks = 0)
    {
        var ep = new Mock<ILlmEndpointGrain>();
        ep.Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => Broken(afterChunks, ct));
        return ep;
    }

    [Fact]
    public async Task Stream_ThatDiesBeforeItsFirstChunk_MovesOnToTheNextProvider()
    {
        var dead = BrokenEndpoint();
        var router = new Mock<ILlmRouterGrain>();
        router.SetupSequence(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dead.Object)
            .ReturnsAsync(Endpoint().Object);
        var chunks = new List<string>();

        var result = await new LlmCall(router.Object).StreamAsync(
            new LlmGenerationJob(), LlmTiers.General, CancellationToken.None,
            c => { chunks.Add(c.Data); return Task.CompletedTask; });

        Assert.Equal("Hello", result.Content);
        Assert.Equal(new[] { "hm", "Hel", "lo" }, chunks);
    }

    [Fact]
    public async Task Stream_ThatDiesBeforeItsFirstChunk_FallsToTheNextTier_WhenItsTierHasNoOneElse()
    {
        var dead = BrokenEndpoint();
        var router = new Mock<ILlmRouterGrain>();
        // A benched provider alone in its tier still routes, so the call has to step past it itself.
        router.Setup(r => r.RouteAsync(JobComplexity.CharacterVoice, It.IsAny<CancellationToken>())).ReturnsAsync(dead.Object);
        router.Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>())).ReturnsAsync(Endpoint().Object);

        var result = await new LlmCall(router.Object).StreamAsync(new LlmGenerationJob(), LlmTiers.Voice, CancellationToken.None);

        Assert.Equal(("Hello", JobComplexity.General), (result.Content, result.Served));
    }

    [Fact]
    public async Task Stream_WithNoOneLeft_SurfacesTheProvidersOwnFailure()
    {
        var dead = BrokenEndpoint();
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>())).ReturnsAsync(dead.Object);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new LlmCall(router.Object).StreamAsync(new LlmGenerationJob(), LlmTiers.General, CancellationToken.None));

        dead.Verify(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stream_ThatDiesMidway_IsNotRetried_SinceTheCallerAlreadySawChunks()
    {
        var router = new Mock<ILlmRouterGrain>();
        router.SetupSequence(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrokenEndpoint(afterChunks: 1).Object)
            .ReturnsAsync(Endpoint().Object);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new LlmCall(router.Object).StreamAsync(new LlmGenerationJob(), LlmTiers.General, CancellationToken.None));

        router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Stream_ForAPersona_StepsFromTheirDeadModelToThePool()
    {
        var persona = Guid.NewGuid();
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.AssignedEndpointAsync(persona, JobComplexity.General, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BrokenEndpoint().Object);
        router.Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>())).ReturnsAsync(Endpoint().Object);

        var result = await new LlmCall(router.Object, persona: persona)
            .StreamAsync(new LlmGenerationJob(), LlmTiers.General, CancellationToken.None);

        Assert.Equal("Hello", result.Content);
    }

    private sealed record Pick(string? Name, int Score);

    [Theory]
    [InlineData("""{"name":"a","score":2}""")]
    [InlineData("```json\n{\"name\":\"a\",\"score\":2}\n```")]
    [InlineData("Sure! {\"name\":\"a\",\"score\":2,}")]
    public void TryDeserialize_ClimbsTheLadder(string raw)
        => Assert.Equal(new Pick("a", 2), LlmJsonParsing.TryDeserialize<Pick>(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    [Fact]
    public void TryDeserialize_NullWhenHopeless()
        => Assert.Null(LlmJsonParsing.TryDeserialize<Pick>("no json here", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
