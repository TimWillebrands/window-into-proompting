using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Economy;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

public partial class PlacesTest
{
    private static Reserves Felt(double fed = 1, double rested = 1, bool napped = false) => new(fed, rested, 0, 0, false, true, napped);

    private LifeContext LifeOf(Guid partyId, Guid who, string name, Reserves reserves, params (Guid Id, string Name)[] others) => new(
        partyId, new CastMember { Id = who, Name = name },
        others.Select(o => new CastMember { Id = o.Id, Name = o.Name }).Append(new CastMember { Id = who, Name = name }).ToList(),
        new HashSet<Guid>(), new List<ChatGroupInfo>(), new List<WorldActivity>(), _fx.Time.GetUtcNow(),
        new Dictionary<Guid, AttentionLook>(), Reserves: reserves);

    private IPersonaSelfGrain SelfOf(Guid partyId, Guid who) => _fx.GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(partyId, who));

    private async Task<Reserves> WakeHungryAsync(Guid partyId, Guid who)
    {
        await SelfOf(partyId, who).LiveBodyAsync(false);
        await SelfOf(partyId, who).RiseAsync(8);
        return await SelfOf(partyId, who).LiveBodyAsync(false);
    }

    [Fact]
    public async Task Hungry_AtHome_TheyCook_AndTheKitchenShowsIt()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        await places.GetSnapshotAsync();
        var hungry = await WakeHungryAsync(partyId, denise);
        Assert.Equal(Hunger.Hungry, hungry.Hunger);

        var ctx = LifeOf(partyId, denise, "Denise", hungry);
        var cook = Assert.Single((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Meets == Bio.Food);
        Assert.Equal(("Make yourself lunch in the kitchen (it's been a good while since you ate)", 2, "eat:cook"), (cook.Label, cook.Priority, cook.Verb));

        var done = await app.ActAsync(ctx, cook, "eggs on toast", null, default);

        Assert.Equal(("eat", "makes lunch at home"), (done!.Action, done.Summary));
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(("kitchen", "making lunch"), (body.SpotId, body.Activity));
        var fed = await SelfOf(partyId, denise).LiveBodyAsync(false);
        Assert.Equal(Hunger.None, fed.Hunger);
        Assert.True(fed.AteAt > 0);
        Assert.DoesNotContain((await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", fed), default))!.Affordances, a => a.Meets == Bio.Food);
    }

    [Fact]
    public async Task Hungry_WhereNothingFeeds_ThePlacesThatDoStandOut()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var lobby = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Lobby");
        await places.MoveAsync(denise, lobby.Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        var fine = (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt()), default))!.Affordances;
        var hungry = (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(fed: 0.25)), default))!.Affordances;

        Assert.DoesNotContain(fine, a => a.Meets is not null);
        Assert.DoesNotContain(hungry, a => a.Verb!.StartsWith("eat:"));
        var cafe = Assert.Single(hungry, a => a.Label.StartsWith("Go to the Café"));
        Assert.Equal(("Go to the Café (to get something to eat)", Bio.Food), (cafe.Label, cafe.Meets));
        Assert.Equal(fine.Single(a => a.Label.StartsWith("Go to the Café")).Priority + 2, cafe.Priority);
        Assert.Equal("Head home (to make something to eat)", hungry.Single(a => a.Label.StartsWith("Head home")).Label);
        Assert.Equal("Head home", fine.Single(a => a.Label.StartsWith("Head home")).Label);
    }

    [Fact]
    public async Task Ailing_AwayFromHome_HomePulls_AndSpentSaysOnlyThat()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var lobby = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Lobby");
        await places.MoveAsync(denise, lobby.Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        Affordance Home(IReadOnlyList<Affordance> seen) => seen.Single(a => a.Label.StartsWith("Head home"));
        var fine = Home((await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(rested: 0.38)), default))!.Affordances);
        var ill = Home((await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(rested: 0.38) with { Ailing = true }), default))!.Affordances);
        var spent = Home((await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(rested: 0.1) with { Ailing = true }), default))!.Affordances);

        Assert.Equal(("Head home", null), (fine.Label, fine.Meets));
        Assert.Equal(("Head home (you're not well)", Bio.Rest, fine.Priority + 2), (ill.Label, ill.Meets, ill.Priority));
        Assert.Equal("Head home (you're dead on your feet)", spent.Label);
    }

    [Fact]
    public async Task AilAsync_OnABodyNobodyLivedIn_DoesNothing_AndOnALivedOneItShows()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();

        Assert.Null(await SelfOf(partyId, pieter).AilAsync());
        var never = await SelfOf(partyId, pieter).GetStateAsync();
        Assert.Equal((0L, 0L), (never.AilingSince, never.LivedAt));
        Assert.False(await SelfOf(partyId, pieter).BreakingAsync());

        await SelfOf(partyId, denise).LiveBodyAsync(false);
        Assert.NotNull(await SelfOf(partyId, denise).AilAsync());
        Assert.Null(await SelfOf(partyId, denise).AilAsync());
        var body = await SelfOf(partyId, denise).LiveBodyAsync(false);
        Assert.True(body.Ailing);
        Assert.True(body.Rested <= Bio.AilingRested);
        Assert.Contains(Bio.Lines(body, DateTimeOffset.Now), l => l.StartsWith("You're not well today"));
    }

    [Fact]
    public async Task ATiredBody_IsNoLongerRestless()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var lobby = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Lobby");
        await places.MoveAsync(denise, lobby.Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await places.GetSituationAsync(denise);
        _fx.Time.Advance(TimeSpan.FromHours(2));

        int Centre(IReadOnlyList<Affordance> seen) => seen.Single(a => a.Label.StartsWith("Go to the city centre")).Priority;
        var fresh = (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt()), default))!.Affordances;
        var flagging = (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(rested: 0.3)), default))!.Affordances;

        Assert.Equal(1, Centre(fresh) - Centre(flagging));
    }

    [Fact]
    public async Task Hungry_WhereFoodIsSold_TheyOrder_Pay_AndTheWalkThereCost()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        var hungry = await WakeHungryAsync(partyId, denise);
        var wallet = _fx.GrainFactory.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, denise));
        var before = (await wallet.GetAsync()).Balance;

        var walk = await app.ActAsync(LifeOf(partyId, denise, "Denise", hungry), new Affordance("places", "Go to the Café", 0, cafe.Id, "go"), "go", null, default);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal("walk", walk!.Action);
        Assert.True((await SelfOf(partyId, denise).LiveBodyAsync(false)).Rested < hungry.Rested);

        var ctx = LifeOf(partyId, denise, "Denise", hungry);
        var order = Assert.Single((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Meets == Bio.Food);
        Assert.Equal(("Order lunch at the counter (6 credits; it's been a good while since you ate)", 2), (order.Label, order.Priority));

        var done = await app.ActAsync(ctx, order, "a cheese sandwich", null, default);

        Assert.Equal("has lunch at the Café (6 credits)", done!.Summary);
        Assert.Equal(before - Sustenance.LightMealCosts, (await wallet.GetAsync()).Balance);
        Assert.Equal("having lunch", (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise).Activity);
        Assert.Equal(Hunger.None, (await SelfOf(partyId, denise).LiveBodyAsync(false)).Hunger);
    }

    [Fact]
    public async Task CookingAtHome_FeedsWhoeverIsOver()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        await places.LetInAsync(denise, pieter);
        await places.MoveAsync(pieter, home.Id, null);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var hungry = await WakeHungryAsync(partyId, denise);
        await WakeHungryAsync(partyId, pieter);

        var ctx = LifeOf(partyId, denise, "Denise", hungry, (pieter, "Pieter"));
        var cook = Assert.Single((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Meets == Bio.Food);
        Assert.Equal("Make lunch for you and Pieter, in the kitchen (it's been a good while since you ate)", cook.Label);

        var done = await app.ActAsync(ctx, cook, "soup", null, default);

        Assert.Equal(("makes lunch at home, for Pieter too", pieter), (done!.Summary, done.WithPersonaId));
        Assert.Equal(Hunger.None, (await SelfOf(partyId, pieter).LiveBodyAsync(false)).Hunger);
        Assert.Equal("eating", (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == pieter).Activity);
        var theirs = await _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId).GetActivitiesAsync(5, pieter);
        Assert.Contains(theirs, a => a.Action == "eat" && a.Summary == "eats what Denise made at Denise's place");
    }

    [Fact]
    public async Task Spent_AtHome_ALieDownIsOffered_AndOnlyOnce()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        await places.GetSnapshotAsync();
        await SelfOf(partyId, denise).LiveBodyAsync(false);

        var ctx = LifeOf(partyId, denise, "Denise", Felt(rested: 0.1));
        var nap = Assert.Single((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Verb == "nap");
        Assert.Equal(("Lie down for a bit on the sofa", Bio.Seated, Bio.Rest), (nap.Label, nap.Priority, nap.Meets));

        var done = await app.ActAsync(ctx, nap, "lie down", null, default);
        _fx.Time.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(("nap", "lies down for a bit on the sofa"), (done!.Action, done.Summary));
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(("sofa", Poses.Lie, "having a lie-down"), (body.SpotId, body.Pose, body.Activity));
        Assert.True((await SelfOf(partyId, denise).LiveBodyAsync(false)).Napped);
        Assert.DoesNotContain((await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(rested: 0.1, napped: true)), default))!.Affordances, a => a.Verb == "nap");
        Assert.Null(await app.ActAsync(ctx, nap, "again", null, default));
    }

    [Fact]
    public async Task Flagging_TheCoffeeStandsOut_AndHoldsItOff()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        await places.GetSnapshotAsync();
        await SelfOf(partyId, denise).LiveBodyAsync(false);

        var fresh = (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt()), default))!.Affordances;
        var ctx = LifeOf(partyId, denise, "Denise", Felt(rested: 0.3));
        var coffee = Assert.Single((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Verb == "use:coffee");

        Assert.DoesNotContain(fresh, a => a.Meets == Bio.Rest);
        Assert.Equal(("Make yourself a coffee at the coffee maker", 1, Bio.Rest), (coffee.Label, coffee.Priority, coffee.Meets));

        await app.ActAsync(ctx, coffee, "coffee", null, default);

        Assert.True((await SelfOf(partyId, denise).LiveBodyAsync(false)).Perked);
    }

    [Fact]
    public async Task Hungry_AtTheHerringCart_TheFeatureIsTheOneOffer_AndItFeeds()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var centre = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the city centre");
        await places.MoveAsync(denise, centre.Id, "cart");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var hungry = await WakeHungryAsync(partyId, denise);

        var ctx = LifeOf(partyId, denise, "Denise", hungry);
        var seen = (await app.PerceiveAsync(ctx, default))!.Affordances;
        var herring = Assert.Single(seen, a => a.Verb == "do:herring");
        Assert.Equal(("Get a herring at the cart (it's been a good while since you ate)", Bio.Pull(Hunger.Hungry), Bio.Food), (herring.Label, herring.Priority, herring.Meets));
        Assert.DoesNotContain(seen, a => a.Verb!.StartsWith("eat:"));

        var done = await app.ActAsync(ctx, herring, "a herring", null, default);

        Assert.Equal("do", done!.Action);
        var fed = await SelfOf(partyId, denise).LiveBodyAsync(false);
        Assert.True(fed.Fed > hungry.Fed + 0.3);
        await places.DoFeatureAsync(denise, centre.Id, "herring", "onions, by the tail, and a nod from the herring man");
        var again = (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(fed: 0.25)), default))!.Affordances;
        Assert.DoesNotContain(again, a => a.Verb == "do:herring");
        var plain = Assert.Single(again, a => a.Meets == Bio.Food);
        Assert.Equal(("eat:thing:herring", "Get something to eat at the herring cart (it's been a good while since you ate)"), (plain.Verb, plain.Label));
    }

    private async Task<HashSet<Guid>> ADayAndANightAsync(IPlacesGrain places)
    {
        var slept = new HashSet<Guid>();
        for (var step = 0; step < 24 * 6; step++)
        {
            _fx.Time.Advance(TimeSpan.FromMinutes(10));
            slept.UnionWith(await places.GetAsleepAsync());
        }
        return slept;
    }

    private async Task<SelfState> RisenAsync(Guid partyId, Guid who, long upBefore)
    {
        var state = await SelfOf(partyId, who).GetStateAsync();
        for (var i = 0; i < 50 && state.UpSince == upBefore; i++)
        {
            await Task.Delay(100);
            state = await SelfOf(partyId, who).GetStateAsync();
        }
        return state;
    }

    [Fact]
    public async Task AfterANight_ALivedBodyRisesWantingBreakfast_AndOneNobodyLivesInStaysUnfelt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        await places.GetSnapshotAsync();
        var lived = await SelfOf(partyId, denise).LiveBodyAsync(false);

        var slept = await ADayAndANightAsync(places);

        Assert.Contains(denise, slept);
        Assert.Contains(pieter, slept);
        var risen = await RisenAsync(partyId, denise, lived.UpSince);
        Assert.True(risen.UpSince > lived.UpSince);
        Assert.InRange(risen.Fed, Bio.WakesAtLeast, Bio.WakesAtMost);
        var nobody = await SelfOf(partyId, pieter).GetStateAsync();
        Assert.Equal((0L, 0L, 1.0), (nobody.LivedAt, nobody.UpSince, nobody.Fed));
        Assert.False(Bio.Now(nobody, DateTimeOffset.Now).Felt);
    }

    [Fact]
    public async Task WokenAndUpBeforeMorning_TheyStillRiseWhenItComes()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        var lived = await SelfOf(partyId, denise).LiveBodyAsync(false);
        var (bed, wake) = Sleep.Window(denise);
        _fx.Time.Advance(UntilLocal(bed + 5));
        await places.GetAsleepAsync();
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Contains(denise, await places.GetAsleepAsync());

        await places.MoveAsync(denise, cafe.Id, null);
        _fx.Time.Advance(UntilLocal(wake + 5));
        Assert.DoesNotContain(denise, await places.GetAsleepAsync());

        var risen = await RisenAsync(partyId, denise, lived.UpSince);
        Assert.True(risen.UpSince > lived.UpSince);
        Assert.InRange(risen.Fed, Bio.WakesAtLeast, Bio.WakesAtMost);
        Assert.Null((await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise).AsleepSince);
    }
}
