using PartyTown.Grains.Generation;
using PartyTown.World.Apps;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Works;

/// <summary>The Architect's desk in a Life (ADR 0039): one move per turn; the tool loop lives in the sandbox.</summary>
public sealed class WorksApp(IGrainFactory grains, IWorldLlm llm, Worksite site) : IWorldApp
{
    private const int HealthWindow = 300;

    public string Id => WorksScreen.AppId;

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var view = await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetAsync();
        if (view.Job?.CompanyId is not { } companyId || await Company(ctx, companyId) is not { } company) return null;
        var commissions = await Commissions(ctx).GetAllAsync();
        if (!view.OnShift) return new AppPerception(Id, "Work: the bench", WorksScreen.Glance(company, commissions), new List<Affordance>());
        var desk = await DeskAsync(ctx, company, commissions, ct);
        return new AppPerception(Id, $"Work: your desk at {company.Name}", $"{WorksScreen.Frame(company)}\n{WorksScreen.Render(desk)}", WorksScreen.Affordances(desk));
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target) return null;
        return affordance.Verb switch
        {
            WorksScreen.VerbCommission => await CommissionAsync(ctx, target, intent, ct),
            WorksScreen.VerbSurvey => await SurveyAsync(ctx, target, intent, ct),
            WorksScreen.VerbSteer => await SteerAsync(ctx, target, intent, ct),
            WorksScreen.VerbPropose => await MoveAsync(ctx, target, site.ProposeAsync, c => $"sent \"{c.Title}\" to the world-spirits", ct),
            WorksScreen.VerbDrop => await MoveAsync(ctx, target, site.DropAsync, c => $"dropped \"{c.Title}\"", ct),
            _ => null,
        };
    }

    private async Task<ActOutcome?> CommissionAsync(LifeContext ctx, Guid companyId, string intent, CancellationToken ct)
    {
        if (await Company(ctx, companyId) is not { } company || await site.StatusAsync(ct, fresh: true) is not { Models.Count: > 0 } runner) return null;
        var desk = await DeskAsync(ctx, company, await Commissions(ctx).GetAllAsync(), ct);
        var json = await AskAsync(ctx, company, desk,
            "Turn what you've decided into one commission. The builder reads code, not moods: say what should behave differently for people here, and point at what you saw on your desk or in your days that makes it worth doing. Pick a builder and a budget you can defend, knowing the reserve is everyone's.",
            WorksScreen.CommissionShape, new[] { "title", "why", "brief", "builder", "budget" }, intent, ct);
        if (WorksScreen.ReadCommission(json, runner.Models, runner.ReserveUsd) is not { } draft)
            return new ActOutcome("hold", $"sat at the desk at {company.Name} and ordered nothing", company.Id);
        var filed = await site.OrderAsync(ctx.PartyId, company, ctx.ViewOf(ctx.Self.Id), CommissionKind.Change, draft, ct);
        return Filed(filed, company, $"commissioned \"{draft.Title}\" at {company.Name}, {WorksScreen.Money(draft.BudgetUsd)}");
    }

    private async Task<ActOutcome?> SurveyAsync(LifeContext ctx, Guid companyId, string intent, CancellationToken ct)
    {
        if (await Company(ctx, companyId) is not { } company || await site.StatusAsync(ct, fresh: true) is not { Models.Count: > 0 } runner) return null;
        var desk = await DeskAsync(ctx, company, await Commissions(ctx).GetAllAsync(), ct);
        var json = await AskAsync(ctx, company, desk, "Turn what you've decided into one question for the builder about how something here actually works. Nothing changes; you'll just know more.",
            WorksScreen.SurveyShape, new[] { "question", "builder", "budget" }, intent, ct);
        if (WorksScreen.ReadSurvey(json, runner.Models, runner.ReserveUsd) is not { } draft)
            return new ActOutcome("hold", $"thought of a question at {company.Name} and let it go", company.Id);
        var filed = await site.OrderAsync(ctx.PartyId, company, ctx.ViewOf(ctx.Self.Id), CommissionKind.Survey, draft, ct);
        return Filed(filed, company, $"asked the builder at {company.Name}: {draft.Title}");
    }

    private async Task<ActOutcome?> SteerAsync(LifeContext ctx, Guid commissionId, string intent, CancellationToken ct)
    {
        if (await Commissions(ctx).GetAsync(commissionId) is not { } commission || await Company(ctx, commission.CompanyId) is not { } company) return null;
        var runner = await site.StatusAsync(ct, fresh: true);
        if (runner is null) return null;
        var desk = await DeskAsync(ctx, company, await Commissions(ctx).GetAllAsync(), ct);
        var json = await AskAsync(ctx, company, desk, "Turn what you've decided into what you tell the builder next. If you think it wants more money, say how much.",
            WorksScreen.SteerShape, new[] { "message", "add_budget" }, intent, ct);
        if (WorksScreen.ReadSteer(json, runner.ReserveUsd) is not var (message, addUsd))
            return new ActOutcome("hold", $"read over \"{commission.Title}\" and said nothing", commission.Id);
        var moved = await site.SteerAsync(ctx.PartyId, commission, message, addUsd, ct);
        return moved
            ? new ActOutcome("steer", $"told the builder what to do next on \"{commission.Title}\"", commission.Id)
            : new ActOutcome("hold", $"couldn't reach the builder about \"{commission.Title}\"", commission.Id);
    }

    private async Task<ActOutcome?> MoveAsync(LifeContext ctx, Guid commissionId, Func<Guid, Commission, CancellationToken, Task<bool>> move, Func<Commission, string> said, CancellationToken ct)
    {
        if (await Commissions(ctx).GetAsync(commissionId) is not { } commission) return null;
        return await move(ctx.PartyId, commission, ct)
            ? new ActOutcome("bench", said(commission), commission.Id)
            : new ActOutcome("hold", $"couldn't do anything with \"{commission.Title}\" right now", commission.Id);
    }

    private static ActOutcome Filed(Commission? filed, Company company, string said) => filed switch
    {
        null => new ActOutcome("hold", $"found the bench at {company.Name} already taken", company.Id),
        { Status: CommissionStatus.Failed } => new ActOutcome("hold", $"couldn't get the builder at {company.Name} going", company.Id),
        _ => new ActOutcome("commission", said, filed.Id),
    };

    private Task<System.Text.Json.Nodes.JsonObject?> AskAsync(LifeContext ctx, Company company, WorksDesk desk, string ask, string shape, string[] fields, string intent, CancellationToken ct)
        => llm.For(ctx.Self.Id).CompleteJsonAsync(JobComplexity.General,
            $"{WorldLlm.Identity(ctx.Self)}\n\n{WorksScreen.Frame(company)}\n{ask}\nOutput ONLY JSON: {shape}",
            $"{WorksScreen.Render(desk)}\n\nWhat you've decided: {intent}", ct, 0.3, fields);

    private async Task<WorksDesk> DeskAsync(LifeContext ctx, Company company, IReadOnlyList<Commission> commissions, CancellationToken ct)
    {
        var activities = await grains.GetGrain<IWorldClockGrain>(ctx.PartyId).GetActivitiesAsync(HealthWindow, null);
        return new WorksDesk(company, commissions, await site.StatusAsync(ct), WorksScreen.Health(activities, ctx.Now), ctx.Self.Id);
    }

    private async Task<Company?> Company(LifeContext ctx, Guid id)
        => await grains.GetGrain<ICompaniesGrain>(ctx.PartyId).GetAsync(id) is { Kind: CompanyKinds.Works } company ? company : null;

    private ICommissionsGrain Commissions(LifeContext ctx) => grains.GetGrain<ICommissionsGrain>(ctx.PartyId);
}
