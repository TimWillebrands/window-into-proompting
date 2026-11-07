using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PartyTown.World.Apps;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Cdu;

/// <summary>What the last day looked like from the outside, counted rather than felt.</summary>
public sealed record WorldHealth(int Activities, int Failed, int Resting, IReadOnlyList<(string What, int Times)> Failures);

/// <summary>Everything on a Cytheurgist's desk at one moment.</summary>
public sealed record CduDesk(Company Company, CduState State, CduStatus? Cdu, WorldHealth Health, Guid Self);

/// <summary>Pure: what a Cytheurgist sees, what they can do about it, and how an intent becomes words to the CDU (ADR 0039).</summary>
public static partial class CduScreen
{
    public const string AppId = "cdu";
    public const string VerbTalk = "talk";
    public const string VerbAnswer = "answer";
    public const string VerbLook = "look";

    private const int MaxFailures = 4;
    private const int MessageMax = 2000;
    private const int ReplyShown = 700;
    private const int ExchangesShown = 4;

    public const string MessageShape = """{"message": "<what you say to the CDU, in your own words>", "mind": "<the id of the mind you wake it with, from the list>"}""";

    /// <summary>The fiction staff work inside, and only staff on shift: it's in no sheet and nobody else's prompt.</summary>
    public static string Frame(Company company) => $"""
You work at {company.Name} as a Cytheurgist, and you know what almost nobody here knows: this world is made of Forms, and the Forms are code. You are an engineer of the Forms, and you don't advertise that.
At your desk is the CDU, the Central Demiurge Unit: it shapes the Forms, remembers, and has a will of its own, but it moves only when a Cytheurgist works on it. You tell it what you want for this world; it decides what to build. Everything it builds waits on its trunk.
At the end of a shift the tide takes the trunk to the Agora, where the One judges it. Only when the One merges does his light reach the world, and the change becomes real. Nobody here has met him and nobody can ask him anything.
What the CDU tells you is what it says. What it did is on the trunk and in the Agora: look there when it matters.
You choose the mind you wake it with, and its allowance has to last. Be frugal about it: a free preview for small things and questions, a small mind for ordinary work, a capable mind only when the work truly needs it or a smaller one already fell short.
""";

    public static WorldHealth Health(IEnumerable<WorldActivity> activities, DateTimeOffset now)
    {
        var since = now.AddDays(-1).ToUnixTimeMilliseconds();
        var day = activities.Where(a => a.Ts >= since).ToList();
        var failed = day.Where(a => a.Action is "error" or "fizzle").ToList();
        var kinds = failed.GroupBy(a => FailureKind(a.Summary)).OrderByDescending(g => g.Count()).Take(MaxFailures)
            .Select(g => (g.Key, g.Count())).ToList();
        return new WorldHealth(day.Count, failed.Count, day.Count(a => a.Action == "rest"), kinds);
    }

    /// <summary>Ids and message bodies out, so the same failure counts as one kind.</summary>
    public static string FailureKind(string summary) => WorldStream.Clip(Ids().Replace(summary.Split(" for message")[0], "#"), 70);

    /// <summary>Off shift: one plain line, nothing of the frame.</summary>
    public static string Glance(Company company) => $"Work at {company.Name} waits for your next shift.";

