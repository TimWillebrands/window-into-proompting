using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>
/// Conversations move moods (ADR 0021): when a Moment closes, System One asks per present persona whether it
/// left them better or worse off, and nudges their Self's valence. Detached and non-fatal.
/// </summary>
public sealed class MomentAppraisal(ISystemOneJudge judge, IGrainFactory grains, ILogger<MomentAppraisal> logger)
{
    private const int MaxLines = 14;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, string> appraised = new();

    private static readonly IReadOnlyDictionary<string, string?> Moods = new Dictionary<string, string?>
    {
        ["better"] = "better than before",
        ["same"] = "about the same",
        ["worse"] = "worse than before",
    };

    private static readonly IReadOnlyDictionary<string, string?> Closeness = new Dictionary<string, string?>
    {
        ["better"] = "closer than before",
        ["same"] = "about the same",
        ["worse"] = "more distant or put off",
    };

    private async Task<string> WhereAsync(Moment moment)
    {
        if (moment.Source == MomentSources.Board) return "the Board";
        var room = (await grains.GetGrain<IPartyGrain>(moment.PartyId).GetChatGroups()).FirstOrDefault(r => r.Id == moment.ThreadId);
        return room is null ? "a conversation" : room.Kind == "dm" ? $"your chat with {room.Name}" : $"#{room.Name}";
    }

    /// <param name="who">Personas who took a recollection away from it.</param>
    public async Task AppraiseAsync(Moment moment, IReadOnlySet<Guid> who)
    {
        if (!judge.Enabled) return;
        var personas = moment.Present.Where(p => p.Driver == DriverKind.LLM && who.Contains(p.Id)).ToList();
        if (personas.Count == 0) return;
        // Only what's new since this thread's last appraisal: a Board thread re-captures on every reply.
        var lines = Appraisal.Unseen(moment.Lines, appraised.GetValueOrDefault(moment.ThreadId));
        if (lines.Count < Appraisal.MinNewLines) return;
        appraised[moment.ThreadId] = Appraisal.Key(moment.Lines[^1]);
        try
        {
            var where = await WhereAsync(moment);
            var names = moment.Present.ToDictionary(p => p.Id, p => p.Name);
            var transcript = string.Join("\n", lines.TakeLast(MaxLines)
                .Select(l => $"{names.GetValueOrDefault(l.SenderId, "Someone")}: {WorldStream.Clip(l.Text, 240)}"));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            // Three-way choices, not nouls: decision models compress p, and "about the same" absorbs the neutral mass.
            var speakers = lines.Select(l => l.SenderId).ToHashSet();
            var pairs = personas
                .SelectMany(p => moment.Present.Where(q => q.Id != p.Id && speakers.Contains(q.Id) && q.Driver != DriverKind.System).Select(q => (p, q)))
                .ToList();
            var questions = personas.ToDictionary(
                p => p.Id.ToString("N"),
                p => (SystemOneQuestion)new ChoiceQuestion($"How did this conversation leave {p.Name}'s mood, compared to before it?", Moods));
            foreach (var (p, q) in pairs)
                questions[$"{p.Id:N}-{q.Id:N}"] = new ChoiceQuestion($"By the end of this, how did {p.Name} feel toward {q.Name}?", Closeness);
            var answers = await judge.AskAsync(transcript, questions, cts.Token);
            var shifts = personas.Select(p => (p, Better: Appraisal.Better(answers.Choices.GetValueOrDefault(p.Id.ToString("N"))))).ToList();
            foreach (var (p, better) in shifts)
                await grains.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(moment.PartyId, p.Id)).ShiftValenceAsync(better, where);
            foreach (var (p, q) in pairs)
                await grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(moment.PartyId, p.Id))
                    .ShiftWarmthAsync(q.Id, Appraisal.Better(answers.Choices.GetValueOrDefault($"{p.Id:N}-{q.Id:N}")));
            logger.LogInformation("Appraisal in {Where}: {Values}", where, string.Join(", ", shifts.Select(x => $"{x.p.Name}={x.Better:F2}")));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Moment appraisal failed in {ThreadId}", moment.ThreadId);
        }
    }
}

/// <summary>Pure: a mood choice distribution → p(better) on the 0..1 scale <see cref="SelfMath.Shift"/> expects.</summary>
public static class Appraisal
{
    public const int MinNewLines = 2;

    public static string Key(MomentLine line) => $"{line.SenderId:N}|{line.At}|{line.Text.GetHashCode()}";

    /// <summary>Lines after the last appraised one; all of them when it's not in this window.</summary>
    public static IReadOnlyList<MomentLine> Unseen(IReadOnlyList<MomentLine> lines, string? lastKey)
    {
        if (lastKey is null) return lines;
        var at = lines.ToList().FindLastIndex(l => Key(l) == lastKey);
        return at < 0 ? lines : lines.Skip(at + 1).ToList();
    }

    public static double Better(IReadOnlyDictionary<string, double>? choice)
    {
        if (choice is null || choice.Count == 0) return 0.5;
        var better = choice.GetValueOrDefault("better");
        var worse = choice.GetValueOrDefault("worse");
        return Math.Clamp(0.5 + (better - worse) / 2, 0, 1);
    }
}
