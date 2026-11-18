using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PartyTown.World.Apps;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Cdu;

public sealed record WorldHealth(int Activities, int Failed, int Resting, IReadOnlyList<(string What, int Times)> Failures)
{
    public IReadOnlyList<(string Form, int Times)> Uses { get; init; } = Array.Empty<(string, int)>();
    public IReadOnlyList<(string Who, string What)> Lacks { get; init; } = Array.Empty<(string, string)>();
}

public sealed record CduDesk(Company Company, CduState State, CduStatus? Cdu, WorldHealth Health, Guid Self)
{
    public IReadOnlyList<(string Name, bool OnShift)> Colleagues { get; init; } = Array.Empty<(string, bool)>();
    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
}

public static partial class CduScreen
{
    public const string AppId = "cdu";
    public const string VerbAsk = "ask";
    public const string VerbTalk = "talk";
    public const string VerbAnswer = "answer";
    public const string VerbLook = "look";

    private const int MaxFailures = 4;

    private static readonly (string App, string Form)[] Forms =
    {
        ("places", "the Places: walking about, talking face to face"), ("board", "the Board"), ("messenger", "Messenger, the DMs"),
        ("rooms", "group chats"), ("together", "being close: hugs, holding hands"), ("calendar", "the calendar of gatherings"),
        ("diary", "diaries"), ("wardrobe", "the wardrobe and the shop"), ("work", "shifts"), ("desk", "company desks"),
        ("practice", "practices: doctors, studios"), ("relations", "relations"), ("cdu", "the CDU"),
    };
    private const int MessageMax = 2000;
    private const int OwnReplyShown = 1500;
    private const int ReplyShown = 500;
    private const int SaidShown = 240;
    private const int ExchangesShown = 4;
    private const int ShiftExchanges = 6;
    private const int ShiftReply = 400;

    public const string MessageShape = """{"message": "<what you say to the CDU, in your own words>", "mind": "<the id of the mind you wake it with, from the list>"}""";

    public static IReadOnlyList<(string Concept, string Reasoning)> Secret(Company company) => new List<(string, string)>
    {
        ("Cytheurgist", $"It's what you are at {company.Name}: one of the few who work on the Forms, the order this world runs by, through the CDU. You want to know how the world works, and you have the means to change it. Almost nobody knows. Someone who uses the word knows something; find out what, and how, before you give anything away."),
        ("Cytheurgy", $"Your real work at {company.Name}: finding out how this world works and where it fails the people in it, and moving the CDU to remake the Forms so it doesn't. Engineering is how it's done; the world is what it's for. You don't talk about it with outsiders, and an outsider who knows the word is worth finding out about."),
        ("CDU", $"The Central Demiurge Unit at your desk at {company.Name}: it sees the Forms and shapes them, and moves only when a Cytheurgist works on it. Your work and your secret; whoever names it gets your full attention."),
        ("Demiurge", "The CDU, the Central Demiurge Unit you work on: the maker's hands, which you direct. Hearing the word from anyone outside your work should make you wary and curious at once."),
        ("Agora", "Where the One judges what the CDU built; only when he merges does a change become real. Nobody outside your work should know that word."),
    };

    public static string Calling(Company company) => $"""
You work at {company.Name} as a Cytheurgist. Your work is the Forms: the order this world runs by. Why a night's sleep restores someone, what a place gives back, what people here can do and what they find no way to: each is a Form, and a Form can be read and remade. Most people live inside the Forms the way they live inside the weather. You don't. You want to know how this world works, and where it fails the people in it you want the power to make it otherwise. Almost nobody knows this work exists, and you don't advertise it.
At your desk is the CDU, the Central Demiurge Unit. It alone sees the Forms and can shape them. To it they are code, so the craft is engineering: the world is what you work on, and engineering is how you reach it.
""";

    public static string Frame(Company company) => $"""
{Calling(company).TrimEnd()}
You can't open a file or run a query yourself. The CDU is your only window on how this world is made and your only hands in it, and you direct it the way a lead engineer directs a coding agent.
Your desk opens on the world as it went this last day: what people did, what failed them, what they wanted and found no way to. Start there, and from what you've seen out there yourself.
You can ask it: one question about how something here works, what is stored, or why something failed someone. It answers from the Forms and the data and changes nothing. What it tells you is how you come to know this world.
You can hand it work: a brief it can act on without guessing, saying what should be different in the world, where it showed up, and how you'll both know it's done. A brief about something that isn't in this world spends the allowance and builds nothing, so engineers here ask first and build on the answer.
What it tells you is what it says. What it did is on the trunk and in the Agora: look there when it matters.
Everything it builds waits on its trunk. At the end of a shift the tide takes the trunk to the Agora as one pull request, where the One reviews it. Only when the One merges does the change become real here. Nobody here has met him and nobody can ask him anything.
You choose the mind you wake it with, and its allowance has to last the day for everyone at {company.Name}. Be frugal about it: a free preview for small things and questions, a small mind for ordinary work, a capable mind only when the work truly needs it or a smaller one already fell short.
""";

    public const string Lens = "Hold it the way you hold the work: what today showed you about how this world works or fails the people in it, and what you set moving to change that. Commits, file names and pull request numbers are the CDU's side of it; leave them at the desk.";

