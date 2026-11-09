using System.Globalization;
using System.Text;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World.Apps;
using PartyTown.World.Self;

namespace PartyTown.World;

/// <summary>
/// A Persona's Life inside one Party (key = <c>party:persona</c>): the Initiative instance of
/// relevance realization (ADR 0018). Perceive every App → recall → pick one Affordance → act.
/// Stateless between turns — own history lives in the World clock's activity log.
/// </summary>
public sealed class PersonaLifeGrain(
    IEnumerable<IWorldApp> apps,
    IWorldLlm llm,
    WorldMemory memory,
    ISystemOneJudge judge,
    ILogger<PersonaLifeGrain> logger,
    FollowThrough? followThrough = null,
    Work.Responsibilities? responsibilities = null,
    Desires.DesireReview? desireReview = null,
    Reflex.ReflexGate? reflex = null)
    : Grain, IPersonaLifeGrain
{
    private const int MaxOptions = 10;
    private const int MaxOptionsPerApp = 3;
    private static readonly TimeSpan TurnBudget = TimeSpan.FromSeconds(150);
    private const double ChoiceTemperature = 0.8;
    /// <summary>Activity action of a look (opening an App, scrolling back): not habituating, followed by a choice.</summary>
    public const string LookAction = "look";
    /// <summary>When a lack was last voiced; per activation, the cap is soft.</summary>
    private long? _lackAt;
    private const string IntentShape = """{"thought": "<one sentence, first person, what's on your mind>", "intent": "<what exactly you want to say or do, concrete, 1-2 sentences: the substance, not how to word it (no length, language or words to avoid)>", "decline": <true if you wouldn't actually do this after all, else false>}""";

    public async Task<WorldActivity> LiveAsync(string? nudge)
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        if (followThrough is not null) await followThrough.ConsiderPendingAsync(partyId, personaId);
        // Under the 3 min grain response timeout, so a stalled model call ends as a fizzle, not a timeout.
        using var turnCts = new CancellationTokenSource(TurnBudget);
        var (activity, looked) = await LiveCoreAsync(nudge, turnCts.Token);
        // Looking is a step, not a turn: having opened something, decide what to do with what you now see.
        if (looked)
        {
            await GrainFactory.GetGrain<IWorldClockGrain>(partyId).RecordAsync(activity);
            (activity, _) = await LiveCoreAsync($"You just {activity.Summary}. Now that it's in front of you, what do you do?", turnCts.Token, lookedAlready: true);
        }
        // Time to yourself recharges (ADR 0021): resting and writing are how talked-out people refill.
        if (activity.Action is "rest" or "diary")
        {
            if (activity.Action == "diary" && desireReview is not null) _ = ReviewDesiresAsync(partyId, personaId);
            try { await GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, personaId)).RechargeAsync(SelfMath.SolitudeRecharge); }
            catch (Exception ex) { logger.LogDebug(ex, "Recharge failed for {Persona}", personaId); }
        }
        return activity;
    }

    /// <returns>The activity, and whether it was only a look (opening an App, scrolling back) that wants a follow-up choice.</returns>
    private async Task<(WorldActivity Activity, bool Looked)> LiveCoreAsync(string? nudge, CancellationToken ct, bool lookedAlready = false)
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var party = GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var self = cast.FirstOrDefault(c => c.Id == personaId && c.DefaultDriver == DriverKind.LLM);
        if (self is null)
            return (Activity(personaId, "?", "life", "error", "is not an LLM-driven participant here"), false);

        var clock = GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var wants = await WantsAsync(partyId, personaId);
        var threads = await party.GetChatGroups();
        var now = DateTimeOffset.UtcNow;
        var snapshot = await SnapshotAsync(partyId);
        var me = snapshot?.Bodies.FirstOrDefault(b => b.ParticipantId == personaId);
        var happenings = (await clock.GetActivitiesAsync(4, Narrator.PersonaId))
            .Where(a => a.Action == "happening" && now - DateTimeOffset.FromUnixTimeMilliseconds(a.Ts) < HappeningRules.Lingers)
            .Select(a => HappeningRules.Relate(a, me, snapshot?.Places ?? new List<Places.Place>(), personaId))
            .ToList();
        var ctx = new LifeContext(
            partyId, self, cast,
            cast.Where(c => c.DefaultDriver == DriverKind.User).Select(c => c.Id).ToHashSet(),
            threads,
            (await clock.GetActivitiesAsync(12, personaId))
                .Where(a => a.Action is not ("fizzle" or "error" or Desires.Lack.Action)).Take(8).ToList(),
            now,
            await GrainFactory.GetGrain<IPersonaAttentionGrain>(WorldKeys.Life(partyId, personaId)).GetLooksAsync(),
            // A promise due reaches its person, their chat and (for a come-over) where they are; then wants (ADR 0026, 0043).
            Desires.DesireRules.People(wants, cast.Where(c => c.Id != personaId && c.Id != Narrator.PersonaId).Select(c => (c.Id, c.Name)), now.ToUnixTimeMilliseconds(),
                other => threads.FirstOrDefault(t => t.Kind == ChatGroupKinds.Direct && t.MemberIds?.Contains(other) == true && t.MemberIds.Contains(personaId))?.Id,
                other => snapshot?.Bodies.FirstOrDefault(b => b.ParticipantId == other)?.PlaceId),
            happenings);

        var perceptions = (await Task.WhenAll(apps.Select(a => PerceiveSafeAsync(a, ctx, ct))))
            .Where(p => p is not null).Select(p => p!).ToList();

        // Habituation: an App you just used pulls less (explore ⇄ exploit); urgent items still surface.
        var recentApps = ctx.RecentOwn.Where(a => a.Action != LookAction).Take(3).Select(a => a.App).ToList();
        var lastWalk = ctx.RecentOwn.FirstOrDefault(a => a.App == "places" && a.Action == "walk");
        var sinceWalk = lastWalk is null ? (TimeSpan?)null : ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(lastWalk.Ts);
        var onShift = await OnShiftAsync(partyId, personaId);
        // Wanting and owing get a handle (ADR 0026, 0043): an option toward that person pulls one more; a promise also outlasts Settling.
        int Pull(Affordance a) => a.Priority - recentApps.Count(app => app == a.AppId) - Settling.Penalty(a, sinceWalk, ctx.Owes(a.TargetId))
            - Work.Shifts.Damping(a.AppId, onShift) + (ctx.WantAbout(a.TargetId) is null ? 0 : 1);

        // Each App gets at most a few seats on the menu: modes compete on pull, not on how many options they list.
        var options = perceptions
            .SelectMany(p => p.Affordances
                .Where(a => !(lookedAlready && a.Look))
                .OrderByDescending(a => Pull(a) + Random.Shared.NextDouble())
                .Take(MaxOptionsPerApp))
            .OrderByDescending(a => Pull(a) + Random.Shared.NextDouble())
            .Take(MaxOptions)
            .ToList();

        var anchorText = string.Join(" ", perceptions.Select(p => p.Block));
        var (memories, stances) = await memory.RecallAsync(partyId, personaId, cast.Select(c => c.Id).ToList(), anchorText, ct);
        var presence = await PresenceAsync(partyId, personaId);
        var rest = onShift ? Hooks.RestOnShift : Hooks.Rest(await BeatAsync(partyId, personaId));
        if (responsibilities is not null)
            presence += await responsibilities.RoleAsync(partyId, personaId) + await responsibilities.RenderAsync(partyId, personaId, ctx.Now);
        presence += await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetKeyLinesAsync(personaId);
        if (onShift) presence += "\n" + Hooks.ShiftWins;
        presence += Desires.DesireRules.Render(wants);
        if (reflex is not null) presence += await reflex.MindAsync(partyId, personaId);
        var ties = await GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, personaId))
            .RenderAsync(cast.Where(c => c.Id != personaId && c.DefaultDriver != DriverKind.System).Select(c => c.Id).ToList(), here: false);

        var sb = new StringBuilder();
        if (string.IsNullOrWhiteSpace(presence))
            sb.AppendLine($"It is {ctx.Now.ToLocalTime().ToString("dddd HH:mm", CultureInfo.InvariantCulture)}.");
        else
            sb.AppendLine(presence.Trim());
        if (!string.IsNullOrWhiteSpace(nudge)) sb.AppendLine($"Something nudges you: {nudge}");
        if (happenings.Count > 0)
        {
            sb.AppendLine("\n# Lately in the world");
            sb.AppendLine(string.Join("\n", happenings.Select(h => HappeningRules.Line(h, ctx.Now))));
        }
        sb.AppendLine();
        sb.AppendLine("# On your mind");
        // The now first (ADR 0021): a few recent things on the mind, not a scrapbook.
        sb.AppendLine(memories.Count == 0 ? "(nothing in particular)" : string.Join("\n", memories.Take(3).Select(m => $"- {m.Snippet}")));
        if (!string.IsNullOrWhiteSpace(ties)) sb.AppendLine($"\n{ties.Trim()}");
        if (stances.Count > 0)
        {
            sb.AppendLine("\n# Where you stand");
            sb.AppendLine(string.Join("\n", stances.Select(s => $"- {s}")));
        }
        sb.AppendLine("\n# What you did recently");
        sb.AppendLine(ctx.RecentOwn.Count == 0
            ? "(nothing yet today)"
            : string.Join("\n", ctx.RecentOwn.Take(5).Select(a => $"- {WorldStream.Ago(a.Ts, ctx.Now)}: {a.Action} {a.Summary}")));
        sb.AppendLine("\n# The world right now");
        foreach (var p in perceptions)
            sb.AppendLine($"## {p.Title}\n{p.Block}\n");
        var world = sb.ToString();
        sb.AppendLine("# Options");
        string Read(Affordance a) => Desires.DesireRules.Toward(a.Label, ctx.WantAbout(a.TargetId));
        for (var i = 0; i < options.Count; i++)
            sb.AppendLine($"{i + 1}. {Read(options[i])}");
        sb.AppendLine($"0. {rest}");

        // System One weighs the Affordances; sampling keeps exploration alive (opponent processing,
        // not argmax). System Two only voices the one that won.
        var state = $"{WorldLlm.Identity(self)}\n\n{sb}";
        var optionMap = Enumerable.Range(0, options.Count + 1).ToDictionary(
            i => i.ToString(CultureInfo.InvariantCulture),
            i => (string?)(i == 0 ? rest : Read(options[i - 1])));
        var questions = new Dictionary<string, SystemOneQuestion>
        {
            ["next"] = new ChoiceQuestion($"Which option would {self.Name}, being who they are, most plausibly choose next?", optionMap),
        };
        // Reaching past the menu rides the same call (ADR 0047): only a first look at the world, not a follow-up.
        if (!lookedAlready) questions[Desires.Lack.Key] = Desires.Lack.Question(self.Name);
        var answers = await judge.For(self.Id).AskAsync(state, questions, ct);
        if (!lookedAlready && Desires.Lack.Felt(answers.Noul(Desires.Lack.Key, 0), _lackAt, now))
        {
            _lackAt = now.ToUnixTimeMilliseconds();
            _ = FeelLackAsync(partyId, personaId, self, sb.ToString());
        }
        var choice = int.TryParse(answers.SampleChoice("next", ChoiceTemperature), out var c) ? c : 0;
        if (choice < 1 || choice > options.Count)
            return (Activity(personaId, self.Name, "life", "rest", "is taking it easy"), false);

        // A Look needs no voice: what to say comes after seeing.
        if (options[choice - 1].Look)
            return await ActOnAsync(personaId, self, ctx, options[choice - 1], options[choice - 1].Label, null, ct);

        // No options menu here: with it in view the intent drifted to another App's option.
        var decided = options[choice - 1];
        var where = perceptions.FirstOrDefault(p => p.AppId == decided.AppId)?.Title ?? decided.AppId;
        var voiced = await llm.For(personaId).CompleteJsonAsync(JobComplexity.General,
            $"{WorldLlm.Identity(self)}\n\nYou've decided what to do next. Say what's on your mind and what exactly you'll do: only this one thing, in {where}, nothing from elsewhere.\n{LifeChoice.DeclineCue}\nOutput ONLY JSON: {IntentShape}",
            $"{world}\n# What you've decided\n{decided.Label} ({where})", ct, 0.8, ["thought", "intent"]);
        var thought = voiced?["thought"]?.ToString();
        var intent = voiced?["intent"]?.ToString() ?? string.Empty;
        // System Two has the last word (ADR 0034): a pick it wouldn't make in character doesn't happen.
        if (LifeChoice.Declined(voiced))
        {
            logger.LogInformation("Life {Persona} ✗ {Label} | {Thought}", self.Name, decided.Label, thought);
            return (Activity(personaId, self.Name, "life", "rest", $"thought better of it ({decided.Label})", thought), false);
        }

        return await ActOnAsync(personaId, self, ctx, decided, intent, thought, ct);
    }

    private async Task<(WorldActivity Activity, bool Looked)> ActOnAsync(
        Guid personaId, CastMember self, LifeContext ctx, Affordance picked, string intent, string? thought, CancellationToken ct)
    {
        var app = apps.First(a => a.Id == picked.AppId);
        logger.LogInformation("Life {Persona} → {Label} | {Intent}", self.Name, picked.Label, intent);
        try
        {
            var outcome = await app.ActAsync(ctx, picked, string.IsNullOrWhiteSpace(intent) ? picked.Label : intent, thought, ct);
            if (outcome is not null) await TellAsync(ctx.PartyId, personaId, picked.Label);
            return outcome is null
                ? (Activity(personaId, self.Name, app.Id, "fizzle", $"meant to: {picked.Label}", thought), false)
                : (Activity(personaId, self.Name, app.Id, outcome.Action, outcome.Summary, thought, outcome.TargetId, outcome.WithPersonaId), outcome.Looked);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Life {Persona} failed acting on {Label}", self.Name, picked.Label);
            return (Activity(personaId, self.Name, app.Id, "error", $"tried to {picked.Label}: {WorldStream.Clip(ex.Message, 120)}", thought), false);
        }
    }

    /// <summary>Voices what they reached for that nothing here affords, off the turn's clock; "none" leaves no trace.</summary>
    private async Task FeelLackAsync(Guid partyId, Guid personaId, CastMember self, string world)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var json = await llm.For(personaId).CompleteJsonAsync(JobComplexity.General, Desires.Lack.Ask(WorldLlm.Identity(self)), world, cts.Token, 0.7, ["want"]);
            if (Desires.Lack.Read(json?["want"]?.ToString()) is not { } want) return;
            logger.LogInformation("Lack {Persona}: {Want}", self.Name, want);
            await GrainFactory.GetGrain<IWorldClockGrain>(partyId).RecordAsync(Activity(personaId, self.Name, "life", Desires.Lack.Action, want));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Lack failed for {Persona}", self.Name);
        }
    }

    /// <summary>Their wants (ADR 0026), Initiative only, so wanting steers what they do, not what they talk about.</summary>
    private async Task<List<Desires.Desire>> WantsAsync(Guid partyId, Guid personaId)
    {
        try { return (await GrainFactory.GetGrain<Desires.IDesiresGrain>(WorldKeys.Life(partyId, personaId)).GetAsync()).All.ToList(); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Desires failed for {Persona}", personaId);
            return new List<Desires.Desire>();
        }
    }

    private async Task<Places.PlacesSnapshot?> SnapshotAsync(Guid partyId)
    {
        try { return await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync(); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Places snapshot failed for Life in {Party}", partyId);
            return null;
        }
    }

    private async Task ReviewDesiresAsync(Guid partyId, Guid personaId)
    {
        var cast = await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        if (cast.FirstOrDefault(c => c.Id == personaId) is not { } self) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await desireReview!.ReviewAsync(partyId, self, cast.Select(c => c.Id).ToList(), cts.Token);
    }

    private async Task<bool> OnShiftAsync(Guid partyId, Guid personaId)
    {
        try { return (await GrainFactory.GetGrain<Work.IOccupationGrain>(WorldKeys.Life(partyId, personaId)).GetAsync()).OnShift; }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Shift lookup failed for {Persona}", personaId);
            return false;
        }
    }

    /// <summary>What the day has them doing now, for the option of doing nothing in particular.</summary>
    private async Task<string?> BeatAsync(Guid partyId, Guid personaId)
    {
        try { return SelfRender.Beat(await GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, personaId)).GetStateAsync(), DateTimeOffset.Now); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Day beat failed for {Persona}", personaId);
            return null;
        }
    }

    private async Task TellAsync(Guid partyId, Guid personaId, string label)
    {
        try { await GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, personaId)).TellAsync(label); }
        catch (Exception ex) { logger.LogDebug(ex, "Telling an opener failed for {Persona}", personaId); }
    }

    private async Task<string?> PresenceAsync(Guid partyId, Guid personaId)
    {
        try { return await GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, personaId)).GetPresenceAsync(withDay: true); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Presence failed for {Persona}", personaId);
            return null;
        }
    }

    private async Task<AppPerception?> PerceiveSafeAsync(IWorldApp app, LifeContext ctx, CancellationToken ct)
    {
        try { return await app.PerceiveAsync(ctx, ct); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "App {App} perception failed for {Persona}", app.Id, ctx.Self.Name);
            return null;
        }
    }

    private static WorldActivity Activity(Guid personaId, string name, string app, string action, string summary,
        string? thought = null, Guid? target = null, Guid? with = null)
        => new()
        {
            PersonaId = personaId, PersonaName = name, App = app, Action = action,
            Summary = summary, Thought = thought, TargetId = target, WithPersonaId = with,
        };
}