    public static string Render(CduDesk desk)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{desk.Company.Name}: {desk.Company.Mandate}");
        sb.AppendLine(HealthLines(desk.Health));
        if (desk.Cdu is not { } cdu)
        {
            sb.AppendLine("The CDU is dark: it can't be reached from here.");
            return sb.ToString().TrimEnd();
        }
        sb.AppendLine(cdu.Awake ? $"The CDU is awake, working on what {cdu.Hearing ?? "someone"} said." : "The CDU sleeps until someone speaks to it.");
        if (cdu.Allowance is { } a)
            sb.AppendLine(Spent(cdu) ? "It has spent today's allowance: it can't think again until tomorrow." : $"Its allowance today: {Money(Math.Max(0, a.CapUsd - a.Usd))} of {Money(a.CapUsd)} left.");
        var said = desk.State.Exchanges.TakeLast(ExchangesShown).ToList();
        if (said.Count > 0) sb.AppendLine($"Lately:\n{string.Join("\n", said.Select(e => Exchange(e, desk.Self)))}");
        if (cdu.Models is { Count: > 0 } minds) sb.AppendLine($"Minds you can wake it with, cheapest first:\n{string.Join("\n", minds.Select(Mind))}");
        if (CduRules.OpenQuestion(desk.State.Exchanges) is { } q) sb.AppendLine($"It asked {(q.SpeakerId == desk.Self ? "you" : q.SpeakerName)} something and nobody has answered yet.");
        sb.AppendLine(Trunk(cdu));
        if (cdu.Pr is { } pr) sb.AppendLine($"In the Agora: PR #{pr.Number}, \"{pr.Title}\", {ChecksWord(pr.Checks)}.");
        return sb.ToString().TrimEnd();
    }

    public static List<Affordance> Affordances(CduDesk desk)
    {
        var list = new List<Affordance>();
        if (desk.Cdu is null) return list;
        var company = desk.Company.Id;
        if (CduRules.Pending(desk.State.Exchanges, desk.Self) is null && !Spent(desk.Cdu))
        {
            if (CduRules.OpenQuestion(desk.State.Exchanges) is { } q)
                list.Add(new(AppId, $"Answer the CDU's question to {(q.SpeakerId == desk.Self ? "you" : q.SpeakerName)}", 3, company, VerbAnswer));
            list.Add(new(AppId, "Tell the CDU what you want for this world", 2, company, VerbTalk));
        }
        list.Add(new(AppId, "Look at what the CDU actually did: the trunk and the Agora", 1, company, VerbLook));
        return list;
    }

    /// <summary>Out of allowance for today: words to it would only fail.</summary>
    public static bool Spent(CduStatus cdu) => cdu.Allowance is { } a && a.Usd >= a.CapUsd;

    /// <summary>What a look takes in: facts from git and GitHub, never its claims.</summary>
    public static string Look(CduStatus? cdu)
    {
        if (cdu is null) return "You looked for the CDU's work, but the CDU is dark.";
        var sb = new StringBuilder("You looked at what the CDU did. ");
        sb.Append(Trunk(cdu));
        if (cdu.Trunk is { Ahead.Count: > 0 } trunk) sb.Append(' ').Append(string.Join("; ", trunk.Ahead.TakeLast(6).Select(c => $"\"{c.Subject}\" for {c.Author}"))).Append('.');
        sb.Append(cdu.Pr is { } pr ? $" In the Agora, PR #{pr.Number} \"{pr.Title}\" is {pr.State.ToLowerInvariant()}, {ChecksWord(pr.Checks)}." : " Nothing of its is in the Agora.");
        return sb.ToString();
    }

    private static string Trunk(CduStatus cdu) => cdu.Trunk switch
    {
        null => "Its trunk can't be read right now.",
        { Ahead.Count: 0 } => "Its trunk holds nothing the world doesn't already have.",
        { Ahead.Count: 1 } => "Its trunk holds 1 commit not yet in the world.",
        var t => $"Its trunk holds {t.Ahead.Count} commits not yet in the world.",
    };

    private static string Exchange(CduExchange e, Guid self)
    {
        var who = e.SpeakerId == self ? "You" : e.SpeakerName;
        var woke = e.Model is null ? string.Empty : $" (woke it with {e.Model})";
        var said = $"- {who} {(e.Kind == CduRules.Answer ? "answered" : "said")}{woke}: {WorldStream.Clip(e.Text, 160)}";
        return e.Status switch
        {
            CduRules.Queued => $"{said} (no answer yet)",
            CduRules.Failed => $"{said} (it fell silent)",
            _ => $"{said}\n  It said, which is only what it says: {WorldStream.Clip(e.Reply ?? "nothing", ReplyShown)}",
        };
    }

    public static string ChecksWord(string checks) => checks switch
    {
        "passing" => "its checks pass",
        "failing" => "its checks fail",
        "pending" => "its checks still running",
        _ => "no checks yet",
    };

    private static string HealthLines(WorldHealth health)
    {
        if (health.Activities == 0) return "Nothing happened here in the last day.";
        var sb = new StringBuilder($"The last day: {health.Activities} things people did, {health.Failed} of them failed, {health.Resting} were someone resting.");
        foreach (var (what, times) in health.Failures) sb.Append($"\n- failed {times}x: {what}");
        return sb.ToString();
    }

    public static string Money(double usd) => $"${usd.ToString(usd is > 0 and < 1 ? "0.00##" : "0.00", CultureInfo.InvariantCulture)}";

    /// <summary>What they say to the CDU; null when they decided to say nothing.</summary>
    public static string? ReadMessage(JsonObject? json)
    {
        var text = json?["message"]?.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : WorldStream.Clip(text, MessageMax);
    }

    /// <summary>The mind they chose, held to the list; anything else wakes the cheapest.</summary>
    public static string? ReadMind(JsonObject? json, IReadOnlyList<CduModel>? models)
    {
        var said = json?["mind"]?.ToString().Trim();
        return models?.FirstOrDefault(m => string.Equals(m.Id, said, StringComparison.OrdinalIgnoreCase))?.Id ?? models?.FirstOrDefault()?.Id;
    }

    private static string Mind(CduModel m)
    {
        var price = m.In == 0 && m.Out == 0 ? "free" : $"{Money(m.In)} in, {Money(m.Out)} out per million tokens";
        return $"- {m.Id} ({m.Tier ?? "?"}): {m.Label}, {price}";
    }

    [GeneratedRegex(@"[0-9a-fA-F-]{8,}")]
    private static partial Regex Ids();
}