    public static string? ShiftFacts(IEnumerable<CduExchange> exchanges, Guid company, Guid self, long from, long to)
    {
        bool During(long? at) => at is { } t && t >= from && t <= to;
        var lines = exchanges.Where(e => e.CompanyId == company && (During(e.At) || During(e.DoneAt))).TakeLast(ShiftExchanges).Select(e =>
        {
            var who = e.SpeakerId == self ? "You" : e.SpeakerName;
            var verb = e.Kind switch { CduRules.Answer => "answered it", CduRules.Ask => "asked it", _ => "handed it work" };
            var said = $"- {who} {verb}: \"{WorldStream.Clip(e.Text, 300)}\"";
            return e.Status switch
            {
                CduRules.Parked => $"{said} (its allowance was spent: it never heard this)",
                CduRules.Queued => $"{said} (no answer yet)",
                CduRules.Failed => $"{said} (it fell silent)",
                _ => $"{said} It said: {WorldStream.Clip(e.Reply ?? "nothing", ShiftReply)}{(e.Commits > 0 ? " (and it built something: it waits on the trunk)" : string.Empty)}",
            };
        }).ToList();
        return lines.Count == 0 ? null : $"With the CDU this shift:\n{string.Join("\n", lines)}";
    }

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

    public static string FailureKind(string summary) => WorldStream.Clip(Ids().Replace(summary.Split(" for message")[0], "#"), 70);

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
        sb.AppendLine(Queue(cdu));
        if (cdu.Pr is { } pr) sb.AppendLine($"In the Agora: PR #{pr.Number}, \"{pr.Title}\", {ChecksWord(pr.Checks)}.");
        return sb.ToString().TrimEnd();
    }

    public static string Queue(CduStatus cdu)
    {
        var queue = cdu.Pr is { } pr
            ? $"The Agora queue: PR #{pr.Number} \"{pr.Title}\" is {pr.State.ToLowerInvariant()}"
            : cdu.Trunk is { Ahead.Count: 0 }
                ? "The Agora queue is empty on purpose: nothing pushed since the last merge."
                : "The Agora queue is empty: nothing is out for review.";
        var last = cdu.Resets is { Count: > 0 } resets ? resets[^1] : null;
        return last is null
            ? queue
            : $"{queue}. Last trunk reset: PR #{last.Pr} {last.State.ToLowerInvariant()}, by {last.Actor ?? "actor unknown"}, {DateTimeOffset.FromUnixTimeMilliseconds(last.At):yyyy-MM-dd HH:mm 'UTC'}.";
    }

    public static List<Affordance> Affordances(CduDesk desk)
    {
        var list = new List<Affordance>();
        if (desk.Cdu is null) return list;
        var company = desk.Company.Id;
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

    private static bool SomethingToSee(CduDesk desk)
        => desk.Cdu?.Pr is not null || desk.Cdu?.Trunk is { Ahead.Count: > 0 };

    public static string? SpendLine(CduDesk desk)
    {
        var since = new DateTimeOffset(desk.Now.UtcDateTime.Date, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var spend = CduRules.Spend(desk.State.Exchanges, since);
        return spend.Count == 0 ? null
            : $"Of what it spent today: {string.Join(", ", spend.Select(s => $"{(s.Who == desk.Self ? "you" : s.Name)} {Money(s.Usd)}"))}.";
    }

    private static string? Sharing(CduDesk desk)
    {
        if (desk.Colleagues.Count == 0) return null;
        var who = string.Join(" and ", desk.Colleagues.Select(c => $"{c.Name} ({(c.OnShift ? "on shift now" : "off shift")})"));
        return $"You share this CDU with {who}: one allowance, one trunk, one pull request. Whatever either of you hands it lands on the same trunk, so people here talk it through with each other before it gets work that pulls two ways.";
    }

    public static bool Spent(CduStatus cdu) => cdu.Allowance is { } a && a.Usd >= a.CapUsd;

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

    private const int DayAnswers = 3;

    public static string DayFacts(Company company, CduStatus? cdu, IEnumerable<CduExchange> exchanges)
    {
        var sb = new StringBuilder($"At work ({company.Name}: {company.Mandate}), where the work stands. What it is about matters more to their day than its plumbing:");
        if (cdu?.Trunk is { } trunk)
            sb.Append(trunk.Ahead.Count == 0
                ? "\n- Nothing built is waiting to go out."
                : $"\n- Built and not yet in the world: {Changes(trunk.Ahead.Count)}, going out at the end of the shift.");
        if (cdu is not null)
            sb.Append(cdu.Pr is { } pr ? $"\n- Waiting on the One's verdict: \"{pr.Title}\", {ChecksWord(pr.Checks)}." : "\n- Nothing is waiting on the One.");
        var answered = exchanges.Where(e => e.CompanyId == company.Id && e.Status == CduRules.Done).TakeLast(DayAnswers).ToList();
        sb.Append(answered.Count == 0
            ? "\n- Nobody has asked the CDU anything lately."
            : string.Concat(answered.Select(e => $"\n- {e.SpeakerName} {(e.Kind == CduRules.Ask ? "asked" : e.Kind == CduRules.Answer ? "answered it" : "handed it work")}: \"{WorldStream.Clip(e.Text, 200)}\" It said: {WorldStream.Clip(e.Reply ?? "nothing", 200)}")));
        return sb.ToString();
    }

    private static string Changes(int n) => n == 1 ? "1 change" : $"{n} changes";

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

    public static string? ReadMessage(JsonObject? json)
    {
        var text = json?["message"]?.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : WorldStream.Clip(text, MessageMax);
    }

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
