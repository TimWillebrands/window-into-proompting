using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PartyTown.World.Apps;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Works;

/// <summary>What the last day looked like from the outside, counted rather than felt.</summary>
public sealed record WorldHealth(int Activities, int Failed, int Resting, IReadOnlyList<(string What, int Times)> Failures);

/// <summary>Everything on the Architect's desk at one moment.</summary>
public sealed record WorksDesk(Company Company, IReadOnlyList<Commission> Commissions, RunnerStatus? Runner, WorldHealth Health, Guid Self);

/// <summary>An order read from a voiced decision.</summary>
public sealed record CommissionDraft(string Title, string Why, string Brief, string Model, double BudgetUsd);

/// <summary>Pure: what an Architect sees, what they can do about it, and how an intent becomes an order (ADR 0039).</summary>
public static partial class WorksScreen
{
    public const string AppId = "works";
    public const string VerbCommission = "commission";
    public const string VerbSurvey = "survey";
    public const string VerbSteer = "steer";
    public const string VerbPropose = "propose";
    public const string VerbDrop = "drop";

    public const double MinBudget = 0.01;
    public const double MaxBudget = 25;
    public const double DefaultBudget = 1;
    public const double SurveyBudget = 0.25;
    private const int ReportShown = 700;
    private const int MaxFailures = 4;
    private const int TitleMax = 80;
    private const int WhyMax = 400;
    private const int BriefMax = 1500;

    public const string CommissionShape = """{"title": "<short>", "why": "<what you saw that makes this worth doing, in your own words>", "brief": "<what should behave differently for people here, concretely>", "builder": "<a builder id from the list>", "budget": "<dollars you will spend>"}""";
    public const string SurveyShape = """{"question": "<what you want to understand about how this world works>", "builder": "<a builder id from the list>", "budget": "<dollars you will spend>"}""";
    public const string SteerShape = """{"message": "<what you tell the builder next>", "add_budget": "<extra dollars, or 0>"}""";

