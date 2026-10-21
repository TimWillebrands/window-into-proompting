using System.Runtime.CompilerServices;
using System.Text.Json;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

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
