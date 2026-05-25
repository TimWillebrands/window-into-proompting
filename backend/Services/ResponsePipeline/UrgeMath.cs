using System.Text.RegularExpressions;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Pure pre-gate math used by <see cref="PersonaDecisionService"/> and by
/// <c>PersonaGrain</c>'s pre-gate. Extracted so the grain can short-circuit
/// without taking a transitive dependency on the LLM-call service.
/// </summary>
public static class UrgeMath
{
    /// <summary>
    /// Calculates a response urge score (0.0 - 1.0) from mention detection, silence streak,
    /// question presence, cold-open (fresh user message into a quiet room), and chattiness-
    /// weighted chaos.
    /// </summary>
    public static ResponseUrge CalculateResponseUrge(
        SelfView self,
        IReadOnlyList<ChatMessage> history,
        int totalAiRoundsInGroup)
    {
        double mentionScore = 0;
        double questionScore = 0;
        double silenceStreakScore = 0;
        double coldOpenScore = 0;

        if (history.Count == 0)
            return new ResponseUrge(0, 0, 0, 0, 0, Random.Shared.NextDouble() * self.Chattiness);

        var latest = history[^1];
        var content = latest.Content ?? "";

        // Direct mention: persona name as a whole word (case-insensitive).
        // Substring matching triggered on "Tim" in "intimate", "optimization", etc.
        if (!string.IsNullOrWhiteSpace(self.Name))
        {
            var mentionRegex = new Regex(
                $@"\b{Regex.Escape(self.Name)}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (mentionRegex.IsMatch(content))
                mentionScore = 1.0;
        }

        // Question detection: does the latest message end with a question mark?
        if (content.TrimEnd().EndsWith('?'))
            questionScore = 0.6;

        // Silence streak: how many AI rounds without this persona responding?
        // Each round without a response increases urge slightly
        silenceStreakScore = Math.Min(0.4, totalAiRoundsInGroup * 0.1);

        // Cold-open: a fresh user message landing in a quiet room (no AI activity yet).
        // Without this floor, "hiya" yields urge≈0 and every persona declines with a
        // politeness-flavoured justification. A bar with a new arrival should produce
        // at least one reaction.
        if (latest.SenderType == "user" && totalAiRoundsInGroup == 0)
            coldOpenScore = 0.5;

        // Chaos weighted by per-persona chattiness. Replaces pure random with
        // character-driven variability — a chatty Denise pings more than a brooding Vlad.
        var chaosScore = Random.Shared.NextDouble() * self.Chattiness;

        var total = Math.Min(1.0, mentionScore + questionScore + silenceStreakScore + coldOpenScore);
        return new ResponseUrge(total, mentionScore, questionScore, silenceStreakScore, coldOpenScore, chaosScore);
    }

    public static int CountRecentSelfMessages(IReadOnlyList<ChatMessage> history, Guid selfId)
    {
        var count = 0;
        for (var i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].SenderType != "assistant")
                break;
            if (history[i].SenderId == selfId)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Net pressure = pull (urge total + chaos bonus) minus push (round penalty +
    /// self-dominance penalty). Mirrors the bucketed nudge in the LLM-facing prompt.
    /// Centralised so the pre-gate (PersonaGrain short-circuit) and the prompt agree.
    /// </summary>
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

    /// <summary>
    /// True when the math is decisive enough to skip the LLM decision call entirely
    /// (and skip reserving a thought-log slot). Requires no direct mention, no question,
    /// and net pressure deep in the "pass" bucket. Cuts the ~1480-thought-per-1480-msgs
    /// runaway visible in the UI when every persona deliberates on every cascade message.
    /// </summary>
    public static bool IsObviousSkip(ResponseUrge urge, int totalAiRoundsInGroup, int recentSelfMessageCount)
    {
        if (urge.MentionScore > 0 || urge.QuestionScore > 0)
            return false;
        var net = CalculateNetPressure(urge, totalAiRoundsInGroup, recentSelfMessageCount);
        return net < -0.4;
    }
}
