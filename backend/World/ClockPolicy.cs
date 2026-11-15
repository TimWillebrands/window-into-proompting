using PartyTown.Model;

namespace PartyTown.World;

public readonly record struct LifeCandidate(Guid Id, double Chattiness, long? LastActedAt, double Availability, bool Notified = false);

public abstract record LullDecision
{
    public sealed record Seed(int Cursor) : LullDecision;
    public sealed record Wait : LullDecision;
    public sealed record Capture(int Cursor, ChatMessage Anchor, List<ChatMessage> Context) : LullDecision;
}

public static class ClockPolicy
{
    public static readonly TimeSpan Lull = TimeSpan.FromSeconds(90);
    public const int MinNewMessagesForCapture = 3;

    public static bool TickDue(long? lastTickAt, int tickSeconds, DateTimeOffset now)
        => lastTickAt is not { } last || now.ToUnixTimeMilliseconds() - last >= tickSeconds * 1000L;

    public static bool WithinBudget(List<long> actionTimes, int maxActionsPerHour, DateTimeOffset now)
    {
        var hourAgo = now.AddHours(-1).ToUnixTimeMilliseconds();
        actionTimes.RemoveAll(t => t < hourAgo);
        return actionTimes.Count < maxActionsPerHour;
    }

    public static (bool Due, long? LastAmbientAt) Ambient(long? lastAmbientAt, int ambientMinutes, DateTimeOffset now)
    {
        if (ambientMinutes <= 0) return (false, lastAmbientAt);
        var nowMs = now.ToUnixTimeMilliseconds();
        if (lastAmbientAt is not { } last) return (false, nowMs);
        return nowMs - last >= ambientMinutes * 60_000L ? (true, nowMs) : (false, last);
    }

    public const double NotifiedBoost = 2.5;

    public static double[] LifeWeights(IReadOnlyList<LifeCandidate> candidates, int tickSeconds, int castCount, DateTimeOffset now)
    {
        var expectedGap = Math.Max(1, tickSeconds * castCount);
        return candidates.Select(c =>
        {
            var since = c.LastActedAt is { } last ? (now.ToUnixTimeMilliseconds() - last) / 1000.0 : expectedGap * 3.0;
            return (0.25 + c.Chattiness) * Math.Clamp(since / expectedGap, 0.1, 3) * (0.15 + 0.85 * c.Availability)
                * (c.Notified ? NotifiedBoost : 1);
        }).ToArray();
    }

    public static bool Notified(long? notifiedAt, long? lastActedAt) => notifiedAt is { } n && (lastActedAt is not { } a || n > a);

    public static double Resting(IEnumerable<double> chattiness) => chattiness.Sum(c => 0.25 + c);

    public static Guid? Pick(IReadOnlyList<LifeCandidate> candidates, double[] weights, double unit, double resting = 0)
    {
        if (candidates.Count == 0) return null;
        var roll = unit * (weights.Sum() + resting);
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) return candidates[i].Id;
        }
        return resting > 0 ? null : candidates[^1].Id;
    }

    public static LullDecision Lulled(IReadOnlyList<ChatMessage> recent, int? cursor, DateTimeOffset now)
    {
        if (recent.Count == 0) return new LullDecision.Wait();
        var last = recent[^1];
        if (cursor is not { } c) return new LullDecision.Seed(last.MessageId);

        var fresh = recent.Where(m => m.MessageId > c).ToList();
        if (fresh.Count < MinNewMessagesForCapture) return new LullDecision.Wait();
        if (now - DateTimeOffset.FromUnixTimeMilliseconds(last.SendAt ?? 0) < Lull) return new LullDecision.Wait();

        var anchor = MostAnswered(fresh);
        var context = recent.Where(m => m.MessageId <= anchor.MessageId || m.MessageId > c).ToList();
        return new LullDecision.Capture(last.MessageId, anchor, context);
    }

    public static List<ParticipantView> Witnesses(IReadOnlyList<ChatMessage> span, IReadOnlyList<ParticipantView> present)
    {
        if (span.All(m => m.HeardBy is null)) return present.ToList();
        var heard = span.SelectMany(m => m.HeardBy ?? present.Select(p => p.Id).ToList()).ToHashSet();
        return present.Where(p => heard.Contains(p.Id)).ToList();
    }

    public static ChatMessage MostAnswered(IReadOnlyList<ChatMessage> span)
        => span
            .OrderByDescending(m => span.Count(x => x.TriggeredByMessageId == m.MessageId))
            .ThenByDescending(m => m.MessageId)
            .First();

    public static List<Guid> WalkToGathering(IEnumerable<Guid> attendees, IReadOnlyList<CastMember> cast, IReadOnlySet<Guid> asleep)
        => attendees
            .Where(id => cast.FirstOrDefault(c => c.Id == id)?.DefaultDriver == DriverKind.LLM && !asleep.Contains(id))
            .ToList();
}
