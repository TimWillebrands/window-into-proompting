using System.Collections.Concurrent;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;
using PartyTown.World.Desires;

namespace PartyTown.World.Self;

/// <summary>
/// What's done off-screen is settled off-screen (ADR 0055): a promise to do something nobody can watch being done is
/// settled at its time, done or not by who the promiser is, and both of them learn how it ended. Detached and non-fatal.
/// </summary>
public sealed class OffStage(ISystemOneJudge judge, IWorldLlm llm, WorldMemory memory, IGrainFactory grains, ILogger<OffStage> logger)
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, byte> _settling = new();

    /// <summary>Settles each of their promises whose time has come; fire-and-forget.</summary>
    public void SettleDue(Guid partyId, Guid personaId, IEnumerable<Desire> acquired)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var d in acquired.Where(d => PromiseRules.OffStageDue(d, now)).ToList())
        {
            if (!_settling.TryAdd(d.Id, 0)) continue;
            _ = Task.Run(async () =>
            {
                try { await SettleAsync(partyId, personaId, d); }
                catch (Exception ex) { logger.LogWarning(ex, "Settling a promise off-screen failed for {Persona}", personaId); }
                finally { _settling.TryRemove(d.Id, out _); }
            });
        }
    }

    private async Task SettleAsync(Guid partyId, Guid personaId, Desire promise)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        if (cast.FirstOrDefault(c => c.Id == personaId) is not { } self || cast.FirstOrDefault(c => c.Id == promise.TargetId) is not { } holder) return;
        using var cts = new CancellationTokenSource(Budget);
        var said = OffStageRules.Said(self.Name, holder.Name, promise);

        var answers = await judge.For(personaId).AskAsync($"{WorldLlm.Identity(self)}\n\n{said}",
            new Dictionary<string, SystemOneQuestion> { ["done"] = new NoulQuestion(OffStageRules.Done(self.Name)) }, cts.Token);
        // Drawn, not argmaxed: who they are makes it likelier, never certain.
        var p = answers.Noul("done", 0.5);
        var done = Random.Shared.NextDouble() < p;
        var json = await llm.For(personaId).CompleteJsonAsync(JobComplexity.General, OffStageRules.Brief(done), said, cts.Token, 0.9, ["outcome"]);
        if (OffStageRules.Outcome(json?["outcome"]?.ToString()) is not { } outcome) return;

        var desires = grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, personaId));
        // Kept or called off in talk while this was being read: that already settled it.
        if ((await desires.GetAsync()).Acquired.FirstOrDefault(d => d.Id == promise.Id) is not { Status: DesireStatus.Active }) return;
        if (!await desires.MarkAsync(promise.Id, done ? DesireStatus.Fulfilled : DesireStatus.Abandoned)) return;
        if (!done)
            await grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, holder.Id)).ShiftWarmthAsync(personaId, Work.ResponsibilityRules.NoShow);

        var clock = grains.GetGrain<IWorldClockGrain>(partyId);
        var activity = new WorldActivity
        {
            PersonaId = personaId, PersonaName = self.Name, App = "life", Action = OffStageRules.Action, Summary = outcome, WithPersonaId = holder.Id,
        };
        await clock.RecordAsync(activity);
        // Word gets around: both of them know how it ended, so neither asks again.
        var present = new List<ParticipantView> { new(self.Id, self.Name, self.DefaultDriver), new(holder.Id, holder.Name, holder.DefaultDriver) };
        _ = memory.CaptureAsync(new Moment(partyId, MomentSources.World, activity.Id,
            new List<MomentLine> { new(Narrator.PersonaId, OffStageRules.Line(self.Name, outcome), activity.Ts) }, 0, present), CancellationToken.None);
        if (holder.DefaultDriver == DriverKind.LLM) await clock.NotifyAsync(new List<Guid> { holder.Id });
        logger.LogInformation("Off-screen {Persona} → {Holder}: {Done} (p={P:F2}) {Outcome}", self.Name, holder.Name, done ? "done" : "dropped", p, outcome);
    }
}

/// <summary>Pure rules for settling a promise off-screen (ADR 0055).</summary>
public static class OffStageRules
{
    public const string Action = "offstage";

    /// <summary>The promise as the judge and the narrator read it; a put-off counts.</summary>
    public static string Said(string promiser, string holder, Desire promise)
    {
        var again = promise.Again switch { 0 => string.Empty, 1 => " They said it again once since.", _ => $" They said it again {promise.Again} times since." };
        return $"{promiser} told {holder} {promise.Text}.{again}";
    }

    public static string Done(string promiser)
        => $"Being who they are, with the life they have, {promiser} actually got it done, away from any chat.";

    public static string Brief(bool done) => $"""
You are the narrator of a small world. Someone told someone else they would do something. It got settled off-screen: they {(done ? "did it" : "didn't do it")}.
Say how it ended, in one or two plain sentences, past tense: what came of it, concrete and specific to what it was.
{(done
    ? "It's done and over: no next step, nothing still owed."
    : "They let it go. Say how it ended anyway: it's late now, someone else did it, or it stopped mattering. Nothing is left to do.")}
No moral, no feelings, no reasons. Start with a past-tense verb about them, without their name ("sent the form off at ten to six; …").
Output ONLY JSON: {"{"}"outcome": "<one or two sentences>"{"}"}
""";

    /// <summary>The narrator's answer, trimmed; null when there's nothing usable.</summary>
    public static string? Outcome(string? raw)
    {
        var text = raw?.Trim().Trim('"').Trim();
        return string.IsNullOrWhiteSpace(text) || text.Length < 8 ? null : WorldStream.Clip(text, 300);
    }

    /// <summary>How it's remembered by both.</summary>
    public static string Line(string promiser, string outcome) => $"[Settled off-screen] {promiser} {outcome}";
}
