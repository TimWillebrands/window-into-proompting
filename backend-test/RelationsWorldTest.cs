using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Relations;

namespace BackendTest;

/// <summary>Relations in a real cluster (ADR 0035): sharing a Home, splitting it, and asking for a yes.</summary>
public partial class PlacesTest
{
    private IRelationsGrain Relations(Guid partyId) => _fx.GrainFactory.GetGrain<IRelationsGrain>(partyId);

    private async Task<Place> HomeOwnedBy(Guid partyId, Guid owner)
        => (await _fx.Places(partyId).GetSnapshotAsync()).Places.Single(p => p.Kind == PlaceKinds.Home && p.OwnerId == owner);

    private async Task<Body> BodyOf(Guid partyId, Guid who)
    {
        _fx.Time.Advance(TimeSpan.FromMinutes(1));
        return (await _fx.Places(partyId).GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == who);
    }

    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Relation Couple)> CoupleAsync()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        return (partyId, denise, pieter, (await Relations(partyId).DeclareAsync(denise, pieter, RelationKind.Married, null, false))!);
    }

    [Fact]
    public async Task MovingIn_SharesTheHostsHome_AndLeavesYourOwnEmpty()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();

        Assert.True(await Relations(partyId).SetHomeAsync(couple.Id, denise));

        var shared = await HomeOwnedBy(partyId, denise);
        var left = await HomeOwnedBy(partyId, pieter);
        Assert.Equal(new[] { pieter }, shared.Housemates);
        Assert.Equal("Denise and Pieter's place", shared.Name);
        Assert.True(left.Vacated);
        Assert.Equal(shared.Id, (await BodyOf(partyId, pieter)).PlaceId);
        Assert.StartsWith("You're at home", await _fx.Places(partyId).GetSituationAsync(pieter));
        Assert.True((await Relations(partyId).GetAsync()).Relations.Single().LiveTogether);
    }

    [Fact]
    public async Task Housemates_SleepInTheSharedBed()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var places = _fx.Places(partyId);
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await BodyOf(partyId, pieter);

        _fx.Time.Advance(UntilLocal(120));
        await places.GetAsleepAsync();
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var asleep = await places.GetAsleepAsync();

        Assert.Contains(denise, asleep);
        Assert.Contains(pieter, asleep);
        var snapshot = await places.GetSnapshotAsync();
        Assert.All(snapshot.Bodies, b => Assert.Equal(("bed", snapshot.Places.Single(p => p.OwnerId == denise).Id), (b.SpotId, b.PlaceId)));
    }

    [Fact]
    public async Task AHousemate_CanDecorate_AndAnswerTheDoor()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var places = _fx.Places(partyId);
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await BodyOf(partyId, pieter);
        var home = await HomeOwnedBy(partyId, denise);

        Assert.NotNull(await places.DecorateAsync(pieter, "a Lisp poster"));

        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(denise, cafe.Id, null);
        await BodyOf(partyId, denise);
        var hana = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(hana, "Hana", "You are Hana.", "A baker."));
        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant> { new() { Id = hana, Name = "Hana", Driver = DriverKind.LLM } });
        await places.GetSnapshotAsync();

        Assert.True(await places.KnockAsync(hana, home.Id));
        Assert.Contains(hana, await places.GetKnocksAsync(pieter));
    }

    [Fact]
    public async Task AHousemate_IsNeverLetIn_OrKnockingAtTheirOwnDoor()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        var home = await HomeOwnedBy(partyId, denise);

        Assert.False(await _fx.Places(partyId).KnockAsync(pieter, home.Id));
    }

    [Fact]
    public async Task WhenOneHousemateLeaves_GuestsStay()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var places = _fx.Places(partyId);
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await BodyOf(partyId, pieter);
        var guest = Guid.NewGuid();
        await places.LetInAsync(pieter, guest);

        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(denise, cafe.Id, null);
        Assert.Contains(guest, (await HomeOwnedBy(partyId, denise)).Guests);

        await places.MoveAsync(pieter, cafe.Id, null);
        Assert.Empty((await HomeOwnedBy(partyId, denise)).Guests);
    }

    [Fact]
    public async Task BreakingUp_SendsTheOneWhoMovedInBack()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await BodyOf(partyId, pieter);

        Assert.True(await Relations(partyId).EndAsync(pieter, denise));

        var kept = await HomeOwnedBy(partyId, denise);
        var own = await HomeOwnedBy(partyId, pieter);
        Assert.Empty(kept.Housemates);
        Assert.Equal("Denise's place", kept.Name);
        Assert.False(own.Vacated);
        Assert.Equal(own.Id, (await BodyOf(partyId, pieter)).PlaceId);
        var relation = (await Relations(partyId).GetAsync()).Relations.Single();
        Assert.False(relation.IsCurrent());
        Assert.False(relation.LiveTogether);
    }

    [Fact]
    public async Task SomeoneAwayAtWork_FindsTheirNewHomeOnTheWayBack()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var places = _fx.Places(partyId);
        await places.GoAwayAsync(pieter, await places.LocaleAsync("the office", pieter), "at work");
        await BodyOf(partyId, pieter);

        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        Assert.NotEqual((await HomeOwnedBy(partyId, denise)).Id, (await BodyOf(partyId, pieter)).PlaceId);

        await places.ReturnAsync(pieter);
        Assert.Equal((await HomeOwnedBy(partyId, denise)).Id, (await BodyOf(partyId, pieter)).PlaceId);
    }

    [Fact]
    public async Task WhenTheOwnerLeavesTheWorld_TheHousemateGetsTheirHomeBack()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var places = _fx.Places(partyId);
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await BodyOf(partyId, pieter);

        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).RemoveParticipantAsync(denise);
        await places.RemoveParticipantAsync(denise);
        await Relations(partyId).LeaveAsync(denise);

        var snapshot = await places.GetSnapshotAsync();
        var own = snapshot.Places.Single(p => p.Kind == PlaceKinds.Home && p.OwnerId == pieter);
        Assert.False(own.Vacated);
        Assert.Equal(own.Id, snapshot.Bodies.Single(b => b.ParticipantId == pieter).PlaceId);
        Assert.DoesNotContain(snapshot.Places, p => p.OwnerId == denise);
        Assert.False((await Relations(partyId).GetAsync()).Relations.Single().IsCurrent());
    }

    [Fact]
    public async Task WhenTheOwnerIsEvicted_TheHousemateWalksBackAndTheHomeGoes()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var places = _fx.Places(partyId);
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await BodyOf(partyId, pieter);

        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).RemoveParticipantAsync(denise);
        await Relations(partyId).LeaveAsync(denise);
        await places.GetSnapshotAsync();

        var own = await HomeOwnedBy(partyId, pieter);
        Assert.False(own.Vacated);
        Assert.NotNull(await places.MoveAsync(pieter, own.Id, null));
        Assert.Equal(own.Id, (await BodyOf(partyId, pieter)).PlaceId);
        Assert.DoesNotContain((await places.GetSnapshotAsync()).Places, p => p.OwnerId == denise);
    }

    [Fact]
    public async Task WhenAHousemateLeavesTheWorld_TheHomeIsNamedForWhoRemains()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var places = _fx.Places(partyId);
        await Relations(partyId).SetHomeAsync(couple.Id, denise);

        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).RemoveParticipantAsync(pieter);
        await places.GetSnapshotAsync();

        var home = await HomeOwnedBy(partyId, denise);
        Assert.Empty(home.Housemates);
        Assert.Equal("Denise's place", home.Name);
    }

    [Fact]
    public async Task AHomeCannotBeShared_WhileYoureSharingAnother()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        await Relations(partyId).SetHomeAsync(couple.Id, denise);

        Assert.False(await _fx.Places(partyId).MoveInAsync(denise, Guid.NewGuid()));
        Assert.False(await _fx.Places(partyId).MoveInAsync(pieter, Guid.NewGuid()));
    }

    [Fact]
    public async Task AYes_ToGoingOut_MakesThemACouple()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var relations = Relations(partyId);

        Assert.True(await relations.AskAsync(denise, pieter, AskKind.GoOut));
        Assert.False(await relations.AskAsync(denise, pieter, AskKind.Marry));
        Assert.Empty((await relations.GetAsync()).Relations);

        var ask = await relations.AnswerAsync(denise, pieter, yes: true);

        Assert.True(ask!.Yes);
        Assert.True((await relations.GetAsync()).Partners(denise, pieter));
    }

    [Fact]
    public async Task ANo_ChangesNothing_AndCantBeAnsweredTwice()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var relations = Relations(partyId);
        await relations.AskAsync(denise, pieter, AskKind.GoOut);

        Assert.False((await relations.AnswerAsync(denise, pieter, yes: false))!.Yes);

        Assert.Empty((await relations.GetAsync()).Relations);
        Assert.Null(await relations.AnswerAsync(denise, pieter, yes: true));
    }

    [Fact]
    public async Task OnlyTheOneAsked_CanAnswer()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        await Relations(partyId).AskAsync(denise, pieter, AskKind.GoOut);

        Assert.Null(await Relations(partyId).AnswerAsync(pieter, denise, yes: true));
    }

    [Fact]
    public async Task AYes_ToMovingIn_MovesTheOneAsked()
    {
        var (partyId, denise, pieter, _) = await CoupleAsync();
        await Relations(partyId).AskAsync(denise, pieter, AskKind.MoveIn);

        await Relations(partyId).AnswerAsync(denise, pieter, yes: true);

        Assert.Contains(pieter, (await HomeOwnedBy(partyId, denise)).Housemates);
        Assert.True((await Relations(partyId).GetAsync()).Relations.Single().LiveTogether);
    }

    [Fact]
    public async Task AYes_ThatNoLongerFits_TurnsIntoANo()
    {
        var (partyId, denise, pieter, _) = await CoupleAsync();
        var relations = Relations(partyId);
        await relations.AskAsync(denise, pieter, AskKind.Marry);

        var ask = await relations.AnswerAsync(denise, pieter, yes: true);

        Assert.False(ask!.Yes);
        Assert.Equal(RelationKind.Married, (await relations.GetAsync()).Relations.Single().Kind);
    }

    [Fact]
    public async Task APastRelation_LeavesTheCurrentOneAlone()
    {
        var (partyId, denise, pieter, _) = await CoupleAsync();
        var relations = Relations(partyId);

        await relations.DeclareAsync(denise, pieter, RelationKind.Together, "you dated in high school", past: true);

        var book = await relations.GetAsync();
        Assert.Equal(2, book.Relations.Count);
        Assert.Equal("you're married; you dated in high school", RelationRules.Standing(book, denise, pieter));
    }

    private LifeContext Life(Guid partyId, Guid self, params (Guid Id, string Name)[] cast) => new(
        partyId, new CastMember { Id = self, Name = cast.Single(c => c.Id == self).Name },
        cast.Select(c => new CastMember { Id = c.Id, Name = c.Name }).ToList(), new HashSet<Guid>(), new List<ChatGroupInfo>(),
        new List<WorldActivity>(), _fx.Time.GetUtcNow(), new Dictionary<Guid, PartyTown.World.Self.AttentionLook>());

    [Fact]
    public async Task AnAsk_ShowsUpAsYesOrNo_AndTheAnswerGoesBackToTheAsker()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var app = new RelationsApp(_fx.GrainFactory);
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };
        await Relations(partyId).AskAsync(denise, pieter, AskKind.GoOut);

        var seen = await app.PerceiveAsync(Life(partyId, pieter, cast), default);

        Assert.Contains("Denise asked you out", seen!.Block);
        Assert.Equal(new[] { "Say yes to Denise", "Say no to Denise" }, seen.Affordances.Select(a => a.Label));

        var outcome = await app.ActAsync(Life(partyId, pieter, cast), seen.Affordances[0], "yes!", null, default);

        Assert.Equal("is now with Denise", outcome!.Summary);
        Assert.True((await Relations(partyId).GetAsync()).Partners(denise, pieter));
        Assert.Contains("Pieter said yes", (await app.PerceiveAsync(Life(partyId, denise, cast), default))!.Block);
    }

    [Fact]
    public async Task ANo_StaysDiscreetInTheFeed()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var app = new RelationsApp(_fx.GrainFactory);
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };
        await Relations(partyId).AskAsync(denise, pieter, AskKind.GoOut);
        var seen = await app.PerceiveAsync(Life(partyId, pieter, cast), default);

        var outcome = await app.ActAsync(Life(partyId, pieter, cast), seen!.Affordances[1], "not now", null, default);

        Assert.Equal("has a quiet word with Denise", outcome!.Summary);
    }

    [Fact]
    public async Task ASourCouple_AtTheSameSpot_CanBreakUp()
    {
        var (partyId, denise, pieter, _) = await CoupleAsync();
        var places = _fx.Places(partyId);
        var app = new RelationsApp(_fx.GrainFactory);
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        var spot = cafe.Spots[0].Id;
        await places.MoveAsync(denise, cafe.Id, spot);
        await places.MoveAsync(pieter, cafe.Id, spot);
        await BodyOf(partyId, pieter);
        await _fx.GrainFactory.GetGrain<PartyTown.World.Self.IPersonaTiesGrain>(PartyTown.World.WorldKeys.Life(partyId, denise)).ShiftWarmthAsync(pieter, 0);

        var offer = (await app.PerceiveAsync(Life(partyId, denise, cast), default))!.Affordances.Single();
        Assert.Equal("Break up with Pieter", offer.Label);

        await places.MoveAsync(pieter, cafe.Id, cafe.Spots[1].Id);
        await BodyOf(partyId, pieter);
        var outcome = await app.ActAsync(Life(partyId, denise, cast), offer, "it's over", null, default);

        Assert.Equal("breaks up with Pieter", outcome!.Summary);
        Assert.False((await Relations(partyId).GetAsync()).Partners(denise, pieter));
    }

    [Fact]
    public async Task NoRelationsNoCompany_MeansNothingToPerceive()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var seen = await new RelationsApp(_fx.GrainFactory).PerceiveAsync(Life(partyId, denise, (denise, "Denise"), (pieter, "Pieter")), default);
        Assert.Null(seen);
    }

    [Fact]
    public async Task ThePrompt_SaysWhatTheyAre_AndKeepsThePastPrivate()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var hana = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(hana, "Hana", "You are Hana.", "A baker."));
        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant> { new() { Id = hana, Name = "Hana", Driver = DriverKind.LLM } });
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await Relations(partyId).DeclareAsync(denise, hana, RelationKind.Together, "you dated in high school", past: true);
        Task<string> Prompt(Guid who, params Guid[] present)
            => _fx.GrainFactory.GetGrain<PartyTown.World.Self.IPersonaTiesGrain>(WorldKeys.Life(partyId, who)).RenderAsync(present.ToList());

        Assert.Contains("- Pieter: you're married; you live together", await Prompt(denise, pieter));
        Assert.Contains("- Hana: you dated in high school", await Prompt(denise, hana));
        Assert.Contains("- Denise: you dated in high school; married to Pieter", await Prompt(hana, denise));
        Assert.DoesNotContain("high school", await Prompt(pieter, denise, hana));
    }

    [Fact]
    public async Task Forgetting_ALiveTogetherRelation_SplitsTheHome()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        await Relations(partyId).SetHomeAsync(couple.Id, denise);

        Assert.True(await Relations(partyId).ForgetAsync(couple.Id));

        Assert.Empty((await HomeOwnedBy(partyId, denise)).Housemates);
        Assert.False((await HomeOwnedBy(partyId, pieter)).Vacated);
        Assert.Empty((await Relations(partyId).GetAsync()).Relations);
    }
}
