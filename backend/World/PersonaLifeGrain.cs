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
    ILogger<PersonaLifeGrain> logger)
    : Grain, IPersonaLifeGrain
{
    private const int MaxOptions = 10;
    private const int MaxOptionsPerApp = 3;
    private static readonly TimeSpan TurnBudget = TimeSpan.FromSeconds(150);
    private const double ChoiceTemperature = 0.8;
    private const string IntentShape = """{"thought": "<one sentence, first person, what's on your mind>", "intent": "<what exactly you want to say or do, concrete, 1-2 sentences>"}""";
    private const string ChoiceShape = """{"thought": "<one sentence, first person, what's on your mind>", "choice": "<option number>", "intent": "<what exactly you want to say or do, concrete, 1-2 sentences>"}""";

    public async Task<WorldActivity> LiveAsync(string? nudge)
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        // Under the 3 min grain response timeout, so a stalled model call ends as a fizzle, not a timeout.
        using var turnCts = new CancellationTokenSource(TurnBudget);
        var ct = turnCts.Token;
        var party = GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var self = cast.FirstOrDefault(c => c.Id == personaId && c.DefaultDriver == DriverKind.LLM);
        if (self is null)
            return Activity(personaId, "?", "life", "error", "is not an LLM-driven participant here");

        var clock = GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var ctx = new LifeContext(
            partyId, self, cast,
            cast.Where(c => c.DefaultDriver == DriverKind.User).Select(c => c.Id).ToHashSet(),
            await party.GetChatGroups(),
            (await clock.GetActivitiesAsync(12, personaId))
                .Where(a => a.Action is not ("fizzle" or "error")).Take(8).ToList(),
            DateTimeOffset.UtcNow);

        var happenings = (await clock.GetActivitiesAsync(4, Narrator.PersonaId))
            .Where(a => a.Action == "happening" && ctx.Now - DateTimeOffset.FromUnixTimeMilliseconds(a.Ts) < TimeSpan.FromHours(12))
            .ToList();

        var perceptions = (await Task.WhenAll(apps.Select(a => PerceiveSafeAsync(a, ctx, ct))))
            .Where(p => p is not null).Select(p => p!).ToList();

        // Habituation: an App you just used pulls less (explore ⇄ exploit); urgent items still surface.
        var recentApps = ctx.RecentOwn.Take(3).Select(a => a.App).ToList();
        int Pull(Affordance a) => a.Priority - recentApps.Count(app => app == a.AppId);

        // Each App gets at most a few seats on the menu: modes compete on pull, not on how many options they list.
        var options = perceptions
            .SelectMany(p => p.Affordances
                .OrderByDescending(a => Pull(a) + Random.Shared.NextDouble())
                .Take(MaxOptionsPerApp))
            .OrderByDescending(a => Pull(a) + Random.Shared.NextDouble())
            .Take(MaxOptions)
            .ToList();

        var anchorText = string.Join(" ", perceptions.Select(p => p.Block));
        var (memories, stances) = await memory.RecallAsync(partyId, personaId, cast.Select(c => c.Id).ToList(), anchorText, ct);
        var presence = await PresenceAsync(partyId, personaId);
        var ties = await GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, personaId))
            .RenderAsync(cast.Where(c => c.Id != personaId && c.DefaultDriver != DriverKind.System).Select(c => c.Id).ToList());

        var system = $"""
{WorldLlm.Identity(self)}

You live in a small world where a handful of people hang out across a few modes: {string.Join("; ", perceptions.Select(p => p.Title))}. Nobody is asking anything of you right now. You're deciding what you feel like doing next, as yourself.

Pick ONE option. Let your character, mood and relationships decide. Someone waiting on a reply usually matters — unless you'd rather avoid them. Don't repeat what you just did; people drift between apps. Resting is a fine choice.
You have a life beyond this desktop (see "Right now"): your day, your mood, how much social energy you have left. Not everything has to be about what the others are discussing; plenty of what people do online is ordinary, light or idle. When your battery is low, resting or something solitary is natural.
Output ONLY JSON: {ChoiceShape}
""";

        var sb = new StringBuilder();
        if (string.IsNullOrWhiteSpace(presence))
            sb.AppendLine($"It is {ctx.Now.ToLocalTime().ToString("dddd HH:mm", CultureInfo.InvariantCulture)}.");
        else
            sb.AppendLine(presence.Trim());
        if (!string.IsNullOrWhiteSpace(nudge)) sb.AppendLine($"Something nudges you: {nudge}");
        if (happenings.Count > 0)
        {
            sb.AppendLine("\n# Lately in the world");
            sb.AppendLine(string.Join("\n", happenings.Select(h => $"- {WorldStream.Ago(h.Ts, ctx.Now)}: {h.Summary}")));
        }
        sb.AppendLine();
        sb.AppendLine("# On your mind");
        sb.AppendLine(memories.Count == 0 ? "(nothing in particular)" : string.Join("\n", memories.Take(6).Select(m => $"- {m.Snippet}")));
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
        sb.AppendLine("# Options");
        for (var i = 0; i < options.Count; i++)
            sb.AppendLine($"{i + 1}. {options[i].Label}");
        sb.AppendLine("0. Do nothing for now");

        string? thought;
        string intent;
        int choice;
        if (judge.Enabled)
        {
            // System One weighs the Affordances; sampling keeps exploration alive (opponent processing,
            // not argmax). System Two only voices the one that won.
            var state = $"{WorldLlm.Identity(self)}\n\n{sb}";
            var optionMap = Enumerable.Range(0, options.Count + 1).ToDictionary(
                i => i.ToString(CultureInfo.InvariantCulture),
                i => (string?)(i == 0 ? "Do nothing for now" : options[i - 1].Label));
            var answers = await judge.AskAsync(state, new Dictionary<string, SystemOneQuestion>
            {
                ["next"] = new ChoiceQuestion($"Which option would {self.Name}, being who they are, most plausibly choose next?", optionMap),
            }, ct);
            choice = int.TryParse(answers.SampleChoice("next", ChoiceTemperature), out var c) ? c : 0;
            if (choice < 1 || choice > options.Count)
                return Activity(personaId, self.Name, "life", "rest", "is taking it easy");

            var voiced = await llm.CompleteJsonAsync(JobComplexity.General,
                $"{WorldLlm.Identity(self)}\n\nYou've decided what to do next: {options[choice - 1].Label}. Say what's on your mind and what exactly you'll do.\nOutput ONLY JSON: {IntentShape}",
                sb.ToString(), ct, 0.8, ["thought", "intent"]);
            thought = voiced?["thought"]?.ToString();
            intent = voiced?["intent"]?.ToString() ?? string.Empty;
        }
        else
        {
            var json = await llm.CompleteJsonAsync(JobComplexity.General, system, sb.ToString(), ct, 0.8, ["thought", "choice", "intent"]);
            thought = json?["thought"]?.ToString();
            intent = json?["intent"]?.ToString() ?? string.Empty;
            var choiceRaw = json?["choice"]?.ToString() ?? "0";
            choice = int.TryParse(new string(choiceRaw.Where(char.IsDigit).ToArray()), out var n) ? n : 0;
        }

        if (choice < 1 || choice > options.Count)
            return Activity(personaId, self.Name, "life", "rest", "is taking it easy", thought);

        var picked = options[choice - 1];
        var app = apps.First(a => a.Id == picked.AppId);
        logger.LogInformation("Life {Persona} → {Label} | {Intent}", self.Name, picked.Label, intent);
        try
        {
            var outcome = await app.ActAsync(ctx, picked, string.IsNullOrWhiteSpace(intent) ? picked.Label : intent, thought, ct);
            return outcome is null
                ? Activity(personaId, self.Name, app.Id, "fizzle", $"meant to: {picked.Label}", thought)
                : Activity(personaId, self.Name, app.Id, outcome.Action, outcome.Summary, thought, outcome.TargetId, outcome.WithPersonaId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Life {Persona} failed acting on {Label}", self.Name, picked.Label);
            return Activity(personaId, self.Name, app.Id, "error", $"tried to {picked.Label}: {WorldStream.Clip(ex.Message, 120)}", thought);
        }
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
