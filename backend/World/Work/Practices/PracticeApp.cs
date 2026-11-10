using System.Globalization;
using System.Text;
using PartyTown.Services.SystemOne;
using PartyTown.World.Apps;
using PartyTown.World.Economy;
using PartyTown.World.Relations;
using PartyTown.World.Self;

namespace PartyTown.World.Work.Practices;

/// <summary>Practices around you in a Life (ADR 0030). What pulls is judged per person, so no trade needs its own rules.</summary>
public sealed class PracticeApp(IGrainFactory grains, ISystemOneJudge judge, Sessions sessions, ILogger<PracticeApp> logger) : IWorldApp
{
    public const string AppId = "practice";
    private const int MaxOffers = 3;

    public string Id => AppId;

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var others = ctx.Others.ToList();
        var jobs = await Task.WhenAll(others.Select(o => grains.GetGrain<IOccupationGrain>(WorldKeys.Life(ctx.PartyId, o.Id)).GetAsync()));
        var practices = others.Select((o, i) => (Who: o, Job: jobs[i].Job))
            .Where(p => p.Job?.Practice is not null).ToList();
        if (practices.Count == 0) return null;

        var book = grains.GetGrain<ISessionsGrain>(ctx.PartyId);
        var open = (await book.GetSessionsAsync()).Where(s => s.ClientId == ctx.Self.Id && s.Status is SessionStatus.Booked or SessionStatus.Running).ToList();
        var balance = (await grains.GetGrain<IWalletGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetAsync()).Balance;

        var relations = await grains.GetGrain<IRelationsGrain>(ctx.PartyId).GetAsync();
        var sb = new StringBuilder();
        var offers = new List<Affordance>();
        foreach (var (who, job) in practices)
        {
            var practice = job!.Practice!;
            sb.AppendLine($"- {PracticeRules.Advert(who.Name, job, practice)}");
            // Nobody hires their own partner: that talk happens at home, unpaid.
            if (balance < practice.Fee || open.Any(s => s.PractitionerId == who.Id) || relations.Partners(ctx.Self.Id, who.Id)) continue;
            if (PracticeRules.Pull(await FitAsync(ctx, who.Id, who.Name, job, practice, ct)) is { } pull)
                offers.Add(new Affordance(Id, $"Book a session with {who.Name} ({job.Title}, {practice.Fee} credits)", pull, who.Id, "book"));
        }
        return new AppPerception(Id, "People you could see", sb.ToString().TrimEnd(), offers.OrderByDescending(o => o.Priority).Take(MaxOffers).ToList());
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.Verb != "book" || affordance.TargetId is not { } practitionerId) return null;
        var (session, refusal) = await sessions.BookAsync(ctx.PartyId, ctx.Self.Id, practitionerId, intent, ctx.Now);
        if (session is null) return new ActOutcome("fizzle", $"couldn't book {ctx.NameOf(practitionerId)}: {refusal}", null);
        var at = DateTimeOffset.FromUnixTimeMilliseconds(session.StartsAt).ToLocalTime().ToString("ddd HH:mm", CultureInfo.InvariantCulture);
        return new ActOutcome("book", $"booked a session with {session.PractitionerName} for {at}", null, practitionerId);
    }

    /// <summary>One read a day of whether a Practice speaks to what's going on with them.</summary>
    private async Task<double?> FitAsync(LifeContext ctx, Guid practitionerId, string name, Occupation job, Practice practice, CancellationToken ct)
    {
        var weigh = judge.For(ctx.Self.Id);
        var book = grains.GetGrain<ISessionsGrain>(ctx.PartyId);
        var day = Shifts.DayKey(ctx.Now.ToLocalTime());
        if (await book.GetFitAsync(ctx.Self.Id, practitionerId, day) is { } known) return known;
        try
        {
            var presence = await grains.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetPresenceAsync(withDay: true);
            var answers = await weigh.AskAsync($"{WorldLlm.Identity(ctx.Self)}\n\n{presence}", new Dictionary<string, SystemOneQuestion>
            {
                ["fit"] = new NoulQuestion($"{ctx.Self.Name} has something going on right now that {name}, a {job.Title} ({practice.Field}), could really help with."),
            }, ct);
            if (!answers.Nouls.TryGetValue("fit", out var fit)) return null;
            await book.SetFitAsync(ctx.Self.Id, practitionerId, day, fit);
            return fit;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Practice fit failed for {Who}", ctx.Self.Name);
            return null;
        }
    }
}
