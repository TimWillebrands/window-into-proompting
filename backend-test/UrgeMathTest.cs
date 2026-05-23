using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="UrgeMath"/> — the pure pre-gate math (mention/question/silence/
/// cold-open scoring) used by both <see cref="PersonaDecisionService"/> and the
/// <c>PersonaGrain</c> pre-gate short-circuit.
///
/// Note on non-determinism: CalculateResponseUrge uses Random.Shared for the chaos score.
/// Tests avoid asserting exact total values and instead assert on the deterministic
/// components (mentionScore, questionScore, silenceStreakScore) or on bucket boundaries
/// that don't depend on chaos.
/// </summary>
public class UrgeMathTest
{
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
        // A message containing the persona's name (case-insensitive) triggers mention detection.
        // With mentionScore = 1.0, the total will be ≥ 0.9 triggering the auto-respond shortcut.
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
        // Only a trailing '?' counts — a '?' mid-sentence should not trigger it.
        var self = MakeParticipant("Bob");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "I wonder? Anyway, let's move on.") };

        var urge = UrgeMath.CalculateResponseUrge(self, history, 0);

        Assert.Equal(0, urge.QuestionScore);
    }

    [Fact]
    public void CalculateResponseUrge_SilenceStreak_CapsAtPointFour()
    {
        // silenceStreakScore = min(0.4, rounds * 0.1), so 5+ rounds should be capped.
        var self = MakeParticipant("Charlie");
        var senderId = Guid.NewGuid();
        var history = new List<ChatMessage> { UserMessage(senderId, "Hello.") };

        var urge = UrgeMath.CalculateResponseUrge(self, history, 10);

        Assert.Equal(0.4, urge.SilenceStreakScore);
    }
}
