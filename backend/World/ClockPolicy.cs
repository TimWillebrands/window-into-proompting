using PartyTown.Model;

namespace PartyTown.World;

/// <summary>One Life candidate as the clock weighs it.</summary>
/// <param name="Notified">Something concerning them landed since they last acted (a Board reply, a mention).</param>
public readonly record struct LifeCandidate(Guid Id, double Chattiness, long? LastActedAt, double Availability, bool Notified = false);

/// <summary>What the clock does with one thread on a tick.</summary>
public abstract record LullDecision
{
    /// <summary>First sight: start the cursor here; pre-world history is not re-lived.</summary>
    public sealed record Seed(int Cursor) : LullDecision;
    public sealed record Wait : LullDecision;
    /// <summary>Lulled: remember <paramref name="Anchor"/> within <paramref name="Context"/>, move the cursor.</summary>
    public sealed record Capture(int Cursor, ChatMessage Anchor, List<ChatMessage> Context) : LullDecision;
}

/// <summary>
/// The World clock's rules (ADR 0018) as pure functions of state + now: cadence, budget, ambient
/// interval, who gets the turn, when a conversation has lulled. <see cref="WorldClockGrain"/> only executes.
/// </summary>
public static class ClockPolicy
{
    public static readonly TimeSpan Lull = TimeSpan.FromSeconds(90);
    public const int MinNewMessagesForCapture = 3;

    public static bool TickDue(long? lastTickAt, int tickSeconds, DateTimeOffset now)
        => lastTickAt is not { } last || now.ToUnixTimeMilliseconds() - last >= tickSeconds * 1000L;

    /// <summary>Drops actions older than an hour; true while the hourly budget has room.</summary>
    public static bool WithinBudget(List<long> actionTimes, int maxActionsPerHour, DateTimeOffset now)
    {
        var hourAgo = now.AddHours(-1).ToUnixTimeMilliseconds();
        actionTimes.RemoveAll(t => t < hourAgo);
        return actionTimes.Count < maxActionsPerHour;
    }

    /// <summary>Ambient Happening due? First sight starts the interval instead of firing.</summary>
    public static (bool Due, long? LastAmbientAt) Ambient(long? lastAmbientAt, int ambientMinutes, DateTimeOffset now)
    {
        if (ambientMinutes <= 0) return (false, lastAmbientAt);
        var nowMs = now.ToUnixTimeMilliseconds();
        if (lastAmbientAt is not { } last) return (false, nowMs);
        return nowMs - last >= ambientMinutes * 60_000L ? (true, nowMs) : (false, last);
    }

    /// <summary>A notification makes someone likelier to pick up their phone, not certain to.</summary>
    public const double NotifiedBoost = 2.5;

    /// <summary>Chattiness × time since last act × how free their offscreen day leaves them (ADR 0021), boosted when notified.</summary>
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

    /// <summary>Notified after they last acted (or never acted).</summary>
    public static bool Notified(long? notifiedAt, long? lastActedAt) => notifiedAt is { } n && (lastActedAt is not { } a || n > a);

    /// <summary>Weighted draw; <paramref name="unit"/> in [0, 1).</summary>
    public static Guid? Pick(IReadOnlyList<LifeCandidate> candidates, double[] weights, double unit)
    {
        if (candidates.Count == 0) return null;
        var roll = unit * weights.Sum();
        for (var i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];
            if (roll <= 0) return candidates[i].Id;
        }
        return candidates[^1].Id;
    }

    /// <summary>Has this thread gone quiet after enough new talk to be worth one memory?</summary>
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

    /// <summary>Who witnessed a span: everyone present, or for Place talk only those who heard at least one line (Earshot).</summary>
    public static List<ParticipantView> Witnesses(IReadOnlyList<ChatMessage> span, IReadOnlyList<ParticipantView> present)
    {
        if (span.All(m => m.HeardBy is null)) return present.ToList();
        var heard = span.SelectMany(m => m.HeardBy ?? present.Select(p => p.Id).ToList()).ToHashSet();
        return present.Where(p => heard.Contains(p.Id)).ToList();
    }

    /// <summary>The line others reacted to most is the moment; ties go to the latest.</summary>
    public static ChatMessage MostAnswered(IReadOnlyList<ChatMessage> span)
        => span
            .OrderByDescending(m => span.Count(x => x.TriggeredByMessageId == m.MessageId))
            .ThenByDescending(m => m.MessageId)
            .First();

    /// <summary>Who a due Place Gathering walks over: LLM-driven attendees who are awake. Humans come on their own.</summary>
    public static List<Guid> WalkToGathering(IEnumerable<Guid> attendees, IReadOnlyList<CastMember> cast, IReadOnlySet<Guid> asleep)
        => attendees
            .Where(id => cast.FirstOrDefault(c => c.Id == id)?.DefaultDriver == DriverKind.LLM && !asleep.Contains(id))
            .ToList();
}
