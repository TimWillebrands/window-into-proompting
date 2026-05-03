using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Generation;
using PartyTown.Services.Streaming;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="PersonaDecisionService"/> — the per-persona "should I respond?" evaluator.
///
/// Coverage areas:
///   • <see cref="PersonaDecisionService.CalculateResponseUrge"/> static scoring:
///       - Direct mention in latest message → mentionScore = 1.0 → total ≥ 0.9 (auto-respond)
///       - Question mark at end of latest message → questionScore = 0.6
///       - Silence streak contribution (capped at 0.4)
///       - Empty history returns a zero-score urge (aside from the random chaos component)
///
///   • <see cref="PersonaDecisionService.ShouldRespondAsync"/> async decision path:
///       - Auto-respond shortcut fires when mentionScore pushes total ≥ 0.9 (skips LLM call)
///       - Well-formed JSON from the LLM is parsed into ShouldRespondResult
///       - Malformed-but-repairable JSON falls back to JsonRepair and is still parsed
///       - Completely unparseable JSON fails closed: Respond=false, Reason starts with "Fallback"
///
/// Testing strategy:
///   PersonaDecisionService is a plain class — no Orleans grain, no TestKit silo.
///   The async path mocks ILlmRouterGrain (→ ILlmEndpointGrain → scripted IAsyncEnumerable).
///   NullLogger.Instance is used instead of a mock to avoid setting up IsEnabled() on every test.
///
///   Note on non-determinism: CalculateResponseUrge uses Random.Shared for the chaos score.
///   Tests avoid asserting exact total values and instead assert on the deterministic components
///   (mentionScore, questionScore, silenceStreakScore) or on boolean outcomes (Respond, auto-respond).
/// </summary>
public class PersonaDecisionServiceTest
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GenerationParticipant MakeParticipant(string name, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Name = name,
        Bio = $"Test bio for {name}",
    };

    private static ChatMessage UserMessage(Guid senderId, string content) => new()
    {
        MessageId = 1,
        SenderId = senderId,
        SenderType = "user",
        Content = content,
    };

    /// <summary>
    /// Returns an endpoint mock whose GenerateAsync yields a single chunk containing <paramref name="json"/>.
    /// Used to drive the LLM-path tests without involving a real language model.
    /// </summary>
    private static Mock<ILlmEndpointGrain> EndpointReturningJson(string json)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((_, ct) => SingleChunk(json, ct));
        return endpoint;
    }

    private static async IAsyncEnumerable<LlmGenerationEvent> SingleChunk(
        string data,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, data);
    }

    private PersonaDecisionService MakeService(Mock<ILlmEndpointGrain> endpoint)
    {
        var router = new Mock<ILlmRouterGrain>();
        router
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
        return new PersonaDecisionService(router.Object, NullLogger.Instance);
    }

    private PersonaDecisionService MakeServiceWithRouter(Mock<ILlmRouterGrain> router)
        => new PersonaDecisionService(router.Object, NullLogger.Instance);

    // ── CalculateResponseUrge (static, pure) ──────────────────────────────────

    [Fact]
    public void CalculateResponseUrge_EmptyHistory_ReturnsZeroScores()
    {
        var self = MakeParticipant("Alice");
        var urge = PersonaDecisionService.CalculateResponseUrge(self, [], 0);

        Assert.Equal(0, urge.MentionScore);
        Assert.Equal(0, urge.QuestionScore);
        Assert.Equal(0, urge.SilenceStreakScore);
        Assert.Equal(0, urge.Total);
    }

    [Fact]
    public void CalculateResponseUrge_DirectMentionInLatestMessage_SetsMentionScoreToOne()
    {
        // A message containing the persona's name (case-insensitive) triggers mention detection.
        // With mentionScore = 1.0, the total will be ≥ 0.9 triggering the auto-respond shortcut.
        var self = MakeParticipant("Alice");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "Hey alice, what do you think?") };

        var urge = PersonaDecisionService.CalculateResponseUrge(self, history, 0);

        Assert.Equal(1.0, urge.MentionScore);
        Assert.True(urge.Total >= 0.9, $"Expected total ≥ 0.9 for direct mention, got {urge.Total}");
    }

    [Fact]
    public void CalculateResponseUrge_QuestionMarkAtEndOfMessage_SetsQuestionScore()
    {
        var self = MakeParticipant("Bob");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "What do you think?") };

        var urge = PersonaDecisionService.CalculateResponseUrge(self, history, 0);

        Assert.Equal(0.6, urge.QuestionScore);
    }

    [Fact]
    public void CalculateResponseUrge_QuestionMarkNotAtEnd_DoesNotSetQuestionScore()
    {
        // Only a trailing '?' counts — a '?' mid-sentence should not trigger it.
        var self = MakeParticipant("Bob");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "I wonder? Anyway, let's move on.") };

        var urge = PersonaDecisionService.CalculateResponseUrge(self, history, 0);

        Assert.Equal(0, urge.QuestionScore);
    }

    [Fact]
    public void CalculateResponseUrge_SilenceStreak_CapsAtPointFour()
    {
        // silenceStreakScore = min(0.4, rounds * 0.1), so 5+ rounds should be capped.
        var self = MakeParticipant("Charlie");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "Hello.") };

        var urge = PersonaDecisionService.CalculateResponseUrge(self, history, 10);

        Assert.Equal(0.4, urge.SilenceStreakScore);
    }

    // ── ShouldRespondAsync — auto-respond shortcut ────────────────────────────

    [Fact]
    public async Task ShouldRespondAsync_DirectMention_AutoRespondsWithoutCallingLlm()
    {
        // When the persona is directly mentioned (urge.Total ≥ 0.9), the LLM call is skipped
        // entirely to save a round-trip. This is the "you were directly addressed" fast path.
        var router = new Mock<ILlmRouterGrain>();
        var self = MakeParticipant("Diana");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new()
            {
                MessageId = 1,
                SenderId = senderId,
                SenderType = "user",
                Content = "Hey diana, what do you think?"
            }
        };

        var service = MakeServiceWithRouter(router);
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ShouldRespondAsync — LLM decision path ────────────────────────────────

    [Fact]
    public async Task ShouldRespondAsync_WellFormedJsonResponse_ParsesDecision()
    {
        // The LLM returns valid JSON matching the ShouldRespondResult schema. It should be
        // parsed directly without falling through to JsonRepair.
        var self = MakeParticipant("Eve");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = JsonSerializer.Serialize(new ShouldRespondResult
        {
            Respond = true,
            Instruction = "Jump in and agree.",
            Reason = "The user said hello and it's natural to respond."
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var service = MakeService(EndpointReturningJson(json));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.Equal("Jump in and agree.", result.Instruction);
    }

    [Fact]
    public async Task ShouldRespondAsync_MalformedJsonResponse_FallsBackToJsonRepair()
    {
        // The LLM occasionally produces broken JSON (missing quotes, trailing commas, etc.).
        // JsonRepair should recover it rather than failing closed.
        var self = MakeParticipant("Frank");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        // JSON with a trailing comma — invalid syntax but repairable
        var malformed = """{"respond": true, "wouldSay": "Go ahead", "gutReaction": "natural",}""";

        var service = MakeService(EndpointReturningJson(malformed));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        // Repaired JSON should still parse as Respond=true
        Assert.True(result.Respond);
        Assert.False(result.Reason.StartsWith("Fallback"), "Expected repaired JSON, not the fail-closed fallback");
    }

    [Fact]
    public async Task ShouldRespondAsync_JsonWrappedInMarkdownCodeFence_ParsesCorrectly()
    {
        // Observed in production: models (despite schema constraints) frequently wrap their
        // response in ```json … ``` fences. The first parse fails on the leading backtick,
        // and JsonRepair also chokes on it — so the decision service must strip fences itself.
        var self = MakeParticipant("Vlad");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var fenced = "```json\n{\"gutReaction\": \"Natural opening.\", \"respond\": true, \"wouldSay\": \"Greet back.\"}\n```";

        var service = MakeService(EndpointReturningJson(fenced));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.Equal("Greet back.", result.Instruction);
        Assert.False(result.Reason.StartsWith("Fallback"), "Expected fence-stripped JSON to parse, not the fail-closed fallback");
    }

    [Fact]
    public async Task ShouldRespondAsync_JsonWithStrayDoubleQuoteWrappersOnWouldSay_ParsesCorrectly()
    {
        // Observed in production traces (log_id 9199): models occasionally treat wouldSay
        // as a quotation field and wrap the value in literal extra quotes — `"wouldSay": ""xxx""`
        // — yielding two consecutive `"` that JSON parses as an empty string followed by junk.
        // JsonRepair doesn't recognise this. LlmJsonParsing.CollapseStrayDoubleQuoteWrappers
        // strips the outer pair before handing off.
        var self = MakeParticipant("Hana");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        // Mirror the exact failure mode from the prod log: gutReaction normal, wouldSay
        // wrapped in `""…""`, no internal escaping. JsonRepair alone fails here.
        var broken = "{\"gutReaction\": \"Entropy here.\", \"wouldSay\": \"\"Denise, your noise wasn't noise. How do we *invite* it, not dissect?\"\", \"respond\": true}";

        var service = MakeService(EndpointReturningJson(broken));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.False(
            result.Reason.StartsWith("Fallback"),
            $"Expected stray-quote-wrapper repair to parse, not the fail-closed fallback. Got reason: {result.Reason}");
        Assert.Contains("Denise, your noise", result.Instruction);
        // Outer wrappers stripped — content shouldn't start/end with the literal extra quote.
        Assert.False(result.Instruction.StartsWith("\""),
            $"Expected leading wrapper quote stripped; instruction starts with quote: {result.Instruction}");
    }

    [Fact]
    public async Task ShouldRespondAsync_JsonWithRawNewlinesInsideStringValue_ParsesCorrectly()
    {
        // Second observed failure mode: LLMs emit literal newlines inside a multi-line `reason`
        // field. JsonRepairSharp does not escape these — the service must normalize control
        // chars inside string values before handing off to the parser.
        var self = MakeParticipant("Vlad");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        // Raw 0x0A inside the "gutReaction" string — exactly the byte that triggered the prod error.
        var broken = "{\"gutReaction\": \"First line.\nSecond line.\", \"respond\": true, \"wouldSay\": \"Reply.\"}";

        var service = MakeService(EndpointReturningJson(broken));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.False(result.Reason.StartsWith("Fallback"), "Expected escape-normalized JSON to parse, not the fail-closed fallback");
    }

    [Fact]
    public async Task ShouldRespondAsync_UnparseableJsonResponse_FailsClosedWithRespondFalse()
    {
        // If even JsonRepair can't recover the response, the service must fail closed:
        // Respond=false so that a garbled LLM output never causes an unwanted AI turn.
        var self = MakeParticipant("Grace");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        // Complete garbage — neither valid JSON nor repairable
        var garbage = "<<<SYSTEM ERROR: upstream provider timeout>>>";

        var service = MakeService(EndpointReturningJson(garbage));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.False(result.Respond);
        Assert.StartsWith("Fallback", result.Reason);
    }

    [Fact]
    public async Task ShouldRespondAsync_InvokesOnEventCallbackForEachChunk()
    {
        // The optional onEvent callback must receive a streaming event per LLM token so that
        // clients can display the persona's "thinking" state in real time.
        var self = MakeParticipant("Henry");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = JsonSerializer.Serialize(
            new ShouldRespondResult { Respond = false, Instruction = "Stay quiet.", Reason = "Nothing to add." },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var service = MakeService(EndpointReturningJson(json));

        var streamingEvents = new List<string>();
        bool? completionFired = null;

        await service.ShouldRespondAsync(
            self, history, participants, 0,
            (eventType, _, done) =>
            {
                if (done) completionFired = true;
                else streamingEvents.Add(eventType);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        // At least one streaming event should have been received during the LLM call
        Assert.NotEmpty(streamingEvents);
        Assert.All(streamingEvents, e => Assert.Equal(MessageStreamEvent.PersonaEvaluationStreaming, e));

        // A terminal completion event must arrive after streaming ends
        Assert.True(completionFired, "Expected a terminal PersonaEvaluationComplete event");
    }

    // ── ShouldRespondAsync — RepairHint injection ─────────────────────────────

    /// <summary>
    /// Mock helper that captures the <see cref="LlmGenerationJob"/> handed to GenerateAsync,
    /// so a test can assert what the model actually saw (e.g., does the system prompt contain
    /// the repair-hint stanza). Returns the canned JSON regardless of the input.
    /// </summary>
    private static (Mock<ILlmEndpointGrain> endpoint, List<LlmGenerationJob> capturedJobs) EndpointCapturingJobs(string json)
    {
        var captured = new List<LlmGenerationJob>();
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint
            .Setup(e => e.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns<LlmGenerationJob, CancellationToken>((job, ct) =>
            {
                captured.Add(job);
                return SingleChunk(json, ct);
            });
        return (endpoint, captured);
    }

    [Fact]
    public async Task ShouldRespondAsync_NoRepairHint_SystemPromptOmitsRepairStanza()
    {
        var self = MakeParticipant("Iris");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };
        var json = """{"gutReaction":"hi","wouldSay":"","respond":false}""";

        var (endpoint, jobs) = EndpointCapturingJobs(json);
        var service = MakeService(endpoint);
        await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        var system = jobs.Single().Messages.Single(m => m.Role == "system").Content;
        Assert.DoesNotContain("Just before you spoke", system);
    }

    [Fact]
    public async Task ShouldRespondAsync_WithRepairHint_SystemPromptInjectsRepairStanza()
    {
        // The race trigger sets a repair hint when a relevant message arrived during in-flight
        // generation but the persona shipped past the point of no return. The next decision
        // pass should see the hint surfaced in the system prompt so the persona can choose
        // (in-character) whether to acknowledge.
        var self = MakeParticipant("Jules");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 5, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };
        var json = """{"gutReaction":"hi","wouldSay":"","respond":false}""";

        var (endpoint, jobs) = EndpointCapturingJobs(json);
        var service = MakeService(endpoint);

        var hint = new RepairHint(4, "Mira", "wait, wrong room");
        await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None,
            repairHint: hint);

        var system = jobs.Single().Messages.Single(m => m.Role == "system").Content;
        Assert.Contains("Just before you spoke", system);
        Assert.Contains("Mira", system);
        Assert.Contains("wait, wrong room", system);
    }

    [Fact]
    public async Task ShouldRespondAsync_WithRepairHint_BypassesAutoRespondAndCallsLlm()
    {
        // Auto-respond shortcut returns canned text and bypasses the system prompt's
        // repairBlock entirely. When a repair hint is pending, that's wrong: the persona
        // would barrel through name-mentions ignoring whatever they missed during the
        // in-flight generation. Instead the slow path runs so the repair stanza injects.
        var self = MakeParticipant("Kai");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hey kai, what say you?" }
        };
        var json = """{"gutReaction":"name called","wouldSay":"yeah","respond":true}""";

        var (endpoint, jobs) = EndpointCapturingJobs(json);
        var service = MakeService(endpoint);

        var hint = new RepairHint(0, "Mira", "earlier point");
        await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None,
            repairHint: hint);

        // LLM was called (we captured a job).
        Assert.Single(jobs);

        // System prompt contains the repair stanza.
        var system = jobs.Single().Messages.Single(m => m.Role == "system").Content;
        Assert.Contains("Just before you spoke", system);
        Assert.Contains("Mira", system);

        // User prompt surfaces the mention cue (so the model still feels the pull of being named).
        var user = jobs.Single().Messages.Single(m => m.Role == "user").Content;
        Assert.Contains("(They said your name.)", user);
    }

    [Fact]
    public async Task ShouldRespondAsync_NoMention_UserPromptOmitsMentionCue()
    {
        // The mention cue is mention-score-gated. A neutral message must not get a fake
        // "they said your name" cue or every prompt becomes the same.
        var self = MakeParticipant("Lara");
        var senderId = Guid.NewGuid();
        var participants = new List<GenerationParticipant>
        {
            self,
            new() { Id = senderId, Name = "User", IsUser = true },
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "anyway, the rain again" }
        };
        var json = """{"gutReaction":"meh","wouldSay":"","respond":false}""";

        var (endpoint, jobs) = EndpointCapturingJobs(json);
        var service = MakeService(endpoint);
        await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        var user = jobs.Single().Messages.Single(m => m.Role == "user").Content;
        Assert.DoesNotContain("(They said your name.)", user);
    }
}
