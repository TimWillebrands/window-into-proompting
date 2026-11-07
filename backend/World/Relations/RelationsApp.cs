using System.Text;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.World.Apps;
using PartyTown.World.Desires;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.World.Relations;

/// <summary>
/// Relations changing in play (ADR 0035): asked in person, between two people at the same spot, and a yes is needed.
/// The asking stays private; only what comes of it is public.
/// </summary>
public sealed class RelationsApp(IGrainFactory grains, WorldMemory? memory = null) : IWorldApp
{
    private const string Action = "relation";
    private const string Yes = "yes";
    private const string No = "no";
    private const string BreakUp = "breakup";
    private const string GiveKey = "givekey:";
    private const string AskKey = "askkey:";
    private const string ReturnKey = "returnkey:";
    private const string TakeKey = "takekey:";
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
            affordances.AddRange(await KeyOffersAsync(ctx, book, snapshot, me, now));
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

    /// <summary>A key comes up in person; a sour one needs settling wherever you are.</summary>
    private async Task<List<Affordance>> KeyOffersAsync(LifeContext ctx, RelationBook book, PlacesSnapshot snapshot, Body me, long now)
    {
        var self = ctx.Self.Id;
        var offers = new List<Affordance>();
        var acquired = (await grains.GetGrain<IDesiresGrain>(WorldKeys.Life(ctx.PartyId, self)).GetAsync()).Acquired;
        var ties = grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, self));
        var here = snapshot.Bodies.Where(b => b.ParticipantId != self && b.PlaceId == me.PlaceId && !b.IsWalking && !b.Asleep && !ctx.IsHuman(b.ParticipantId)).Select(b => b.ParticipantId).ToHashSet();
        var closed = snapshot.Places.Where(p => p.Keyed()).ToList();

        foreach (var place in closed.Where(p => p.Keyholders.Contains(self)))
        {
            var host = place.Hosts().FirstOrDefault();
            if (host == Guid.Empty) continue;
            var sour = KeyRules.Sour(book, self, host, await ties.GetTieAsync(host));
            if (sour || here.Contains(host)) offers.Add(new Affordance(Id, $"Give {ctx.NameOf(host)} back the key to {place.Name}", sour ? KeyRules.Sore : 0, host, $"{ReturnKey}{place.Id}"));
        }
        foreach (var place in closed.Where(p => p.Hosts().Contains(self)))
        {
            foreach (var holder in place.Keyholders)
                if (KeyRules.Sour(book, self, holder, await ties.GetTieAsync(holder)))
                    offers.Add(new Affordance(Id, $"Take back the key {ctx.NameOf(holder)} has to {place.Name}", KeyRules.Sore, holder, $"{TakeKey}{place.Id}"));
            foreach (var other in here.Where(o => !place.Hosts().Contains(o) && !place.Keyholders.Contains(o) && place.DoorstepsLately(o, now) >= KeyRules.ToThinkOfIt))
                if (await CloseAsync(ctx, book, ties, other))
                    offers.Add(new Affordance(Id, $"Give {ctx.NameOf(other)} a key to {place.Name}", KeyRules.Wants(acquired, place.Id, other) ? KeyRules.Wanted : 0, other, $"{GiveKey}{place.Id}"));
        }
        foreach (var place in closed.Where(p => !p.Hosts().Contains(self) && !p.Keyholders.Contains(self) && p.DoorstepsLately(self, now) >= KeyRules.ToThinkOfIt))
        {
            var host = place.Hosts().FirstOrDefault(here.Contains);
            if (host == Guid.Empty) continue;
            if (book.MayAsk(self, host, AskKind.Key, now) && await CloseAsync(ctx, book, ties, host))
                offers.Add(new Affordance(Id, $"Ask {ctx.NameOf(host)} for a key to {place.Name}", KeyRules.Wants(acquired, place.Id, host) ? KeyRules.Wanted : 0, host, $"{AskKey}{place.Id}"));
        }
        return offers;
    }

    private async Task<bool> CloseAsync(LifeContext ctx, RelationBook book, IPersonaTiesGrain ties, Guid other)
        => KeyRules.Close(book, ctx.Self.Id, other, await ties.GetTieAsync(other),
            await grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, other)).GetTieAsync(ctx.Self.Id));

    private async Task<ActOutcome?> ActOnKeyAsync(LifeContext ctx, string verb, Guid other, string intent, string? thought)
    {
        var relations = grains.GetGrain<IRelationsGrain>(ctx.PartyId);
        var places = grains.GetGrain<IPlacesGrain>(ctx.PartyId);
        var name = ctx.NameOf(other);
        var placeId = Guid.Parse(verb[(verb.IndexOf(':') + 1)..]);
        var place = (await places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == placeId)?.Name ?? "their place";
        switch (verb[..(verb.IndexOf(':') + 1)])
        {
            case GiveKey:
                if (!await relations.GiveKeyAsync(ctx.Self.Id, other, placeId)) return null;
                await SayInPersonAsync(ctx, other, intent, thought);
                Remember(ctx, other, $"{ctx.Self.Name} gave {name} a key to {place}.", placeId);
                return new ActOutcome(Action, $"gives {name} a key to {place}", null, other);
            case AskKey:
                if (!await relations.AskAsync(ctx.Self.Id, other, AskKind.Key, placeId, place)) return null;
                await SayInPersonAsync(ctx, other, intent, thought);
                return new ActOutcome(Action, $"has a quiet word with {name}", null, other);
            case ReturnKey:
                if (!await relations.DropKeyAsync(ctx.Self.Id, other, placeId, taken: false)) return null;
                await SayAsync(ctx, other, intent, thought);
                Remember(ctx, other, $"{ctx.Self.Name} gave {name} back the key to {place}.", placeId);
                return new ActOutcome(Action, $"gives {name} back the key to {place}", null, other);
            case TakeKey:
                if (!await relations.DropKeyAsync(other, ctx.Self.Id, placeId, taken: true)) return null;
                await SayAsync(ctx, other, intent, thought);
                Remember(ctx, other, $"{ctx.Self.Name} took back the key {name} had to {place}.", placeId);
                return new ActOutcome(Action, $"takes back the key {name} had to {place}", null, other);
            default:
                return null;
        }
    }

    /// <summary>Across the room is close enough to decide: they go over, and it's said if they're already there.</summary>
    private async Task SayInPersonAsync(LifeContext ctx, Guid other, string intent, string? thought)
    {
        await SayAsync(ctx, other, intent, thought);
        var places = grains.GetGrain<IPlacesGrain>(ctx.PartyId);
        var bodies = (await places.GetSnapshotAsync()).Bodies;
        if (bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id)?.SpotId != bodies.FirstOrDefault(b => b.ParticipantId == other)?.SpotId)
            await places.JoinAsync(ctx.Self.Id, other);
    }

    /// <summary>Only the two of them hold on to this; a Happening would tell everyone.</summary>
    private void Remember(LifeContext ctx, Guid other, string what, Guid placeId)
    {
        if (memory is null) return;
        var line = new MomentLine(ctx.Self.Id, $"[{what}]", ctx.Now.ToUnixTimeMilliseconds());
        _ = memory.CaptureAsync(new Moment(ctx.PartyId, MomentSources.World, Guid.NewGuid(), new List<MomentLine> { line }, 0, new List<ParticipantView> { ctx.ViewOf(ctx.Self.Id), ctx.ViewOf(other) }, placeId), CancellationToken.None);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } other || affordance.Verb is not { } verb) return null;
        if (verb.Contains(':')) return await ActOnKeyAsync(ctx, verb, other, intent, thought);
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
                if (ask is { Kind: AskKind.Key, Yes: true, PlaceId: { } keyed }) Remember(ctx, other, $"{ctx.Self.Name} gave {name} a key to {ask.About}.", keyed);
                return new ActOutcome(Action, ask.Yes == true ? RelationRules.Done(ask.Kind, name, ask.About) : $"has a quiet word with {name}", null, other);
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
