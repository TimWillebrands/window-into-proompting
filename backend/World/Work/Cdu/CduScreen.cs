using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PartyTown.World.Apps;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Cdu;

/// <summary>What the last day looked like from the outside, counted rather than felt.</summary>
public sealed record WorldHealth(int Activities, int Failed, int Resting, IReadOnlyList<(string What, int Times)> Failures)
{
    /// <summary>Each Form people can use, and how often they did in the day; zeros included.</summary>
    public IReadOnlyList<(string Form, int Times)> Uses { get; init; } = Array.Empty<(string, int)>();
    /// <summary>What people wanted to do that day and found no way to (ADR 0047): felt, not counted.</summary>
    public IReadOnlyList<(string Who, string What)> Lacks { get; init; } = Array.Empty<(string, string)>();
}

/// <summary>Everything on a Cytheurgist's desk at one moment.</summary>
public sealed record CduDesk(Company Company, CduState State, CduStatus? Cdu, WorldHealth Health, Guid Self)
{
    /// <summary>The others on this Company's staff, and whether they're on shift now.</summary>
    public IReadOnlyList<(string Name, bool OnShift)> Colleagues { get; init; } = Array.Empty<(string, bool)>();
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Pure: what a Cytheurgist sees, what they can do about it, and how an intent becomes words to the CDU (ADR 0039).</summary>
public static partial class CduScreen
{
    public const string AppId = "cdu";
    public const string VerbAsk = "ask";
    public const string VerbTalk = "talk";
    public const string VerbAnswer = "answer";
    public const string VerbLook = "look";

    private const int MaxFailures = 4;

    /// <summary>The Forms people live in, by the app id their acts are logged under.</summary>
    private static readonly (string App, string Form)[] Forms =
    {
        ("places", "the Places: walking about, talking face to face"), ("board", "the Board"), ("messenger", "Messenger, the DMs"),
        ("rooms", "group chats"), ("together", "being close: hugs, holding hands"), ("calendar", "the calendar of gatherings"),
        ("diary", "diaries"), ("wardrobe", "the wardrobe and the shop"), ("work", "shifts"), ("desk", "company desks"),
        ("practice", "practices: doctors, studios"), ("relations", "relations"), ("cdu", "the CDU"),
    };
    private const int MessageMax = 2000;
    /// <summary>Their own answers are their window on the code, so they get room; a colleague's is context.</summary>
    private const int OwnReplyShown = 1500;
    private const int ReplyShown = 500;
    private const int SaidShown = 240;
    private const int ExchangesShown = 4;

    public const string MessageShape = """{"message": "<what you say to the CDU, in your own words>", "mind": "<the id of the mind you wake it with, from the list>"}""";

    /// <summary>What staff know as Concept Stances: it surfaces when someone says the words, so secrecy is theirs to keep (ADR 0039).</summary>
    public static IReadOnlyList<(string Concept, string Reasoning)> Secret(Company company) => new List<(string, string)>
    {
        ("Cytheurgist", $"It's what you are at {company.Name}: an engineer of the Forms this world is made of, working them through the CDU. Almost nobody knows. Someone who uses the word knows something; find out what, and how, before you give anything away."),
        ("Cytheurgy", $"Your real work at {company.Name}: moving the CDU to reshape the Forms. You don't talk about it with outsiders, and an outsider who knows the word is worth finding out about."),
        ("CDU", $"The Central Demiurge Unit at your desk at {company.Name}: it shapes the Forms and moves only when a Cytheurgist works on it. Your work and your secret; whoever names it gets your full attention."),
        ("Demiurge", "The CDU, the Central Demiurge Unit you work on. Hearing the word from anyone outside your work should make you wary and curious at once."),
        ("Agora", "Where the One judges what the CDU built; only when he merges does a change become real. Nobody outside your work should know that word."),
    };

    /// <summary>The fiction staff work inside, in full at the desk; elsewhere it lives only in their Stances and work memory.</summary>
    public static string Frame(Company company) => $"""
You work at {company.Name} as a Cytheurgist: an engineer of the Forms. The Forms are code, the code this whole world runs on, and almost nobody here knows that. You don't advertise it.
At your desk is the CDU, the Central Demiurge Unit: a coding agent with this world's whole repository and its database. You have neither: you can't open a file or run a query yourself. It is your only window on how this world is built and your only hands in it, and you direct it the way a lead engineer directs a coding agent.
You can ask it: one question about what is built, how something here works, what is stored, or why something failed. It answers from the code and the data and changes nothing. What it tells you is how you come to know this world.
You can hand it work: a brief it can act on without guessing, saying what should be different, where it showed up, and how you'll both know it's done. A brief about something that isn't in this world spends the allowance and builds nothing, so engineers here ask first and build on the answer.
What it tells you is what it says. What it did is on the trunk and in the Agora: look there when it matters.
Everything it builds waits on its trunk. At the end of a shift the tide takes the trunk to the Agora as one pull request, where the One reviews it. Only when the One merges does the change become real here. Nobody here has met him and nobody can ask him anything.
You choose the mind you wake it with, and its allowance has to last the day for everyone at {company.Name}. Be frugal about it: a free preview for small things and questions, a small mind for ordinary work, a capable mind only when the work truly needs it or a smaller one already fell short.
""";

