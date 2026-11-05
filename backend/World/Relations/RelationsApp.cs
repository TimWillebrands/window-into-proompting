using System.Text;
using PartyTown.Grains;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.World.Relations;

/// <summary>
/// Relations changing in play (ADR 0035): asked in person, between two people at the same spot, and a yes is needed.
/// The asking stays private; only what comes of it is public.
/// </summary>
public sealed class RelationsApp(IGrainFactory grains) : IWorldApp
{
    private const string Action = "relation";
    private const string Yes = "yes";
    private const string No = "no";
    private const string BreakUp = "breakup";
    private const int Answering = 5;

    public string Id => "relations";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var book = await grains.GetGrain<IRelationsGrain>(ctx.PartyId).GetAsync();
        var now = ctx.Now.ToUnixTimeMilliseconds();
        var sb = new StringBuilder();
        var affordances = new List<Affordance>();
        foreach (var ask in book.OpenTo(ctx.Self.Id, now).Where(a => ctx.Others.Any(o => o.Id == a.FromId)))
        {
            var name = ctx.NameOf(ask.FromId);
            sb.AppendLine(RelationRules.Asked(ask, name));
            affordances.Add(new Affordance(Id, $"Say yes to {name}", Answering, ask.FromId, Yes));
            affordances.Add(new Affordance(Id, $"Say no to {name}", Answering, ask.FromId, No));
        }
        foreach (var ask in book.FreshlyAnswered(ctx.Self.Id, now)) sb.AppendLine(RelationRules.Answered(ask, ctx.NameOf(ask.ToId)));

        var snapshot = await grains.GetGrain<IPlacesGrain>(ctx.PartyId).GetSnapshotAsync();
        if (snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id) is { IsWalking: false, Asleep: false } me)
        {
            var company = snapshot.Bodies.Where(b => b.ParticipantId != ctx.Self.Id && b.PlaceId == me.PlaceId && b.SpotId == me.SpotId && !b.IsWalking && !b.Asleep);
            foreach (var other in company.Take(3)) affordances.AddRange(await OffersAsync(ctx, book, other.ParticipantId, now));
        }
        return sb.Length == 0 && affordances.Count == 0
            ? null
            : new AppPerception(Id, "Relationships (what you are to people)", sb.ToString().TrimEnd(), affordances);
    }

    private async Task<List<Affordance>> OffersAsync(LifeContext ctx, RelationBook book, Guid other, long now)
    {
        var myTies = grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id));
        var theirTies = grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, other));
        var mine = await myTies.GetTieAsync(other);
        var theirs = await theirTies.GetTieAsync(ctx.Self.Id);
        var mutual = await myTies.GetClosestAsync() == other && await theirTies.GetClosestAsync() == ctx.Self.Id;
        var name = ctx.NameOf(other);
        var offers = RelationRules.Askable(book, ctx.Self.Id, other, mine, theirs, ctx.IsHuman(other), mutual, now)
            .Select(k => new Affordance(Id, RelationRules.Label(k, name), 1, other, k.ToString()))
            .ToList();
        if (RelationRules.MayBreakUp(book, ctx.Self.Id, other, mine)) offers.Add(new Affordance(Id, $"Break up with {name}", 0, other, BreakUp));
        return offers;
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } other || affordance.Verb is not { } verb) return null;
        var relations = grains.GetGrain<IRelationsGrain>(ctx.PartyId);
        var name = ctx.NameOf(other);
        switch (verb)
        {
            case BreakUp:
                if (!await relations.EndAsync(ctx.Self.Id, other)) return null;
                await SayAsync(ctx, other, intent, thought);
                return new ActOutcome(Action, $"breaks up with {name}", null, other);
            case Yes or No:
                if (await relations.AnswerAsync(other, ctx.Self.Id, verb == Yes) is not { } ask) return null;
                await SayAsync(ctx, other, intent, thought);
                return new ActOutcome(Action, ask.Yes == true ? RelationRules.Done(ask.Kind, name) : $"has a quiet word with {name}", null, other);
            default:
                if (!Enum.TryParse<AskKind>(verb, out var kind) || !await relations.AskAsync(ctx.Self.Id, other, kind)) return null;
                await SayAsync(ctx, other, intent, thought);
                return new ActOutcome(Action, $"has a quiet word with {name}", null, other);
        }
    }

    /// <summary>Said low, when they're right there; otherwise the answer stands on its own.</summary>
    private async Task SayAsync(LifeContext ctx, Guid other, string intent, string? thought)
    {
        var places = grains.GetGrain<IPlacesGrain>(ctx.PartyId);
        var snapshot = await places.GetSnapshotAsync();
        var me = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
        var them = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == other);
        if (me is null || them is null || me.IsWalking || them.IsWalking || me.PlaceId != them.PlaceId || me.SpotId != them.SpotId) return;
        if (snapshot.Places.FirstOrDefault(p => p.Id == me.PlaceId)?.TalkRoomId is not { } room) return;
        await places.SetVoiceAsync(ctx.Self.Id, Volumes.Whisper, other);
        await grains.GetGrain<IPersonaGrain>(ctx.Self.Id).InitiateAsync(room, intent, thought);
    }
}