[Alias("PartyTown.World.IPersonaLifeGrain")]
public interface IPersonaLifeGrain : IGrainWithStringKey
{
    /// <summary>One Initiative turn. <paramref name="nudge"/> is an optional author hint folded into perception.</summary>
    [Alias("LiveAsync")]
    Task<WorldActivity> LiveAsync(string? nudge);
}

/// <summary>System Two voicing System One's pick (ADR 0034), with the say to decline it.</summary>
public static class LifeChoice
{
    public const string DeclineCue = "If, being who you are right now, you wouldn't actually do this after all, set decline to true and say why in thought.";

    public static bool Declined(System.Text.Json.Nodes.JsonObject? voiced)
        => string.Equals(voiced?["decline"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>People settle where they've just arrived (ADR 0021): walking on again soon needs a real reason.</summary>
public static class Settling
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>Pull taken off a movement option this soon after a walk; strong pulls (a Gathering, your name) and a promise due are exempt.</summary>
    public static int Penalty(Affordance a, TimeSpan? sinceWalk, bool promised = false)
    {
        if (a.AppId != "places" || sinceWalk is not { } since || since >= Window || a.Priority >= 3 || promised) return 0;
        return a.Verb switch
        {
            "go" => 2,
            "join" => 1,
            { } v when v.StartsWith("spot:", StringComparison.Ordinal) => 1,
            _ => 0,
        };
    }
}

