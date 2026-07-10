using System.Runtime.CompilerServices;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Streaming;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="SpeakingSession"/> — the single-use streaming pump that drives one
/// LLM response for a specific persona.
///
/// Coverage areas:
///   • Content and reasoning chunks are accumulated into separate StringBuilders and returned
///     in <see cref="SpeakingResult"/> (the two token types must not bleed into each other)
///   • <c>onEvent</c> is called once per chunk (including a terminal <c>isDone=true</c> event
///     after the stream ends)
///   • Cancellation propagates: a cancelled token causes <see cref="OperationCanceledException"/>
///     and no further chunks are emitted after the cancel point
///   • Router exceptions bubble up to the caller (PersonaGrain owns retry)
///
/// Testing strategy:
///   SpeakingSession is a plain class with no Orleans dependency, so no TestKit silo is needed.
///   ILlmRouterGrain and ILlmEndpointGrain are mocked with Moq. Scripted IAsyncEnumerable
///   implementations supply deterministic chunk sequences.
/// </summary>
public class SpeakingSessionTest
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SelfView MakeParticipant(string name) =>
        new(Guid.NewGuid(), name, DriverKind.LLM, $"Test bio for {name}", $"You are {name}.", 0.5, 0.5);

    private static ParticipantView AsView(SelfView s) => new(s.Id, s.Name, s.Driver);

    /// <summary>
    /// Builds a mocked router that routes to the given endpoint grain mock.
    /// </summary>
    private static Mock<ILlmRouterGrain> RouterFor(Mock<ILlmEndpointGrain> endpoint)
    {
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
        return router;
    }

    /// <summary>
    /// Builds an endpoint mock whose GenerateAsync yields the provided scripted chunks.
    /// </summary>
    private static Mock<ILlmEndpointGrain> EndpointWith(
        IEnumerable<LlmGenerationEvent> chunks)
    {
        var chunkList = chunks.ToList();
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => Emit(chunkList, ct));
        return endpoint;
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> Emit(
        IEnumerable<LlmGenerationEvent> chunks,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk;
        }
    }

    // ── Content / reasoning accumulation ─────────────────────────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_AccumulatesContentAndReasoningSeparately()
    {
        // ContentChunk tokens go to SpeakingResult.Message; ReasoningChunk tokens go to
        // SpeakingResult.Reasoning. The two streams must not bleed into each other.
        var chunks = new[]
        {
            new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "Hello"),
            new LlmGenerationEvent(LlmGenerationEvent.ReasoningChunk, "because reasons"),
            new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, " world"),
        };

        var endpoint = EndpointWith(chunks);
        var persona = MakeParticipant("Alice");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        var result = await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal("Hello world", result.Message);
        Assert.Equal("because reasons", result.Reasoning);
    }

    // ── onEvent callbacks ─────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_InvokesOnEventForEachChunkThenDone()
    {
        // onEvent must be called once per chunk with isDone=false, then a final call with
        // isDone=true carrying the GenerationComplete event type after the stream exhausts.
        var chunks = new[]
        {
            new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "A"),
            new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "B"),
            new LlmGenerationEvent(LlmGenerationEvent.ReasoningChunk, "R"),
        };

        var endpoint = EndpointWith(chunks);
        var persona = MakeParticipant("Bob");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        var events = new List<(string type, string data, bool done)>();
        await session.GenerateResponseOnlyAsync(
            persona, [],
            (type, data, done) => { events.Add((type, data, done)); return Task.CompletedTask; },
            CancellationToken.None);

        // 3 chunk events + 1 terminal event
        Assert.Equal(4, events.Count);
        Assert.All(events.Take(3), e => Assert.False(e.done));
        var terminal = events[^1];
        Assert.True(terminal.done);
        Assert.Equal(MessageStreamEvent.GenerationComplete, terminal.type);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_EmptyStream_StillFiresTerminalEvent()
    {
        // Even with no chunks, the terminal GenerationComplete event must be emitted so that
        // downstream clients can close their generation subscription.
        var endpoint = EndpointWith([]);
        var persona = MakeParticipant("Charlie");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        var events = new List<(string type, string data, bool done)>();
        var result = await session.GenerateResponseOnlyAsync(
            persona, [],
            (type, data, done) => { events.Add((type, data, done)); return Task.CompletedTask; },
            CancellationToken.None);

        var terminal = Assert.Single(events);
        Assert.True(terminal.done);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Reasoning);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        // A pre-cancelled token must surface as OperationCanceledException. The scripted
        // async enumerable checks ThrowIfCancellationRequested() before the first yield, so
        // no chunks are emitted and onEvent is never called.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var endpoint = EndpointWith([
            new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "unreachable"),
        ]);
        var persona = MakeParticipant("Dana");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        var events = new List<(string, string, bool)>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.GenerateResponseOnlyAsync(
                persona, [],
                (t, d, done) => { events.Add((t, d, done)); return Task.CompletedTask; },
                cts.Token));

        // No events should have been emitted before the cancellation
        Assert.Empty(events);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_CancelledMidStream_StopsAfterCancelPoint()
    {
        // When cancelled after the first chunk, subsequent chunks must not reach onEvent.
        // This verifies that no partial results leak out after cancellation.
        using var cts = new CancellationTokenSource();

        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => TwoChunksThenCancel(cts, ct));

        var persona = MakeParticipant("Eve");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        var events = new List<(string, string, bool)>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            session.GenerateResponseOnlyAsync(
                persona, [],
                (t, d, done) => { events.Add((t, d, done)); return Task.CompletedTask; },
                cts.Token));

        // Only the first chunk should have been received before cancellation
        Assert.Single(events);
        Assert.Equal("first", events[0].Item2);
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> TwoChunksThenCancel(
        CancellationTokenSource cts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "first");
        await Task.Yield();
        cts.Cancel(); // Cancel after yielding the first chunk
        ct.ThrowIfCancellationRequested();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "second"); // unreachable
    }

    // ── Scenario rendering ────────────────────────────────────────────────────

    /// <summary>
    /// Captures the LlmGenerationJob passed to the endpoint so tests can assert on the
    /// rendered system prompt without coupling to the streaming pump.
    /// </summary>
    private static (Mock<ILlmEndpointGrain> endpoint, Func<LlmGenerationJob?> getJob) CapturingEndpoint()
    {
        LlmGenerationJob? captured = null;
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
            {
                captured = job;
                return Emit([], ct);
            });
        return (endpoint, () => captured);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_WithScenario_RendersScenarioSectionInSystemPrompt()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [],
            (_, _, _) => Task.CompletedTask,
            CancellationToken.None,
            turnInstruction: null,
            scenario: "Office of a stealth horticulture startup.");

        var systemMessage = getJob()!.Messages[0];
        Assert.Equal("system", systemMessage.Role);
        Assert.Contains("# Scenario", systemMessage.Content);
        Assert.Contains("Office of a stealth horticulture startup.", systemMessage.Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_NullScenario_OmitsScenarioSection()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [],
            (_, _, _) => Task.CompletedTask,
            CancellationToken.None);

        var systemMessage = getJob()!.Messages[0];
        Assert.DoesNotContain("# Scenario", systemMessage.Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_WhitespaceScenario_OmitsScenarioSection()
    {
        // Whitespace-only scenario must be treated as "no scenario" so we don't emit an
        // empty heading that confuses the model.
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [],
            (_, _, _) => Task.CompletedTask,
            CancellationToken.None,
            turnInstruction: null,
            scenario: "   \n  ");

        var systemMessage = getJob()!.Messages[0];
        Assert.DoesNotContain("# Scenario", systemMessage.Content);
    }

    // ── Memory cue from decision phase ───────────────────────────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_WithMemoryToReference_RendersMemoryBlockAtRecencyPosition()
    {
        // The decision phase picked a memory; the speaking phase must render it as a
        // dedicated block AFTER the # Style section so it lands in the recency slot of the
        // system prompt — the last thing the model sees before the conversation history.
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Eiko");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [],
            (_, _, _) => Task.CompletedTask,
            CancellationToken.None,
            turnInstruction: null,
            scenario: null,
            memoryToReference: "you watched Denise pivot toward CTO ambition");

        var systemContent = getJob()!.Messages[0].Content;
        Assert.Contains("# A memory surfacing for you", systemContent);
        Assert.Contains("you watched Denise pivot toward CTO ambition", systemContent);

        // Recency check: memory block must come after the Style block, not before.
        var styleIdx = systemContent.IndexOf("# Style", StringComparison.Ordinal);
        var memoryIdx = systemContent.IndexOf("# A memory surfacing for you", StringComparison.Ordinal);
        Assert.True(styleIdx >= 0, "Expected # Style block in system prompt");
        Assert.True(memoryIdx > styleIdx,
            $"Expected memory block after # Style (recency position). style={styleIdx}, memory={memoryIdx}");
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_NullMemoryToReference_OmitsMemoryBlock()
    {
        // No memory selected → no block. Avoids priming the model with an empty "memory"
        // heading that would otherwise invite confabulation.
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Eiko");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [],
            (_, _, _) => Task.CompletedTask,
            CancellationToken.None,
            turnInstruction: null,
            scenario: null,
            memoryToReference: null);

        var systemContent = getJob()!.Messages[0].Content;
        Assert.DoesNotContain("# A memory surfacing for you", systemContent);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_WhitespaceMemoryToReference_OmitsMemoryBlock()
    {
        // Same as null — a whitespace-only memory string must not produce an empty heading.
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Eiko");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [],
            (_, _, _) => Task.CompletedTask,
            CancellationToken.None,
            turnInstruction: null,
            scenario: null,
            memoryToReference: "  \n  ");

        var systemContent = getJob()!.Messages[0].Content;
        Assert.DoesNotContain("# A memory surfacing for you", systemContent);
    }

    // ── Identity composition (Bio + SystemPrompt unification) ────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_BioOnly_RendersBioAsIdentity()
    {
        // A persona authored with just a Bio (SystemPrompt null — the persona-library
        // default and the bench cast shape) must still speak from that identity. Before
        // unification the speaking phase read SystemPrompt only, so Bio-only personas ran
        // with a blank identity block and weak models collapsed to assistant register.
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = new SelfView(Guid.NewGuid(), "Vlad", DriverKind.LLM,
            Bio: "A centuries-old vampire, world-weary and dry.",
            SystemPrompt: null, Chattiness: 0.2, Impulsivity: 0.3);
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Contains("A centuries-old vampire, world-weary and dry.", getJob()!.Messages[0].Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_BioAndSystemPrompt_RendersBoth()
    {
        // Both fields present → Bio (the one-liner the decision phase also anchors on)
        // followed by SystemPrompt (detailed voice instructions). Same identity, richer detail.
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = new SelfView(Guid.NewGuid(), "Vlad", DriverKind.LLM,
            Bio: "A centuries-old vampire.",
            SystemPrompt: "Speak in short, dry sentences.", Chattiness: 0.2, Impulsivity: 0.3);
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        var systemContent = getJob()!.Messages[0].Content;
        Assert.Contains("A centuries-old vampire.", systemContent);
        Assert.Contains("Speak in short, dry sentences.", systemContent);
        // Bio anchors first — it's the shared identity line across both phases.
        Assert.True(
            systemContent.IndexOf("A centuries-old vampire.", StringComparison.Ordinal) <
            systemContent.IndexOf("Speak in short, dry sentences.", StringComparison.Ordinal),
            "Expected Bio before SystemPrompt in the identity block");
    }

    // ── Decision→speaking handoff (turn-guidance message) ────────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_WithGutReactionAndInstruction_RendersBothInGuidance()
    {
        // The decision phase's gut reaction travels with the draft so the speaking model
        // refines a felt moment instead of re-deciding from cold history. Both land in the
        // final system message — the strongest recency slot.
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None,
            turnInstruction: "Congrats on the bakery.",
            gutReaction: "Fifteen years, gone like that — that's actually huge.");

        var guidance = getJob()!.Messages[^1];
        Assert.Equal("system", guidance.Role);
        Assert.Contains("Fifteen years, gone like that", guidance.Content);
        Assert.Contains("Congrats on the bakery.", guidance.Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_WithPickedMemory_RestatesMemoryInGuidance()
    {
        // The picked memory is restated in the final guidance message, not only in the
        // system-prompt block — the block alone proved droppable by smaller models
        // (bench: pick made, utterance generic).
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None,
            turnInstruction: "Say hi.",
            memoryToReference: "Denise announced she is opening a bakery called Rise & Grind.");

        var guidance = getJob()!.Messages[^1];
        Assert.Equal("system", guidance.Role);
        Assert.Contains("Rise & Grind", guidance.Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_NoHandoffFields_OmitsGuidanceMessage()
    {
        // Nothing to hand off → no trailing system message (don't prime the model with an
        // empty guidance stanza).
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        var job = getJob()!;
        Assert.Single(job.Messages); // system identity prompt only
    }

    // ── Tier routing fallback ─────────────────────────────────────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_NoCharacterVoiceProvider_FallsBackToGeneral()
    {
        // A setup with only General-capable providers must not mute speaking: the session
        // retries the route at General (mirrors MemoryExtractor.RouteExtractionAsync).
        var (endpoint, getJob) = CapturingEndpoint();
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(JobComplexity.CharacterVoice, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No model-providers available for job complexity CharacterVoice"));
        router
            .Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);

        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(router.Object, new ParticipantView[] { AsView(persona) });

        var result = await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(JobComplexity.General, getJob()!.JobComplexity);
        router.Verify(r => r.RouteAsync(JobComplexity.CharacterVoice, It.IsAny<CancellationToken>()), Times.Once);
        router.Verify(r => r.RouteAsync(JobComplexity.General, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Error propagation ─────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateResponseOnlyAsync_RouterThrows_ExceptionBubblesUp()
    {
        // SpeakingSession does not retry — exceptions from the router propagate directly.
        // PersonaGrain is responsible for retry/backoff logic. (InvalidOperationException
        // on the CharacterVoice route triggers the General fallback; when that fails too,
        // the exception surfaces.)
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no endpoints available"));

        var persona = MakeParticipant("Frank");
        var session = new SpeakingSession(router.Object, new ParticipantView[] { AsView(persona) });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.GenerateResponseOnlyAsync(
                persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None));
    }
}
