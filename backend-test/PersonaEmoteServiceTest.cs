using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Generation;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="PersonaEmoteService"/> — the abandonment-line generator that
/// produces an in-character italic action when the stop-signal race cancels an
/// in-flight generation.
///
/// Coverage:
///   • Empty draft → cheap canned <see cref="PersonaEmoteService.DecisionPhaseFallback"/>
///     (no LFM call burned when there's nothing to summarize).
///   • Well-formed model output is sanitized (markdown fences stripped, quotes stripped,
///     length-clamped) and returned.
///   • Routing failure → <see cref="PersonaEmoteService.GenerationFailureFallback"/>.
///   • Generation exception → <see cref="PersonaEmoteService.GenerationFailureFallback"/>.
///   • Empty/whitespace model output → <see cref="PersonaEmoteService.GenerationFailureFallback"/>
///     (cancel path must always produce a renderable line).
///   • Routing requests <see cref="JobComplexity.CharacterThoughts"/> (cheap-model lane).
/// </summary>
public class PersonaEmoteServiceTest
{
    private static GenerationParticipant MakeSelf() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Vlad",
        Bio = "Reluctant philosopher",
        Impulsivity = 0.3,
    };

    private static async IAsyncEnumerable<LlmGenerationEvent> SingleChunk(
        string data,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, data);
    }

    private static (Mock<ILlmRouterGrain> router, Mock<ILlmEndpointGrain> endpoint) MakeRouter(string output)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SingleChunk(output, ct));
        endpoint
            .Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "lfm2-test" });

        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
        return (router, endpoint);
    }

    private static PersonaEmoteService MakeService(string output)
    {
        var (router, _) = MakeRouter(output);
        return new PersonaEmoteService(router.Object, NullLogger.Instance);
    }

    [Fact]
    public async Task GenerateAbandonmentEmoteAsync_EmptyDraft_ReturnsDecisionPhaseFallbackWithoutLlmCall()
    {
        // Decision-phase cancel: nothing has been "drafted" yet so there's nothing to summarize.
        // Expectation: skip the LFM call and return the canned line.
        var router = new Mock<ILlmRouterGrain>(MockBehavior.Strict);
        // Strict: any RouteAsync invocation will throw — proves we didn't call it.

        var service = new PersonaEmoteService(router.Object, NullLogger.Instance);
        var result = await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: string.Empty,
            interruptingMessage: "anything",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.Equal(PersonaEmoteService.DecisionPhaseFallback, result);
        router.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateAbandonmentEmoteAsync_WellFormedOutput_ReturnsTrimmedLine()
    {
        var service = MakeService("*clears throat, decides against it*");
        var result = await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: "I think the real answer is",
            interruptingMessage: "actually never mind",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.Equal("*clears throat, decides against it*", result);
    }

    [Fact]
    public async Task GenerateAbandonmentEmoteAsync_StripsMarkdownFenceAndQuotes()
    {
        // Models occasionally wrap output in fences or quotes despite the "no quotes" cue.
        var service = MakeService("```\n\"*falls quiet, listening*\"\n```");
        var result = await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: "Well, I",
            interruptingMessage: "wait, hold on",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.Equal("*falls quiet, listening*", result);
    }

    [Fact]
    public async Task GenerateAbandonmentEmoteAsync_CollapsesMultilineToSingleLine()
    {
        // Emotes are one beat. Multi-line output is collapsed so the chat doesn't get a paragraph.
        var service = MakeService("*sets the cup down*\n\nAnd that's all\nI'll say.");
        var result = await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: "Look,",
            interruptingMessage: "stop",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.DoesNotContain('\n', result);
    }

    [Fact]
    public async Task GenerateAbandonmentEmoteAsync_RoutingFailure_ReturnsLiteralFallback()
    {
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no provider supports CharacterThoughts"));

        var service = new PersonaEmoteService(router.Object, NullLogger.Instance);
        var result = await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: "I think",
            interruptingMessage: "stop",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.Equal(PersonaEmoteService.GenerationFailureFallback, result);
    }

    [Fact]
    public async Task GenerateAbandonmentEmoteAsync_GenerationException_ReturnsLiteralFallback()
    {
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

        var service = new PersonaEmoteService(router.Object, NullLogger.Instance);
        var result = await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: "I think",
            interruptingMessage: "stop",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.Equal(PersonaEmoteService.GenerationFailureFallback, result);

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
    public async Task GenerateAbandonmentEmoteAsync_EmptyOutput_ReturnsLiteralFallback()
    {
        // Cancel path MUST produce a renderable line. If the model returns nothing, fall
        // back to the literal so the chat doesn't get an empty-content emote.
        var service = MakeService("   \n  ");
        var result = await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: "I think",
            interruptingMessage: "stop",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.Equal(PersonaEmoteService.GenerationFailureFallback, result);
    }

    [Fact]
    public async Task GenerateAbandonmentEmoteAsync_RoutesViaCharacterThoughtsComplexity()
    {
        JobComplexity? requested = null;
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) =>
                SingleChunk("*holds back*", ct));
        endpoint
            .Setup(e => e.GetAttributionAsync())
            .ReturnsAsync(new ChatMessageMetadata { Provider = "test", ModelName = "lfm2-test" });

        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .Callback<JobComplexity, CancellationToken>((c, _) => requested = c)
            .ReturnsAsync(endpoint.Object);

        var service = new PersonaEmoteService(router.Object, NullLogger.Instance);
        await service.GenerateAbandonmentEmoteAsync(
            MakeSelf(),
            partialDraft: "I think",
            interruptingMessage: "stop",
            interruptingSenderName: "Mira",
            CancellationToken.None);

        Assert.Equal(JobComplexity.CharacterThoughts, requested);
    }
}
