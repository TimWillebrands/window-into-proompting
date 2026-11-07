using PartyTown.Grains.Generation;
using PartyTown.World.Apps;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Cdu;

/// <summary>A Cytheurgist's desk in a Life (ADR 0039): one move per on-shift turn; the CDU is the only agent.</summary>
public sealed class CduApp(IGrainFactory grains, IWorldLlm llm, Cytheurgy site) : IWorldApp
{
    private const int HealthWindow = 300;

    public string Id => CduScreen.AppId;

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var view = await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetAsync();
        if (view.Job?.CompanyId is not { } companyId || await Company(ctx, companyId) is not { } company) return null;
        if (!view.OnShift) return new AppPerception(Id, "Work", CduScreen.Glance(company), new List<Affordance>());
        var desk = await DeskAsync(ctx, company, ct);
        return new AppPerception(Id, $"Work: your desk at {company.Name}", $"{CduScreen.Frame(company)}\n{CduScreen.Render(desk)}", CduScreen.Affordances(desk));
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target || await Company(ctx, target) is not { } company) return null;
        return affordance.Verb switch
        {
            CduScreen.VerbTalk => await SayAsync(ctx, company, CduRules.Talk, intent, ct),
            CduScreen.VerbAnswer => await SayAsync(ctx, company, CduRules.Answer, intent, ct),
            CduScreen.VerbLook => new ActOutcome("look", await site.LookAsync(ctx.PartyId, company, ctx.ViewOf(ctx.Self.Id), ct), company.Id),
            _ => null,
        };
    }

    private async Task<ActOutcome?> SayAsync(LifeContext ctx, Company company, string kind, string intent, CancellationToken ct)
    {
        var desk = await DeskAsync(ctx, company, ct, fresh: true);
        if (desk.Cdu is null) return new ActOutcome("hold", $"found the CDU at {company.Name} dark", company.Id);
        var ask = kind == CduRules.Answer
            ? "Answer the CDU's question in your own words, as yourself. Say what you think, and why, plainly."
            : "Put what you've decided into words for the CDU, as yourself. Say what you want to be different for people here and what you saw that makes it worth it. It decides how; you bring the why.";
        var json = await llm.For(ctx.Self.Id).CompleteJsonAsync(JobComplexity.General,
            $"{WorldLlm.Identity(ctx.Self)}\n\n{CduScreen.Frame(company)}\n{ask}\nOutput ONLY JSON: {CduScreen.MessageShape}",
            $"{CduScreen.Render(desk)}\n\nWhat you've decided: {intent}", ct, 0.4, new[] { "message", "mind" });
        if (CduScreen.ReadMessage(json) is not { } message) return new ActOutcome("hold", $"sat with the CDU at {company.Name} and said nothing", company.Id);
        var mind = CduScreen.ReadMind(json, desk.Cdu.Models);
        var said = await site.SayAsync(ctx.PartyId, company, ctx.ViewOf(ctx.Self.Id), message, kind, mind, ct);
        if (said is null) return new ActOutcome("hold", $"couldn't reach the CDU at {company.Name}", company.Id);
        return new ActOutcome(kind, kind == CduRules.Answer ? $"answered the CDU at {company.Name}" : $"spoke to the CDU at {company.Name}", company.Id);
    }

    private async Task<CduDesk> DeskAsync(LifeContext ctx, Company company, CancellationToken ct, bool fresh = false)
    {
        var activities = await grains.GetGrain<IWorldClockGrain>(ctx.PartyId).GetActivitiesAsync(HealthWindow, null);
        var state = await grains.GetGrain<ICduGrain>(ctx.PartyId).GetAsync();
        return new CduDesk(company, state, await site.StatusAsync(ct, fresh), CduScreen.Health(activities, ctx.Now), ctx.Self.Id);
    }

    private async Task<Company?> Company(LifeContext ctx, Guid id)
        => await grains.GetGrain<ICompaniesGrain>(ctx.PartyId).GetAsync(id) is { } company && CompanyKinds.IsCdu(company.Kind) ? company : null;
}
