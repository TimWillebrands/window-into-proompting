using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

public class UrgeMathTest
{
    private static SelfView MakeParticipant(string name, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, DriverKind.LLM, $"Test bio for {name}", null, 0.5, 0.5);

    private static ChatMessage UserMessage(Guid senderId, string content) => new()
    {
        MessageId = 1,
        SenderId = senderId,
        SenderType = "user",
        Content = content,
    };

    [Fact]
    public void CalculateResponseUrge_EmptyHistory_ReturnsZeroScores()
    {
        var self = MakeParticipant("Alice");
        var urge = UrgeMath.CalculateResponseUrge(self, [], 0);

        Assert.Equal(0, urge.MentionScore);
        Assert.Equal(0, urge.QuestionScore);
        Assert.Equal(0, urge.SilenceStreakScore);
        Assert.Equal(0, urge.Total);
    }

    [Fact]
    public void CalculateResponseUrge_DirectMentionInLatestMessage_SetsMentionScoreToOne()
    {
        var self = MakeParticipant("Alice");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "Hey alice, what do you think?") };

        var urge = UrgeMath.CalculateResponseUrge(self, history, 0);

        Assert.Equal(1.0, urge.MentionScore);
        Assert.True(urge.Total >= 0.9, $"Expected total ≥ 0.9 for direct mention, got {urge.Total}");
    }

    [Fact]
    public void CalculateResponseUrge_QuestionMarkAtEndOfMessage_SetsQuestionScore()
    {
        var self = MakeParticipant("Bob");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "What do you think?") };

        var urge = UrgeMath.CalculateResponseUrge(self, history, 0);

        Assert.Equal(0.6, urge.QuestionScore);
    }

    [Fact]
    public void CalculateResponseUrge_QuestionMarkNotAtEnd_DoesNotSetQuestionScore()
    {
        var self = MakeParticipant("Bob");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "I wonder? Anyway, let's move on.") };

        var urge = UrgeMath.CalculateResponseUrge(self, history, 0);

        Assert.Equal(0, urge.QuestionScore);
    }

    [Fact]
    public void CalculateResponseUrge_SilenceStreak_CapsAtPointFour()
    {
        var self = MakeParticipant("Charlie");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "Hello.") };

        var urge = UrgeMath.CalculateResponseUrge(self, history, 10);

        Assert.Equal(0.4, urge.SilenceStreakScore);
    }

    [Theory]
    [InlineData("Pieter Voss", "Pieter, jij bent toch jurist?", true)]
    [InlineData("Pieter Voss", "vraag het pieter voss maar", true)]
    [InlineData("Pieter Voss", "Pietertje is jarig", false)]
    [InlineData("Al Pacino", "al die mensen", false)]
    [InlineData("Al Pacino", "Al Pacino, hi", true)]
    [InlineData("Mara", "hoi Mara!", true)]
    public void Names_ByFullOrFirstName(string name, string content, bool named)
        => Assert.Equal(named, UrgeMath.Names(name, content));
}