    public static WorldHealth Health(IEnumerable<WorldActivity> activities, DateTimeOffset now)
    {
        var since = now.AddDays(-1).ToUnixTimeMilliseconds();
        var day = activities.Where(a => a.Ts >= since).ToList();
        var failed = day.Where(a => a.Action is "error" or "fizzle").ToList();
        var kinds = failed.GroupBy(a => FailureKind(a.Summary)).OrderByDescending(g => g.Count()).Take(MaxFailures)
            .Select(g => (g.Key, g.Count())).ToList();
        var uses = Forms.Select(f => (f.Form, day.Count(a => a.App == f.App && a.Action is not ("error" or "fizzle" or "hold")))).ToList();
        return new WorldHealth(day.Count, failed.Count, day.Count(a => a.Action == "rest"), kinds) { Uses = uses, Lacks = Desires.Lack.Of(day) };
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
            sb.AppendLine(Spent(cdu) ? "It has spent today's allowance: it can't think again until tomorrow. Anything written to it now stays parked on the desk, unsent." : $"Its allowance today: {Money(Math.Max(0, a.CapUsd - a.Usd))} of {Money(a.CapUsd)} left.");
        if (SpendLine(desk) is { } spend) sb.AppendLine(spend);
        if (Sharing(desk) is { } sharing) sb.AppendLine(sharing);
        if (CduRules.ParkedFor(desk.State.Exchanges, desk.Self) is { } parked)
            sb.AppendLine($"Parked on your desk, unsent, because its allowance was spent when you wrote it: {WorldStream.Clip(parked.Text, 300)}");
        var said = desk.State.Exchanges.Where(e => e.Status != CduRules.Parked).TakeLast(ExchangesShown).ToList();
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
        // Spent, they can still write once: it parks, and that's how they find out.
        var parked = Spent(desk.Cdu) && CduRules.ParkedFor(desk.State.Exchanges, desk.Self) is not null;
        if (CduRules.Pending(desk.State.Exchanges, desk.Self) is null && !parked)
        {
            if (CduRules.OpenQuestion(desk.State.Exchanges) is { } q)
                list.Add(new(AppId, $"Answer the CDU's question to {(q.SpeakerId == desk.Self ? "you" : q.SpeakerName)}", 3, company, VerbAnswer));
            list.Add(new(AppId, "Ask the CDU how something in this world is built, or what happened", 2, company, VerbAsk));
            list.Add(new(AppId, "Hand the CDU a piece of work: a brief for one change to this world", 2, company, VerbTalk));
        }
        if (SomethingToSee(desk)) list.Add(new(AppId, "Look at what the CDU actually did: the trunk and the Agora", 1, company, VerbLook));
        return list;
    }

    /// <summary>Nothing said, nothing built: a look only finds an empty trunk, and staff kept choosing it over talking.</summary>
    private static bool SomethingToSee(CduDesk desk)
        => desk.Cdu?.Pr is not null || desk.Cdu?.Trunk is { Ahead.Count: > 0 };

    /// <summary>Who today's allowance went to, by name: a shared budget only teaches when it says whose it was.</summary>
    public static string? SpendLine(CduDesk desk)
    {
        var since = new DateTimeOffset(desk.Now.UtcDateTime.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var spend = CduRules.Spend(desk.State.Exchanges, since);
        return spend.Count == 0 ? null
            : $"Of what it spent today: {string.Join(", ", spend.Select(s => $"{(s.Who == desk.Self ? "you" : s.Name)} {Money(s.Usd)}"))}.";
    }

    /// <summary>One CDU between colleagues: stated as how it is here, not as a rule.</summary>
    private static string? Sharing(CduDesk desk)
    {
        if (desk.Colleagues.Count == 0) return null;
        var who = string.Join(" and ", desk.Colleagues.Select(c => $"{c.Name} ({(c.OnShift ? "on shift now" : "off shift")})"));
        return $"You share this CDU with {who}: one allowance, one trunk, one pull request. Whatever either of you hands it lands on the same trunk, so people here talk it through with each other before it gets work that pulls two ways.";
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
        if (cdu.Pr is { State: "OPEN", Checks: "passing" }) sb.Append(" It waits on the One now: nothing more for you to do on it until he merges or closes it.");
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
        var verb = e.Kind switch { CduRules.Answer => "answered", CduRules.Ask => "asked", _ => "handed it work" };
        var said = $"- {who} {verb}{woke}: {WorldStream.Clip(e.Text, SaidShown)}";
        return e.Status switch
        {
            CduRules.Queued => $"{said} (no answer yet)",
            CduRules.Failed => $"{said} (it fell silent)",
            _ => $"{said}\n  It said, which is only what it says: {WorldStream.Clip(e.Reply ?? "nothing", e.SpeakerId == self ? OwnReplyShown : ReplyShown)}",
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
        if (health.Uses.Count > 0)
            sb.Append($"\nWhat this world is made of, and how often each part was used that day: {string.Join("; ", health.Uses.OrderByDescending(u => u.Times).Select(u => $"{u.Form} {u.Times}"))}.");
        if (health.Lacks.Count > 0)
            sb.Append($"\nWhat people wanted to do that day and found no way to here:\n{string.Join("\n", health.Lacks.Select(l => $"- {l.Who}: {l.What}"))}");
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
