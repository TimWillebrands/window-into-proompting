using System.Runtime.CompilerServices;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Streaming;

namespace BackendTest;

public class SpeakingSessionTest
{
    private static SelfView MakeParticipant(string name) =>
        new(Guid.NewGuid(), name, DriverKind.LLM, $"Test bio for {name}", $"You are {name}.", 0.5, 0.5);

    private static ParticipantView AsView(SelfView s) => new(s.Id, s.Name, s.Driver);

    private static Mock<ILlmRouterGrain> RouterFor(Mock<ILlmEndpointGrain> endpoint)
    {
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
        return router;
    }

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

    [Fact]
    public async Task GenerateResponseOnlyAsync_AccumulatesContentAndReasoningSeparately()
    {
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

    [Fact]
    public async Task GenerateResponseOnlyAsync_InvokesOnEventForEachChunkThenDone()
    {
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

        Assert.Equal(4, events.Count);
        Assert.All(events.Take(3), e => Assert.False(e.done));
        var terminal = events[^1];
        Assert.True(terminal.done);
        Assert.Equal(MessageStreamEvent.GenerationComplete, terminal.type);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_EmptyStream_StillFiresTerminalEvent()
    {
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

    [Fact]
    public async Task GenerateResponseOnlyAsync_CancelledToken_ThrowsOperationCancelledException()
    {
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

        Assert.Empty(events);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_CancelledMidStream_StopsAfterCancelPoint()
    {
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

        Assert.Single(events);
        Assert.Equal("first", events[0].Item2);
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> TwoChunksThenCancel(
        CancellationTokenSource cts,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "first");
        await Task.Yield();
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "second");
    }

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

    [Fact]
    public async Task GenerateResponseOnlyAsync_WithMemoryToReference_RendersMemoryBlockAtRecencyPosition()
    {
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

        var styleIdx = systemContent.IndexOf("# Style", StringComparison.Ordinal);
        var memoryIdx = systemContent.IndexOf("# A memory surfacing for you", StringComparison.Ordinal);
        Assert.True(styleIdx >= 0, "Expected # Style block in system prompt");
        Assert.True(memoryIdx > styleIdx,
            $"Expected memory block after # Style (recency position). style={styleIdx}, memory={memoryIdx}");
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_NullMemoryToReference_OmitsMemoryBlock()
    {
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

    [Fact]
    public async Task GenerateResponseOnlyAsync_BioOnly_RendersBioAsIdentity()
    {
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
        Assert.True(
            systemContent.IndexOf("A centuries-old vampire.", StringComparison.Ordinal) <
            systemContent.IndexOf("Speak in short, dry sentences.", StringComparison.Ordinal),
            "Expected Bio before SystemPrompt in the identity block");
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_WithGutReactionAndInstruction_RendersBothInGuidance()
    {
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
    public async Task GenerateResponseOnlyAsync_MemorySubjectPresent_RendersAddressCueInBlockAndGuidance()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var vlad = MakeParticipant("Vlad");
        var denise = MakeParticipant("Denise");
        var session = new SpeakingSession(
            RouterFor(endpoint).Object, new ParticipantView[] { AsView(vlad), AsView(denise) });

        await session.GenerateResponseOnlyAsync(
            vlad, [], (_, _, _) => Task.CompletedTask, CancellationToken.None,
            turnInstruction: "Say hi.",
            memoryToReference: "Denise announced she is opening a bakery called Rise & Grind.");

        var systemContent = getJob()!.Messages[0].Content;
        Assert.Contains("Denise is here in the room with you", systemContent);

        var guidance = getJob()!.Messages[^1];
        Assert.Contains("Denise is right here in the room", guidance.Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_MemorySubjectAbsent_OmitsAddressCue()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var vlad = MakeParticipant("Vlad");
        var hana = MakeParticipant("Hana");
        var session = new SpeakingSession(
            RouterFor(endpoint).Object, new ParticipantView[] { AsView(vlad), AsView(hana) });

        await session.GenerateResponseOnlyAsync(
            vlad, [], (_, _, _) => Task.CompletedTask, CancellationToken.None,
            turnInstruction: "Say hi.",
            memoryToReference: "Denise announced she is opening a bakery called Rise & Grind.");

        var systemContent = getJob()!.Messages[0].Content;
        Assert.DoesNotContain("here in the room", systemContent);
        Assert.DoesNotContain("here in the room", getJob()!.Messages[^1].Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_MemoryNamesSelfOnly_OmitsAddressCue()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var vlad = MakeParticipant("Vlad");
        var hana = MakeParticipant("Hana");
        var session = new SpeakingSession(
            RouterFor(endpoint).Object, new ParticipantView[] { AsView(vlad), AsView(hana) });

        await session.GenerateResponseOnlyAsync(
            vlad, [], (_, _, _) => Task.CompletedTask, CancellationToken.None,
            turnInstruction: "Say hi.",
            memoryToReference: "You were invited for coffee Thursday; Vlad rarely leaves the castle.");

        Assert.DoesNotContain("here in the room", getJob()!.Messages[0].Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_NoHandoffFields_GuidanceCarriesLengthCueThenOnlyTheLine()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        var job = getJob()!;
        Assert.Equal(2, job.Messages.Count);
        Assert.StartsWith("Length:", job.Messages[1].Content);
        Assert.EndsWith(SpeakingSession.OnlyTheLine("Vlad", false), job.Messages[1].Content);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_Guidance_EndsOnOnlyTheLine_AndNeverCallsTheIntentADraft()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Yusuf Halloran");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None,
            turnInstruction: "Tell Anne the dunes bit made me laugh.", avoidWords: new[] { "ferry" });

        var guidance = getJob()!.Messages[^1].Content;
        Assert.DoesNotContain("Draft", guidance);
        Assert.Contains("Tell Anne the dunes bit made me laugh.", guidance);
        Assert.EndsWith("no summary of what others said.", guidance);
        Assert.Contains("what Yusuf sends", guidance);
    }

    [Theory]
    [InlineData("[ERROR: Can't parse response]", "")]
    [InlineData("Lena asks in Dutch whether I've eaten. Reply in English, short, under 12 words, avoid stale words and \"boat, foam, ferry, real\". I'm tired, cold toast earlier, so admit that lightly.\n\nnothing but cold toast, and that was a crime too",
        "nothing but cold toast, and that was a crime too")]
    public async Task GenerateResponseOnlyAsync_NeverReturnsAPlaceholderOrANoteAsTheLine(string raw, string expected)
    {
        var persona = MakeParticipant("Yusuf Halloran");
        var endpoint = EndpointWith(new[] { new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, raw) });
        var session = new SpeakingSession(RouterFor(endpoint).Object, new ParticipantView[] { AsView(persona) });

        var result = await session.GenerateResponseOnlyAsync(persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        Assert.Equal(expected, result.Message);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_NoCharacterVoiceProvider_FallsBackToGeneral()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(JobComplexity.CharacterVoice, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No model-providers available for job complexity CharacterVoice"));
        router
            .Setup(r => r.RouteAsync(JobComplexity.General, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);

        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(router.Object, new ParticipantView[] { AsView(persona) });

        var result = await session.GenerateResponseOnlyAsync(
            persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(JobComplexity.General, getJob()!.JobComplexity);
        router.Verify(r => r.RouteAsync(JobComplexity.CharacterVoice, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        router.Verify(r => r.RouteAsync(JobComplexity.General, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_RouterThrows_ExceptionBubblesUp()
    {
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no endpoints available"));

        var persona = MakeParticipant("Frank");
        var session = new SpeakingSession(router.Object, new ParticipantView[] { AsView(persona) });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.GenerateResponseOnlyAsync(
                persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_DropsEmptyMessages_SoNoPhantomSenderReachesTheModel()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Denise");
        var anne = MakeParticipant("Anne");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new[] { AsView(persona), AsView(anne) });
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = Guid.NewGuid(), SenderType = "persona", Content = "" },
            new() { MessageId = 2, SenderId = anne.Id, SenderType = "persona", Content = "Lentils at mine later?" },
            new() { MessageId = 3, SenderId = persona.Id, SenderType = "persona", Content = "   " },
        };

        await session.GenerateResponseOnlyAsync(persona, history, (_, _, _) => Task.CompletedTask, CancellationToken.None);

        var messages = getJob()!.Messages;
        Assert.DoesNotContain(messages, m => m.Content.Contains("sender=\"Unknown\""));
        Assert.DoesNotContain(messages, m => m.Role == "assistant" && string.IsNullOrWhiteSpace(m.Content));
        Assert.Single(messages, m => m.Role == "user");
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_InPerson_TalksOutLoud_AndPeopleAreJustPeople()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var stranger = new ParticipantView(Guid.NewGuid(), "Medevortius", DriverKind.User);
        var session = new SpeakingSession(RouterFor(endpoint).Object, new[] { AsView(persona), stranger });

        await session.GenerateResponseOnlyAsync(persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None, inPerson: true);

        var system = getJob()!.Messages[0].Content;
        Assert.Contains("You're here in person, talking out loud", system);
        Assert.DoesNotContain("chat app", system);
        Assert.Contains("- Medevortius\n", system);
        Assert.DoesNotContain("(human)", system);
    }

    [Fact]
    public async Task GenerateResponseOnlyAsync_InAChat_StillTypes()
    {
        var (endpoint, getJob) = CapturingEndpoint();
        var persona = MakeParticipant("Vlad");
        var session = new SpeakingSession(RouterFor(endpoint).Object, new[] { AsView(persona), new ParticipantView(Guid.NewGuid(), "Tim", DriverKind.User) });

        await session.GenerateResponseOnlyAsync(persona, [], (_, _, _) => Task.CompletedTask, CancellationToken.None);

        var system = getJob()!.Messages[0].Content;
        Assert.Contains("typing in a chat app", system);
        Assert.Contains("- Tim\n", system);
        Assert.DoesNotContain("(human)", system);
    }
}