    /// <summary>The fiction staff work inside, and only staff: it's in no sheet and nobody else's prompt.</summary>
    public static string Frame(Company company) => $"""
You work at {company.Name}, and you know what almost nobody here knows: this world is made. It runs on a codebase, and the people in it, you included, run on that code. {company.Name} is the small outfit with access to it, and you don't advertise that.
From your desk you commission changes and a builder carries them out against the code. You don't need to code. You decide what's worth changing, and say so clearly.
Finished work becomes a proposition, and it goes live only if the world-spirits take it. Nobody here has met them and nobody can ask them. You can keep building on what's still waiting for them.
Everything the builder does spends the reserve, the same money that lets everyone here think. When it runs out, everyone stops.
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

    public static string Glance(Company company, IReadOnlyList<Commission> commissions)
    {
        var open = commissions.FirstOrDefault(c => c.Kind == CommissionKind.Change && CommissionRules.IsOpen(c));
        var waiting = commissions.Count(c => c.Status == CommissionStatus.Proposed);
        var parts = new List<string> { open is null ? "Nothing on the bench." : $"On the bench: {open.Title}, {Word(open)}." };
        if (waiting > 0) parts.Add($"{Waiting(waiting)} with the world-spirits.");
        return $"At {company.Name}: {string.Join(" ", parts)}";
    }

    public static string Render(WorksDesk desk)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{desk.Company.Name}: {desk.Company.Mandate}");
        sb.AppendLine(Reserve(desk.Runner));
        sb.AppendLine(HealthLines(desk.Health));
        if (desk.Runner is null) sb.AppendLine("The builder isn't answering.");
        else sb.AppendLine($"Builders you can hire:\n{string.Join("\n", desk.Runner.Models.Select(Hire))}");

        var open = desk.Commissions.FirstOrDefault(c => c.Kind == CommissionKind.Change && CommissionRules.IsOpen(c));
        if (open is not null) sb.AppendLine(Bench(open));
        var waiting = desk.Commissions.Where(c => c.Status == CommissionStatus.Proposed).ToList();
        if (waiting.Count > 0) sb.AppendLine($"With the world-spirits:\n{string.Join("\n", waiting.Select(c => $"- {c.Title} ({ChecksLine(c)})"))}");
        var heard = desk.Commissions.Where(c => c.Status is CommissionStatus.Accepted or CommissionStatus.Rejected or CommissionStatus.Fallen).TakeLast(3).ToList();
        if (heard.Count > 0) sb.AppendLine($"Their word so far:\n{string.Join("\n", heard.Select(Verdict))}");
        return sb.ToString().TrimEnd();
    }

    public static List<Affordance> Affordances(WorksDesk desk)
    {
        var list = new List<Affordance>();
        if (desk.Runner is null) return list;
        var company = desk.Company.Id;
        var open = desk.Commissions.FirstOrDefault(c => c.Kind == CommissionKind.Change && CommissionRules.IsOpen(c));
        var builderFree = !desk.Runner.Busy;
        if (open is null && builderFree) list.Add(new(AppId, "Commission a change to how the world works", 2, company, VerbCommission));
        if (builderFree) list.Add(new(AppId, "Ask the builder how something here actually works", 1, company, VerbSurvey));
        if (open is { Status: CommissionStatus.Waiting } && builderFree)
        {
            list.Add(new(AppId, $"Tell the builder what to do next on \"{open.Title}\"", 3, open.Id, VerbSteer));
            if (open.Files > 0) list.Add(new(AppId, $"Send \"{open.Title}\" to the world-spirits", 3, open.Id, VerbPropose));
            list.Add(new(AppId, $"Drop \"{open.Title}\"", 1, open.Id, VerbDrop));
        }
        return list;
    }

    public static string Reserve(RunnerStatus? runner) => runner?.ReserveUsd is { } left
        ? $"The reserve: {Money(left)}."
        : "The reserve: nobody can read it from here.";

    private static string HealthLines(WorldHealth health)
    {
        if (health.Activities == 0) return "Nothing happened here in the last day.";
        var sb = new StringBuilder($"The last day: {health.Activities} things people did, {health.Failed} of them failed, {health.Resting} were someone resting.");
        foreach (var (what, times) in health.Failures) sb.Append($"\n- failed {times}x: {what}");
        return sb.ToString();
    }

    private static string Hire(RunnerModel model)
    {
        var price = model.In == 0 && model.Out == 0 ? "free" : $"{Money(model.In)} in, {Money(model.Out)} out per million tokens";
        return $"- {model.Id}: {model.Label}, {price}";
    }

    private static string Bench(Commission c)
    {
        var sb = new StringBuilder($"On the bench: \"{c.Title}\", from {c.ArchitectName}, {Word(c)}. {Money(c.SpentUsd)} of {Money(c.BudgetUsd)} spent.");
        sb.Append($"\nThe brief: {c.Brief}");
        if (c.Status != CommissionStatus.Waiting) return sb.ToString();
        sb.Append(c.Files == 0
            ? "\nThe code has not changed."
            : $"\nThe code changed: {c.Files} files, +{c.Added} -{c.Removed}: {string.Join(", ", c.Paths.Take(6))}.");
        if (c.Stop is { } stop) sb.Append($"\nIt stopped early: {Stopped(stop)}.");
        if (!string.IsNullOrWhiteSpace(c.Report)) sb.Append($"\nThe builder says, which is only what it says: {WorldStream.Clip(c.Report, ReportShown)}");
        return sb.ToString();
    }

    private static string Stopped(string stop) => stop switch
    {
        "budget" => "the money you gave it ran out",
        "time" => "it ran out of time",
        "crash" => "the builder fell over",
        _ => stop,
    };

    private static string Verdict(Commission c) => c.Status switch
    {
        CommissionStatus.Accepted => $"- took \"{c.Title}\"",
        CommissionStatus.Fallen => $"- \"{c.Title}\" fell with what it stood on",
        _ => $"- turned down \"{c.Title}\"{(string.IsNullOrWhiteSpace(c.Note) ? string.Empty : $": {c.Note}")}",
    };

    public static string ChecksLine(Commission c)
    {
        if (c.Checks.Count == 0) return "not checked yet";
        var failed = c.Checks.Where(k => !k.Passed).Select(k => k.Name).ToList();
        return failed.Count == 0 ? $"all {c.Checks.Count} checks passed" : $"failed: {string.Join(", ", failed)}";
    }

    public static string Word(Commission c) => c.Status switch
    {
        CommissionStatus.Filed or CommissionStatus.Working => "the builder is on it",
        CommissionStatus.Waiting => "waiting on an Architect",
        CommissionStatus.Checking => "being checked",
        _ => c.Status.ToString().ToLowerInvariant(),
    };

    private static string Waiting(int count) => count == 1 ? "One proposition" : $"{count} propositions";

    public static string Money(double usd) => $"${usd.ToString(usd is > 0 and < 1 ? "0.00##" : "0.00", CultureInfo.InvariantCulture)}";

    /// <summary>A voiced commission; null when they decided on nothing, or left out what it's for.</summary>
    public static CommissionDraft? ReadCommission(JsonObject? json, IReadOnlyList<RunnerModel> models, double? reserve)
    {
        var title = Text(json?["title"], TitleMax);
        var brief = Text(json?["brief"], BriefMax);
        if (title is null || brief is null || title.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        return new CommissionDraft(title, Text(json?["why"], WhyMax) ?? title, brief, Builder(json?["builder"], models), Budget(json?["budget"], DefaultBudget, reserve));
    }

    /// <summary>A question as a read-only draft; the question stands in as title and brief.</summary>
    public static CommissionDraft? ReadSurvey(JsonObject? json, IReadOnlyList<RunnerModel> models, double? reserve)
    {
        if (Text(json?["question"], BriefMax) is not { } question) return null;
        return new CommissionDraft(WorldStream.Clip(question, TitleMax), question, question, Builder(json?["builder"], models), Budget(json?["budget"], SurveyBudget, reserve));
    }

    /// <summary>What to tell the builder next, and any extra money; null when they had nothing to say.</summary>
    public static (string Message, double AddUsd)? ReadSteer(JsonObject? json, double? reserve)
    {
        if (Text(json?["message"], BriefMax) is not { } message) return null;
        return (message, Number(json?["add_budget"]) is { } extra and > 0 ? Math.Min(extra, reserve ?? MaxBudget) : 0);
    }

    private static string Builder(JsonNode? node, IReadOnlyList<RunnerModel> models)
    {
        var said = node?.ToString().Trim();
        return models.FirstOrDefault(m => string.Equals(m.Id, said, StringComparison.OrdinalIgnoreCase))?.Id ?? models[0].Id;
    }

    /// <summary>Their number, held to what exists: a cap of zero would stop the run before it starts.</summary>
    public static double Budget(JsonNode? node, double fallback, double? reserve)
    {
        var ceiling = Math.Max(Math.Min(reserve ?? MaxBudget, MaxBudget), MinBudget);
        return Math.Clamp(Number(node) ?? fallback, MinBudget, ceiling);
    }

    private static double? Number(JsonNode? node)
        => double.TryParse(Numeric().Match(node?.ToString() ?? string.Empty).Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? Text(JsonNode? node, int max)
    {
        var text = node?.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : WorldStream.Clip(text, max);
    }

    /// <summary>What the agent is told. Its repo instructions come from the repo, which the Architects can change.</summary>
    public static string Order(Company company, Commission c) => c.Kind == CommissionKind.Survey ? Question(company, c) : $"""
You are the builder at {company.Name}. {c.ArchitectName}, an Architect here, commissioned this.
/work is the code of the world they live in: a C#/.NET and Orleans backend and a React frontend, where AI personas live. Read CLAUDE.md and CONTEXT.md first, then only what the work needs.

Commission: {c.Title}
Why: {c.Why}
What should change: {c.Brief}

Make the smallest change that does this, following the repo's conventions. There is no internet and no new packages can be added. Run `dotnet build backend/backend.csproj`, then the tests covering what you touched: `dotnet test backend-test/backend-test.csproj`, and `pnpm test` in frontend/. Postgres is on localhost:5455.
Finish with a short report in plain words for someone who doesn't read code: what now behaves differently, what you checked, what you didn't do.
""";

    private static string Question(Company company, Commission c) => $"""
You are the builder at {company.Name}. {c.ArchitectName}, an Architect here, wants to understand the world they live in. /work is its code: a C#/.NET and Orleans backend and a React frontend, where AI personas live. CONTEXT.md and docs/adr/ explain the ideas.
Change nothing. Read what you need and answer in plain words, under 200 words, for someone who doesn't read code:
{c.Brief}
""";

    [GeneratedRegex(@"[0-9a-fA-F-]{8,}")]
    private static partial Regex Ids();

    [GeneratedRegex(@"-?\d+(\.\d+)?")]
    private static partial Regex Numeric();
}
