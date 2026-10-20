using System.Globalization;
using System.Text;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.World.Apps;

namespace PartyTown.World;

/// <summary>
/// A Persona's Life inside one Party (key = <c>party:persona</c>): the Initiative instance of
/// relevance realization (ADR 0018). Perceive every App → recall → pick one Affordance → act.
/// Stateless between turns — own history lives in the World clock's activity log.
/// </summary>
public sealed class PersonaLifeGrain(
    IEnumerable<IWorldApp> apps,
    WorldLlm llm,
    WorldMemory memory,
    ILogger<PersonaLifeGrain> logger)
    : Grain, IPersonaLifeGrain
{
    private const int MaxOptions = 10;
    private const string ChoiceShape = """{"thought": "<one sentence, first person, what's on your mind>", "choice": "<option number>", "intent": "<what exactly you want to say or do, concrete, 1-2 sentences>"}""";

    public async Task<WorldActivity> LiveAsync(string? nudge)
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var ct = CancellationToken.None;
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
            await clock.GetActivitiesAsync(8, personaId),
            DateTimeOffset.UtcNow);

        var perceptions = (await Task.WhenAll(apps.Select(a => PerceiveSafeAsync(a, ctx, ct))))
            .Where(p => p is not null).Select(p => p!).ToList();

        var options = perceptions
            .SelectMany(p => p.Affordances)
            .OrderByDescending(a => a.Priority + Random.Shared.NextDouble())
            .Take(MaxOptions)
            .ToList();

        var anchorText = string.Join(" ", perceptions.Select(p => p.Block));
        var (memories, stances) = await memory.RecallAsync(partyId, personaId, cast.Select(c => c.Id).ToList(), anchorText, ct);

        var system = $"""
{WorldLlm.Identity(self)}

You live in a small online world — a retro desktop where a handful of people hang out across a few apps: chat rooms, private messages, a public Board, and your own diary. Nobody is asking anything of you right now. You're deciding what you feel like doing next, as yourself.

Pick ONE option. Let your character, mood and relationships decide. Someone waiting on a reply usually matters — unless you'd rather avoid them. Don't repeat what you just did; people drift between apps. Resting is a fine choice.
Output ONLY JSON: {ChoiceShape}
""";

        var sb = new StringBuilder();
        sb.AppendLine($"It is {ctx.Now.ToLocalTime().ToString("dddd HH:mm", CultureInfo.InvariantCulture)}.");
        if (!string.IsNullOrWhiteSpace(nudge)) sb.AppendLine($"Something nudges you: {nudge}");
        sb.AppendLine();
        sb.AppendLine("# On your mind");
        sb.AppendLine(memories.Count == 0 ? "(nothing in particular)" : string.Join("\n", memories.Take(6).Select(m => $"- {m.Snippet}")));
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

        var json = await llm.CompleteJsonAsync(JobComplexity.General, system, sb.ToString(), ct, 0.8);
        var thought = json?["thought"]?.ToString();
        var intent = json?["intent"]?.ToString() ?? string.Empty;
        var choiceRaw = json?["choice"]?.ToString() ?? "0";
        var choice = int.TryParse(new string(choiceRaw.Where(char.IsDigit).ToArray()), out var n) ? n : 0;

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
                : Activity(personaId, self.Name, app.Id, outcome.Action, outcome.Summary, thought, outcome.TargetId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Life {Persona} failed acting on {Label}", self.Name, picked.Label);
            return Activity(personaId, self.Name, app.Id, "error", $"tried to {picked.Label}: {WorldStream.Clip(ex.Message, 120)}", thought);
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
        string? thought = null, Guid? target = null)
        => new()
        {
            PersonaId = personaId, PersonaName = name, App = app, Action = action,
            Summary = summary, Thought = thought, TargetId = target,
        };
}

[Alias("PartyTown.World.IPersonaLifeGrain")]
public interface IPersonaLifeGrain : IGrainWithStringKey
{
    /// <summary>One Initiative turn. <paramref name="nudge"/> is an optional author hint folded into perception.</summary>
    [Alias("LiveAsync")]
    Task<WorldActivity> LiveAsync(string? nudge);
}
