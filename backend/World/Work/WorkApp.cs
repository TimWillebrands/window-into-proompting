using System.Text;
using PartyTown.World.Apps;

namespace PartyTown.World.Work;

/// <summary>
/// Work in a Life (ADR 0026). The clock starts and ends shifts; this App only offers what a shift affords:
/// get on with it, get back to it, or walk out early.
/// </summary>
public sealed class WorkApp(IGrainFactory grains, Workplaces workplaces) : IWorldApp
{
    public const string AppId = "work";

    public string Id => AppId;

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var view = await Occupation(ctx).GetAsync();
        if (view.Job is not { } job) return null;
        await SyncAsync(ctx, job);
        var local = ctx.Now.ToLocalTime();
        var atWork = await AtWorkAsync(ctx, job);

        var sb = new StringBuilder();
        sb.AppendLine($"Your job: {Shifts.Describe(job)}.");
        if (Shifts.Line(job, view.Today, local, atWork) is { } now) sb.AppendLine(now);

        var affordances = new List<Affordance>();
        if (view.OnShift)
        {
            if (atWork)
            {
                affordances.Add(new Affordance(Id, $"Get on with work ({job.Title})", 2, job.PlaceId, "work"));
                // Staff are the centre of a Place (ADR 0026): someone who just came in is theirs to greet.
                if (job.PlaceId is { } placeId && await GreetAsync(ctx, placeId) is { } greet) affordances.Add(greet);
            }
            else
                affordances.Add(new Affordance(Id, $"Head back to work at {job.Workplace}", 3, job.PlaceId, "return"));
            affordances.Add(new Affordance(Id, "Leave work early", 0, job.PlaceId, "leave"));
        }
        return new AppPerception(Id, "Work", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        var occupation = Occupation(ctx);
        var view = await occupation.GetAsync();
        if (view.Job is not { } job || !view.OnShift) return null;
        switch (affordance.Verb)
        {
            case "work":
                await grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId).SetActivityAsync(ctx.Self.Id, job.Onscreen ? "working" : "at work", null);
                return new ActOutcome("work", $"is busy at work ({job.Title}, {job.Workplace})", job.PlaceId);
            case "return":
                await workplaces.GoAsync(ctx.PartyId, ctx.Self.Id, job);
                return new ActOutcome("return", $"is heading back to work at {job.Workplace}", job.PlaceId);
            case "greet" when affordance.TargetId is { } talkRoomId:
            {
                var line = await grains.GetGrain<PartyTown.Grains.IPersonaGrain>(ctx.Self.Id).InitiateAsync(talkRoomId, intent, thought);
                return string.IsNullOrWhiteSpace(line) ? null : new ActOutcome("greet", $"greeted someone at {job.Workplace}: \"{WorldStream.Clip(line, 160)}\"", talkRoomId);
            }
            case "leave":
                if (await occupation.LeaveEarlyAsync(ctx.Now.ToLocalTime()) is not { } record) return null;
                var pay = await workplaces.LeaveEarlyAsync(ctx.PartyId, ctx.Self.Id, job, record);
                return new ActOutcome("leave", $"left work at {job.Workplace} early (+{pay} credits)", job.PlaceId);
        }
        return null;
    }

    private async Task<Affordance?> GreetAsync(LifeContext ctx, Guid placeId)
    {
        var snapshot = await grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId).GetSnapshotAsync();
        if (snapshot.Places.FirstOrDefault(p => p.Id == placeId)?.TalkRoomId is not { } talkRoomId) return null;
        var arrival = Roles.NewArrivals(snapshot, placeId, ctx.Self.Id, snapshot.Now).FirstOrDefault();
        return arrival is null ? null : new Affordance(Id, $"Greet {ctx.NameOf(arrival.ParticipantId)}, who just came in (you work here)", 2, talkRoomId, "greet");
    }

    /// <summary>A view copy, so Openings and renames show now, not at the next shift edge.</summary>
    private async Task SyncAsync(LifeContext ctx, Occupation job)
    {
        var places = (await grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId).GetSnapshotAsync()).Places;
        if (places.FirstOrDefault(p => p.Id == (job.PlaceId ?? job.LocaleId)) is { } place) WorkRules.Sync(job, place);
    }

    /// <summary>Whether the Body is at the workplace: the Place onscreen, the Locale offscreen.</summary>
    private async Task<bool> AtWorkAsync(LifeContext ctx, Occupation job)
        => (job.PlaceId ?? job.LocaleId) is { } where && await grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId).GetPlaceOfAsync(ctx.Self.Id) == where;

    private IOccupationGrain Occupation(LifeContext ctx) => grains.GetGrain<IOccupationGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id));
}
