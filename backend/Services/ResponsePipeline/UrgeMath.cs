using System.Text.RegularExpressions;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

public static class UrgeMath
{
    public static ResponseUrge CalculateResponseUrge(
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        int totalAiRoundsInGroup,
        bool directlyAddressed = false)
    {
        double mentionScore = 0;
        double questionScore = 0;
        double silenceStreakScore = 0;
        double coldOpenScore = 0;

        if (history.Count == 0)
            return new ResponseUrge(0, 0, 0, 0, 0, Random.Shared.NextDouble() * self.Chattiness);

        var latest = history[^1];
        var content = latest.Content ?? "";

        if (Names(self.Name, content))
            mentionScore = 1.0;

        if (directlyAddressed)
            mentionScore = 1.0;

        if (content.TrimEnd().EndsWith('?'))
            questionScore = 0.6;

        silenceStreakScore = Math.Min(0.4, totalAiRoundsInGroup * 0.1);

        if (latest.SenderType == "user" && totalAiRoundsInGroup == 0)
            coldOpenScore = 0.5;

        var chaosScore = Random.Shared.NextDouble() * self.Chattiness;

        var total = Math.Min(1.0, mentionScore + questionScore + silenceStreakScore + coldOpenScore);
        return new ResponseUrge(total, mentionScore, questionScore, silenceStreakScore, coldOpenScore, chaosScore);
    }

    public const int FirstNameFrom = 3;

    public static bool Names(string? name, string? content)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(content)) return false;
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var pattern = first.Length >= FirstNameFrom && first != name
            ? $@"\b(?:{Regex.Escape(name)}|{Regex.Escape(first)})\b"
            : $@"\b{Regex.Escape(name)}\b";
        return Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static readonly TimeSpan ExchangeWindow = World.Self.ChatAttention.SessionGap;

    public static int CountExchangeRounds(IReadOnlyList<ChatMessage> history)
    {
        var since = ExchangeSince(history);
        var count = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            var msg = history[i];
            if (msg.SenderType != "assistant") break;
            if (string.IsNullOrEmpty(msg.Content)) continue;
            if (msg.SendAt < since) break;
            count++;
        }
        return count;
    }

    public static int CountRecentSelfMessages(IReadOnlyList<ChatMessage> history, Guid selfId)
    {
        var since = ExchangeSince(history);
        var count = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].SenderType != "assistant")
                break;
            if (!string.IsNullOrEmpty(history[i].Content) && history[i].SendAt < since)
                break;
            if (history[i].SenderId == selfId)
                count++;
        }
        return count;
    }

    private static long? ExchangeSince(IReadOnlyList<ChatMessage> history)
    {
        for (var i = history.Count - 1; i >= 0; i--)
            if (!string.IsNullOrEmpty(history[i].Content) && history[i].SendAt is { } at)
                return at - (long)ExchangeWindow.TotalMilliseconds;
        return null;
    }

    public static double CalculateNetPressure(ResponseUrge urge, int totalAiRoundsInGroup, int recentSelfMessageCount)
    {
        var roundPenalty = totalAiRoundsInGroup switch
        {
            <= 1 => 0.0,
            2 => 0.15,
            3 => 0.35,
            4 => 0.6,
            _ => 0.9
        };
        var selfPenalty = recentSelfMessageCount switch
        {
            0 => 0.0,
            1 => 0.3,
            _ => 0.7
        };
        var chaosBonus = urge.ChaosScore switch
        {
            >= 0.85 => 0.3,
            >= 0.65 => 0.1,
            _ => 0.0
        };
        return urge.Total - roundPenalty - selfPenalty + chaosBonus;
    }

    public static bool IsObviousSkip(ResponseUrge urge, int totalAiRoundsInGroup, int recentSelfMessageCount)
    {
        if (urge.MentionScore > 0 || urge.QuestionScore > 0)
            return false;
        var net = CalculateNetPressure(urge, totalAiRoundsInGroup, recentSelfMessageCount);
        return net < -0.4;
    }
}

public readonly record struct ResponseUrge(
    double Total,
    double MentionScore,
    double QuestionScore,
    double SilenceStreakScore,
    double ColdOpenScore,
    double ChaosScore);
