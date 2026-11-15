using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.Streaming;

namespace BackendTest;

public class PersonaDecisionServiceTest
{
    private static SelfView MakeParticipant(string name, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, DriverKind.LLM, $"Test bio for {name}", null, 0.5, 0.5);

    private static ParticipantView AsView(SelfView s) => new(s.Id, s.Name, s.Driver);
    private static ParticipantView UserPv(Guid id, string name) => new(id, name, DriverKind.User);

    private static ChatMessage UserMessage(Guid senderId, string content) => new()
    {
        MessageId = 1,
        SenderId = senderId,
        SenderType = "user",
        Content = content,
    };

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
            .Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(endpoint.Object);
        return new PersonaDecisionService(router.Object, NullLogger.Instance);
    }

    private PersonaDecisionService MakeServiceWithRouter(Mock<ILlmRouterGrain> router)
        => new PersonaDecisionService(router.Object, NullLogger.Instance);

    [Fact]
    public async Task ShouldRespondAsync_DirectMention_AutoRespondsWithoutCallingLlm()
    {
        var router = new Mock<ILlmRouterGrain>();
        var self = MakeParticipant("Diana");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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
        Assert.True(result.Shortcut);
        router.Verify(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShouldRespondAsync_WellFormedJsonResponse_ParsesDecision()
    {
        var self = MakeParticipant("Eve");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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
        var self = MakeParticipant("Frank");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var malformed = """{"respond": true, "wouldSay": "Go ahead", "gutReaction": "natural",}""";

        var service = MakeService(EndpointReturningJson(malformed));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.False(result.Reason.StartsWith("Fallback"), "Expected repaired JSON, not the fail-closed fallback");
    }

    [Fact]
    public async Task ShouldRespondAsync_JsonWrappedInMarkdownCodeFence_ParsesCorrectly()
    {
        var self = MakeParticipant("Vlad");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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
        var self = MakeParticipant("Hana");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var broken = "{\"gutReaction\": \"Entropy here.\", \"wouldSay\": \"\"Denise, your noise wasn't noise. How do we *invite* it, not dissect?\"\", \"respond\": true}";

        var service = MakeService(EndpointReturningJson(broken));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.False(
            result.Reason.StartsWith("Fallback"),
            $"Expected stray-quote-wrapper repair to parse, not the fail-closed fallback. Got reason: {result.Reason}");
        Assert.Contains("Denise, your noise", result.Instruction);
        Assert.False(result.Instruction.StartsWith("\""),
            $"Expected leading wrapper quote stripped; instruction starts with quote: {result.Instruction}");
    }

    [Fact]
    public async Task ShouldRespondAsync_JsonWithRawNewlinesInsideStringValue_ParsesCorrectly()
    {
        var self = MakeParticipant("Vlad");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var broken = "{\"gutReaction\": \"First line.\nSecond line.\", \"respond\": true, \"wouldSay\": \"Reply.\"}";

        var service = MakeService(EndpointReturningJson(broken));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.False(result.Reason.StartsWith("Fallback"), "Expected escape-normalized JSON to parse, not the fail-closed fallback");
    }

    [Fact]
    public async Task ShouldRespondAsync_UnparseableJsonResponse_FailsClosedWithRespondFalse()
    {
        var self = MakeParticipant("Grace");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var garbage = "<<<SYSTEM ERROR: upstream provider timeout>>>";

        var service = MakeService(EndpointReturningJson(garbage));
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.False(result.Respond);
        Assert.StartsWith("Fallback", result.Reason);
    }

    [Fact]
    public async Task ShouldRespondAsync_InvokesOnEventCallbackForEachChunk()
    {
        var self = MakeParticipant("Henry");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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

        Assert.NotEmpty(streamingEvents);
        Assert.All(streamingEvents, e => Assert.Equal(MessageStreamEvent.PersonaEvaluationStreaming, e));

        Assert.True(completionFired, "Expected a terminal PersonaEvaluationComplete event");
    }

    [Fact]
    public async Task ShouldRespondAsync_LlmPicksMemory_ReturnsMemoryToReference()
    {
        var self = MakeParticipant("Kira");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = """
            {
              "gutReaction": "Reminds me of yesterday.",
              "memoryToReference": 2,
              "wouldSay": "Funny — yesterday you sounded ready to walk away from corporate.",
              "respond": true
            }
            """;

        var service = MakeService(EndpointReturningJson(json));
        var result = await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None,
            recollections: ["you heard Denise praise stability", "you watched Denise pivot toward CTO ambition"]);

        Assert.True(result.Respond);
        Assert.Equal(2, result.MemoryToReference);
    }

    [Fact]
    public async Task ShouldRespondAsync_LlmPicksIntegerShapedString_ParsesLeniently()
    {
        var self = MakeParticipant("Kira");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = """
            {"gutReaction":"hm","memoryToReference":"2","wouldSay":"Hey.","respond":true}
            """;

        var service = MakeService(EndpointReturningJson(json));
        var result = await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None,
            recollections: ["first memory", "second memory"]);

        Assert.True(result.Respond);
        Assert.Equal(2, result.MemoryToReference);
    }

    [Fact]
    public async Task ShouldRespondAsync_LlmPastesMemoryText_ReadsAsNullPick()
    {
        var self = MakeParticipant("Kira");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = """
            {"gutReaction":"hm","memoryToReference":"you watched Denise pivot toward CTO ambition","wouldSay":"Hey.","respond":true}
            """;

        var service = MakeService(EndpointReturningJson(json));
        var result = await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None,
            recollections: ["you watched Denise pivot toward CTO ambition"]);

        Assert.True(result.Respond);
        Assert.Null(result.MemoryToReference);
    }

    [Fact]
    public async Task ShouldRespondAsync_OutOfRangePick_ClampsToNull()
    {
        var self = MakeParticipant("Kira");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = """
            {"gutReaction":"hm","memoryToReference":5,"wouldSay":"Hey.","respond":true}
            """;

        var service = MakeService(EndpointReturningJson(json));
        var result = await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None,
            recollections: ["only memory"]);

        Assert.True(result.Respond);
        Assert.Null(result.MemoryToReference);
    }

    [Fact]
    public async Task ShouldRespondAsync_RecollectionsRenderAsNumberedList()
    {
        var self = MakeParticipant("Kira");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = """
            {"gutReaction":"hm","memoryToReference":null,"wouldSay":"Hey.","respond":true}
            """;

        var (endpoint, capturedJobs) = EndpointCapturingJobs(json);
        var service = MakeService(endpoint);
        await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None,
            recollections: ["first memory", "second memory"]);

        var captured = Assert.Single(capturedJobs);
        var system = captured.Messages.First(m => m.Role == "system").Content;
        Assert.Contains("1. first memory", system);
        Assert.Contains("2. second memory", system);
    }

    [Fact]
    public async Task ShouldRespondAsync_LlmPicksNullMemory_ReturnsNullMemoryToReference()
    {
        var self = MakeParticipant("Lev");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = """
            {"gutReaction":"hi","memoryToReference":null,"wouldSay":"Hey.","respond":true}
            """;

        var service = MakeService(EndpointReturningJson(json));
        var result = await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.Null(result.MemoryToReference);
    }

    [Fact]
    public async Task ShouldRespondAsync_LegacyJsonWithoutMemoryField_DefaultsToNull()
    {
        var self = MakeParticipant("Mira");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hello." }
        };

        var json = """{"gutReaction":"hi","wouldSay":"Hey.","respond":true}""";

        var service = MakeService(EndpointReturningJson(json));
        var result = await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.Null(result.MemoryToReference);
    }

    [Fact]
    public async Task ShouldRespondAsync_AutoRespondShortcut_LeavesMemoryToReferenceNull()
    {
        var router = new Mock<ILlmRouterGrain>();
        var self = MakeParticipant("Noor");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
        };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "Hey noor, quick one?" }
        };

        var service = MakeServiceWithRouter(router);
        var result = await service.ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None);

        Assert.True(result.Respond);
        Assert.Null(result.MemoryToReference);
    }

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
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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
        var self = MakeParticipant("Jules");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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
        var self = MakeParticipant("Kai");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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

        Assert.Single(jobs);

        var system = jobs.Single().Messages.Single(m => m.Role == "system").Content;
        Assert.Contains("Just before you spoke", system);
        Assert.Contains("Mira", system);

        var user = jobs.Single().Messages.Single(m => m.Role == "user").Content;
        Assert.Contains("(They said your name.)", user);
    }

    [Fact]
    public async Task ShouldRespondAsync_WindingDown_BypassesAutoRespondAndCallsLlm()
    {
        var self = MakeParticipant("Kai");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView> { AsView(self), UserPv(senderId, "User") };
        var history = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = senderId, SenderType = "user", Content = "kai, still here?" }
        };
        var (endpoint, jobs) = EndpointCapturingJobs("""{"gutReaction":"same as before","wouldSay":"","respond":false}""");
        var service = MakeService(endpoint);

        var result = await service.ShouldRespondAsync(
            self, history, participants, 0, null, CancellationToken.None, windingDown: true);

        Assert.Single(jobs);
        Assert.False(result.Respond);
    }

    [Fact]
    public async Task ShouldRespondAsync_NoMention_UserPromptOmitsMentionCue()
    {
        var self = MakeParticipant("Lara");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView>
        {
            AsView(self),
            UserPv(senderId, "User"),
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

    [Fact]
    public async Task ShouldRespondAsync_InPerson_ReadsTheRoom_AndSaysItOutLoud()
    {
        var self = MakeParticipant("Justin");
        var senderId = Guid.NewGuid();
        var participants = new List<ParticipantView> { AsView(self), UserPv(senderId, "Medevortius") };
        var history = new List<ChatMessage> { UserMessage(senderId, "I understand the secrets of this place.") };
        var (endpoint, capturedJobs) = EndpointCapturingJobs("""{"gutReaction":"who is this?","memoryToReference":null,"wouldSay":"Who let you in?","respond":true}""");

        await MakeService(endpoint).ShouldRespondAsync(self, history, participants, 0, null, CancellationToken.None, inPerson: true);

        var system = Assert.Single(capturedJobs).Messages.First(m => m.Role == "system").Content;
        Assert.Contains("You're here in person", system);
        Assert.Contains("whether they belong", system);
        Assert.Contains("the words you'd actually say out loud", system);
        Assert.DoesNotContain("group chat", system);
        Assert.DoesNotContain("(human)", system);
    }
}
