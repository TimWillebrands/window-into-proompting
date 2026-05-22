using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="PersonaSalienceService"/> — the stop-signal salience classifier
/// used by <c>PersonaGrain</c>'s race trigger to decide whether a new message warrants
/// interrupting an in-flight generation.
///
/// Coverage:
///   • Well-formed JSON parses to the expected (Salience, Kind) tuple.
///   • Salience values outside [0, 1] are clamped (cheap models occasionally emit
///     2.0 or -0.3 despite the strict schema).
///   • Markdown-fenced JSON ( ```json … ``` ) is unwrapped via the shared cleanup helper.
///   • Routing failure → <see cref="SalienceScore.LetItRide"/> (no cancel, no repair) —
///     the race code MUST NOT cancel an in-flight gen because the cheap model is missing.
///   • Generation exception (post-routing) → also <see cref="SalienceScore.LetItRide"/>.
///   • Unparseable JSON → <see cref="SalienceScore.LetItRide"/>.
///   • Routing requests <see cref="JobComplexity.CharacterThoughts"/> (the cheap-model lane).
///
/// Strategy mirrors PersonaDecisionServiceTest: mock the router + endpoint, script JSON
/// chunks via a single-yield IAsyncEnumerable, NullLogger.
/// </summary>
public class PersonaSalienceServiceTest
{
    private static GenerationParticipant MakeSelf() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Vlad",
        Bio = "Reluctant philosopher",
        Impulsivity = 0.3,
    };

    private static ChatMessage NewMessage(string content) => new()
    {
        MessageId = 99,
        SenderId = Guid.NewGuid(),
        SenderType = "user",
        Content = content,
    };

    private static async IAsyncEnumerable<LlmGenerationEvent> SingleChunk(
        string data,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, data);
    }

    private static (Mock<ILlmRouterGrain> router, Mock<ILlmEndpointGrain> endpoint) MakeRouter(string json)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SingleChunk(json, ct));
        endpoint
            .Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "lfm2-test" });

        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
        return (router, endpoint);
    }

    private static PersonaSalienceService MakeService(string json)
    {
        var (router, _) = MakeRouter(json);
        return new PersonaSalienceService(router.Object, NullLogger.Instance);
    }

    [Fact]
    public async Task ScoreAsync_WellFormedJson_ParsesValueAndKind()
    {
        var service = MakeService("""{"salience":0.82,"kind":"redirect"}""");
        var result = await service.ScoreAsync(
            MakeSelf(), "I was about to defend it.", "Defend it.", "I think it's actua",
            NewMessage("forget that — it's been deprecated"), "Mira",
            CancellationToken.None);

        Assert.Equal(0.82, result.Value, precision: 3);
        Assert.Equal("redirect", result.Kind);
    }

    [Fact]
    public async Task ScoreAsync_SalienceAbove1_IsClampedTo1()
    {
        // Cheap models occasionally hallucinate magnitudes (2.0, "high", etc.) despite the
        // strict schema. The race math depends on a clean unit interval — clamp on the way out.
        var service = MakeService("""{"salience":2.4,"kind":"contradict"}""");
        var result = await service.ScoreAsync(
            MakeSelf(), "go", "go", "",
            NewMessage("don't"), "Mira",
            CancellationToken.None);

        Assert.Equal(1.0, result.Value);
    }

    [Fact]
    public async Task ScoreAsync_SalienceBelow0_IsClampedTo0()
    {
        var service = MakeService("""{"salience":-0.5,"kind":"irrelevant"}""");
        var result = await service.ScoreAsync(
            MakeSelf(), "go", "go", "",
            NewMessage("unrelated"), "Mira",
            CancellationToken.None);

        Assert.Equal(0.0, result.Value);
    }

    [Fact]
    public async Task ScoreAsync_MarkdownFencedJson_ParsesViaCleanupHelper()
    {
        // Cheap models still wrap structured output in ```json … ``` despite the schema.
        // The shared LlmJsonParsing.ExtractJsonPayload strips the fence before parsing.
        var service = MakeService("```json\n{\"salience\":0.4,\"kind\":\"tangent\"}\n```");
        var result = await service.ScoreAsync(
            MakeSelf(), "g", "g", "",
            NewMessage("anyway"), "Mira",
            CancellationToken.None);

        Assert.Equal(0.4, result.Value, precision: 3);
        Assert.Equal("tangent", result.Kind);
    }

    [Fact]
    public async Task ScoreAsync_UnparseableJson_ReturnsLetItRide()
    {
        // Garbage from the model must NOT cancel an in-flight generation. Defaulting
        // to LetItRide preserves current behavior rather than introducing a new failure.
        var service = MakeService("<<error: provider returned 503>>");
        var result = await service.ScoreAsync(
            MakeSelf(), "g", "g", "",
            NewMessage("hi"), "Mira",
            CancellationToken.None);

        Assert.Equal(SalienceScore.LetItRide, result);
    }

    [Fact]
    public async Task ScoreAsync_RoutingFailure_ReturnsLetItRide()
    {
        // No CharacterThoughts-capable provider configured → router throws.
        // Race code MUST treat this as "let it ride" so an unconfigured cheap model
        // doesn't spuriously cancel every in-flight generation.
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no provider supports CharacterThoughts"));

        var service = new PersonaSalienceService(router.Object, NullLogger.Instance);
        var result = await service.ScoreAsync(
            MakeSelf(), "g", "g", "",
            NewMessage("hi"), "Mira",
            CancellationToken.None);

        Assert.Equal(SalienceScore.LetItRide, result);
    }

    [Fact]
    public async Task ScoreAsync_GenerationException_ReturnsLetItRide()
    {
        // Routing succeeded but the LLM call itself blew up (network, parse error inside
        // the endpoint, etc.). Same conservative fallback.
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, _) => ThrowingEnumerable());
        endpoint
            .Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "lfm2-test" });

        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);

        var service = new PersonaSalienceService(router.Object, NullLogger.Instance);
        var result = await service.ScoreAsync(
            MakeSelf(), "g", "g", "",
            NewMessage("hi"), "Mira",
            CancellationToken.None);

        Assert.Equal(SalienceScore.LetItRide, result);

        static async IAsyncEnumerable<LlmGenerationEvent> ThrowingEnumerable()
        {
            await Task.Yield();
            throw new InvalidOperationException("upstream broke");
#pragma warning disable CS0162 // Unreachable code — required to make the method an iterator.
            yield break;
#pragma warning restore CS0162
        }
    }

    [Fact]
    public async Task ScoreAsync_RoutesViaCharacterThoughtsComplexity()
    {
        // Contract: salience scoring MUST request the cheap-model lane via
        // JobComplexity.CharacterThoughts. If this changes, provider configuration
        // (LlmProviderEntry.SupportedComplexities) and the deployment guidance break.
        JobComplexity? requested = null;
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) =>
                SingleChunk("""{"salience":0.1,"kind":"tangent"}""", ct));
        endpoint
            .Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "lfm2-test" });

        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .Callback<JobComplexity, CancellationToken>((c, _) => requested = c)
            .ReturnsAsync(endpoint.Object);

        var service = new PersonaSalienceService(router.Object, NullLogger.Instance);
        await service.ScoreAsync(
            MakeSelf(), "g", "g", "",
            NewMessage("hi"), "Mira",
            CancellationToken.None);

        Assert.Equal(JobComplexity.CharacterThoughts, requested);
    }
}
