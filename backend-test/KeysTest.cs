using System.Text.Json.Nodes;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Desires;
using PartyTown.World.Places;
using PartyTown.World.Relations;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Keys (ADR 0036): pure rules for who's close enough, and when it's turned sour.</summary>
public class KeyRulesTest
{
    private static readonly Guid Mara = Guid.NewGuid();
    private static readonly Guid Lena = Guid.NewGuid();
    private static readonly TieRead Fresh = new(0, 0);
    private static readonly TieRead Known = new(10, 0);
    private static readonly TieRead Strained = new(30, -0.3);
    private const long Now = 100_000_000_000;

    private static RelationBook Book(RelationKind? kind = null, long? ended = null) => new()
    {
        Relations = kind is { } k ? new List<Relation> { new() { AId = Mara, BId = Lena, Kind = k, EndedAt = ended } } : new List<Relation>(),
    };

    [Fact]
    public void ACurrentRelation_IsCloseEnough_EvenBeforeTheyveTalked() => Assert.True(KeyRules.Close(Book(RelationKind.Family), Mara, Lena, Fresh, Fresh));

    [Fact]
    public void KnowingEachOther_IsCloseEnough_ButOnlyBothWays()
    {
        Assert.True(KeyRules.Close(Book(), Mara, Lena, Known, Known));
        Assert.False(KeyRules.Close(Book(), Mara, Lena, Known, Fresh));
    }

    [Fact]
    public void Strain_RulesItOut_FromEitherSide()
    {
        Assert.False(KeyRules.Close(Book(RelationKind.Married), Mara, Lena, Strained, Known));
        Assert.False(KeyRules.Close(Book(RelationKind.Married), Mara, Lena, Known, Strained));
    }

    [Fact]
    public void Exes_AreSour_ButPastFriendsArent()
    {
        Assert.True(KeyRules.Sour(Book(RelationKind.Together, ended: 1), Mara, Lena, Known));
        Assert.False(KeyRules.Sour(Book(RelationKind.Friends, ended: 1), Mara, Lena, Known));
        Assert.False(KeyRules.Sour(Book(RelationKind.Together), Mara, Lena, Known));
    }

    [Fact]
    public void OneLiveWant_AtATime_AndNotRightAfterTheLast()
    {
        var place = Guid.NewGuid();
        Assert.True(KeyRules.MayWant(new[] { new Desire { Since = 0 } }, Now));
        Assert.False(KeyRules.MayWant(new[] { new Desire { PlaceId = place, Since = 0 } }, Now));
        var recent = new Desire { PlaceId = place, Status = DesireStatus.Faded, Since = Now - (long)TimeSpan.FromDays(1).TotalMilliseconds };
        Assert.False(KeyRules.MayWant(new[] { recent }, Now));
        recent.Since = Now - (long)TimeSpan.FromDays(4).TotalMilliseconds;
        Assert.True(KeyRules.MayWant(new[] { recent }, Now));
    }

    [Fact]
    public void Doorsteps_ForgetAfterAWeek_AndKeepOnlyTheLastFew()
    {
        var place = new Place();
        var day = (long)TimeSpan.FromDays(1).TotalMilliseconds;
        place.NoteDoorstep(Lena, Now - 8 * day);
        place.NoteDoorstep(Lena, Now - day);
        Assert.Equal(1, place.DoorstepsLately(Lena, Now));

        for (var i = 0; i < 20; i++) place.NoteDoorstep(Lena, Now);
        Assert.Equal(8, place.Doorsteps[Lena].Count);
    }

    [Fact]
    public void AKeyAsk_ReadsWithItsPlace()
    {
        var ask = new RelationAsk { Kind = AskKind.Key, About = "Mara's studio", Yes = true };
        Assert.Equal("Lena asked you for a key to Mara's studio, and you haven't answered yet.", RelationRules.Asked(ask, "Lena"));
        Assert.Equal("Mara gave you a key to Mara's studio.", RelationRules.Answered(ask, "Mara"));
        Assert.Equal("gives Lena a key to Mara's studio", RelationRules.Done(AskKind.Key, "Lena", ask.About));
    }
}

/// <summary>Keys in a real cluster (ADR 0036): the door gets felt, a want forms there, and it's acted on.</summary>
public partial class PlacesTest
{
    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Place Studio)> CoupleWithStudioAsync()
    {
        var (partyId, denise, pieter, studio) = await ClosedStudioAsync();
        await Relations(partyId).DeclareAsync(denise, pieter, RelationKind.Together, null, false);
        return (partyId, denise, pieter, studio);
    }

    private async Task<Place> Fresh(Guid partyId, Guid placeId) => (await _fx.Places(partyId).GetSnapshotAsync()).Places.Single(p => p.Id == placeId);

    private async Task BothAtTheCafeAsync(Guid partyId, Guid a, Guid b)
    {
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(a, cafe.Id, cafe.Spots[0].Id);
        await places.MoveAsync(b, cafe.Id, cafe.Spots[1].Id);
        await BodyOf(partyId, a);
    }

    /// <summary>Host out at the Café, two tries at the locked door: enough for a key to come up.</summary>
    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Place Studio)> DoorInTheWayAsync()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await BothAtTheCafeAsync(partyId, denise, pieter);
        var places = _fx.Places(partyId);
        await places.KnockAsync(pieter, studio.Id);
        await places.KnockAsync(pieter, studio.Id);
        return (partyId, denise, pieter, studio);
    }

    private static async Task Until(Func<Task<bool>> done)
    {
        for (var i = 0; i < 100 && !await done(); i++) await Task.Delay(20);
    }

    private IDesiresGrain Wants(Guid partyId, Guid who) => _fx.GrainFactory.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, who));

    private async Task<List<Desire>> DoorWants(Guid partyId, Guid who)
        => (await Wants(partyId, who).GetAsync()).Acquired.Where(d => d.PlaceId is not null).ToList();

    private async Task<T> Thinking<T>(Func<string, double> read, Func<Task<T>> act)
    {
        ScriptedJudge.Instance.Noul = read;
        ScriptedLlm.Instance.Json = (system, _) => system.Contains("\"want\"") ? new JsonObject { ["want"] = "not to stand outside that door again" } : null;
        try { return await act(); }
        finally
        {
            ScriptedJudge.Instance.Noul = null;
            ScriptedLlm.Instance.Json = (_, _) => null;
        }
    }

    private async Task<IReadOnlyList<Affordance>> Offers(Guid partyId, Guid who, params (Guid Id, string Name)[] cast)
        => (await new RelationsApp(_fx.GrainFactory).PerceiveAsync(Life(partyId, who, cast), default))?.Affordances ?? Array.Empty<Affordance>();

    [Fact]
    public async Task AKey_LetsYouIn_WhileTheOwnersOut()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        var places = _fx.Places(partyId);
        await places.MoveAsync(denise, (await HomeOwnedBy(partyId, denise)).Id, null);
        await BodyOf(partyId, denise);
        Assert.Null(await places.MoveAsync(pieter, studio.Id, null));

        Assert.True(await Relations(partyId).GiveKeyAsync(denise, pieter, studio.Id));

        Assert.NotNull(await places.MoveAsync(pieter, studio.Id, null));
    }

    [Fact]
    public async Task AKey_OutlastsTheOwnerLeaving_UnlikeAnInvitation()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await Relations(partyId).GiveKeyAsync(denise, pieter, studio.Id);

        await _fx.Places(partyId).MoveAsync(denise, (await HomeOwnedBy(partyId, denise)).Id, null);

        Assert.Contains(pieter, (await Fresh(partyId, studio.Id)).Keyholders);
    }

    [Fact]
    public async Task AKey_WorksOnAHomeToo()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var home = await HomeOwnedBy(partyId, denise);

        Assert.True(await Relations(partyId).GiveKeyAsync(denise, pieter, home.Id));

        Assert.NotNull(await _fx.Places(partyId).MoveAsync(pieter, home.Id, null));
    }

    [Fact]
    public async Task OnlyAHost_CanGiveAKey_AndOnlyToAClosedPlace()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        var cafe = (await _fx.Places(partyId).GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");

        Assert.False(await Relations(partyId).GiveKeyAsync(pieter, denise, studio.Id));
        Assert.False(await Relations(partyId).GiveKeyAsync(pieter, pieter, studio.Id));
        Assert.False(await Relations(partyId).GiveKeyAsync(denise, pieter, cafe.Id));
    }

    [Fact]
    public async Task ALockedDoor_CountsAsADoorstep()
    {
        var (partyId, _, pieter, studio) = await DoorInTheWayAsync();
        var place = await Fresh(partyId, studio.Id);
        Assert.Equal(2, place.DoorstepsLately(pieter, _fx.Time.GetUtcNow().ToUnixTimeMilliseconds()));
        Assert.Empty(place.Knocks);
    }

    [Fact]
    public async Task AKnockAnsweredIsOneDoorstep_NotTwo()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        var places = _fx.Places(partyId);

        await places.KnockAsync(pieter, studio.Id);
        await places.LetInAsync(denise, pieter);

        Assert.Equal(1, (await Fresh(partyId, studio.Id)).DoorstepsLately(pieter, _fx.Time.GetUtcNow().ToUnixTimeMilliseconds()));
    }

    [Fact]
    public async Task NeedingAnInvitation_CountsAsADoorstep()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();

        await _fx.Places(partyId).LetInAsync(denise, pieter);

        Assert.Equal(1, (await Fresh(partyId, studio.Id)).DoorstepsLately(pieter, _fx.Time.GetUtcNow().ToUnixTimeMilliseconds()));
    }

    [Fact]
    public async Task GoingToSomeoneBehindAClosedDoor_Knocks_RatherThanFizzling()
    {
        var (partyId, denise, pieter, _) = await CoupleWithStudioAsync();
        var places = _fx.Places(partyId);

        Assert.Null(await places.JoinAsync(pieter, denise));

        Assert.Contains(pieter, await places.GetKnocksAsync(denise));
    }

    [Fact]
    public async Task TheLockedDoor_IsFeltByPeopleClose_NotByStrangers()
    {
        var (partyId, denise, pieter, _) = await ClosedStudioAsync();
        var places = _fx.Places(partyId);
        await places.MoveAsync(denise, (await HomeOwnedBy(partyId, denise)).Id, null);
        await BodyOf(partyId, denise);
        var app = new PlacesApp(_fx.GrainFactory);
        LifeContext Him() => Life(partyId, pieter, (denise, "Denise"), (pieter, "Pieter"));

        Assert.DoesNotContain("locked", (await app.PerceiveAsync(Him(), default))!.Block);

        await Relations(partyId).DeclareAsync(denise, pieter, RelationKind.Friends, null, false);

        var seen = await app.PerceiveAsync(Him(), default);
        Assert.Contains("Denise's studio is locked; Denise is out.", seen!.Block);
        Assert.Contains(seen.Affordances, a => a is { Verb: "knock", Label: "Go by Denise's studio anyway (it's locked)" });
    }

    [Fact]
    public async Task GoingByALockedDoor_IsSomethingThatHappened()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await _fx.Places(partyId).MoveAsync(denise, (await HomeOwnedBy(partyId, denise)).Id, null);
        await BodyOf(partyId, denise);

        var outcome = await new PlacesApp(_fx.GrainFactory).ActAsync(Life(partyId, pieter, (denise, "Denise"), (pieter, "Pieter")),
            new Affordance("places", "Go by", 0, studio.Id, "knock"), "see if she's in", null, default);

        Assert.Equal("goes by Denise's studio and finds it locked", outcome!.Summary);
    }

    [Fact]
    public async Task Invitations_GoToAPartnerFirst()
    {
        var (partyId, denise, pieter, _) = await CoupleWithStudioAsync();
        var hana = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(hana, "Hana", "You are Hana.", "A baker."));
        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant> { new() { Id = hana, Name = "Hana", Driver = DriverKind.LLM } });
        await _fx.Places(partyId).GetSnapshotAsync();
        var app = new PlacesApp(_fx.GrainFactory);

        for (var i = 0; i < 12; i++)
        {
            var seen = await app.PerceiveAsync(Life(partyId, denise, (denise, "Denise"), (pieter, "Pieter"), (hana, "Hana")), default);
            Assert.Equal(pieter, seen!.Affordances.Single(a => a.Verb == "invite").TargetId);
        }
    }

    [Fact]
    public async Task NoKeyComesUp_UntilTheDoorsBeenInTheWay()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await BothAtTheCafeAsync(partyId, denise, pieter);
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };
        await _fx.Places(partyId).KnockAsync(pieter, studio.Id);

        Assert.DoesNotContain(await Offers(partyId, denise, cast), a => a.Label.Contains("key"));
        Assert.DoesNotContain(await Offers(partyId, pieter, cast), a => a.Label.Contains("key"));
    }

    [Fact]
    public async Task EitherOfThem_CanRaiseIt_OnceItHas()
    {
        var (partyId, denise, pieter, _) = await DoorInTheWayAsync();
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };

        Assert.Equal(0, (await Offers(partyId, denise, cast)).Single(a => a.Label == "Give Pieter a key to Denise's studio").Priority);
        Assert.Equal(0, (await Offers(partyId, pieter, cast)).Single(a => a.Label == "Ask Denise for a key to Denise's studio").Priority);
    }

    [Fact]
    public async Task AKey_NeverComesUpWithAStranger()
    {
        var (partyId, denise, pieter, studio) = await ClosedStudioAsync();
        await BothAtTheCafeAsync(partyId, denise, pieter);
        await _fx.Places(partyId).KnockAsync(pieter, studio.Id);
        await _fx.Places(partyId).KnockAsync(pieter, studio.Id);

        Assert.DoesNotContain(await Offers(partyId, denise, (denise, "Denise"), (pieter, "Pieter")), a => a.Label.Contains("key"));
    }

    [Fact]
    public async Task AWant_PullsHarder_ThanTheBareIdea()
    {
        var (partyId, denise, pieter, studio) = await DoorInTheWayAsync();
        await Wants(partyId, denise).AddAsync(new Desire { Kind = DesireKind.Being, Text = "let him in", PlaceId = studio.Id, TargetId = pieter, Since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });

        var offer = (await Offers(partyId, denise, (denise, "Denise"), (pieter, "Pieter"))).Single(a => a.Label.Contains("key"));

        Assert.Equal(KeyRules.Wanted, offer.Priority);
    }

    [Fact]
    public async Task GivingAKey_NeedsNoYes_AndSettlesWhatBothWanted()
    {
        var (partyId, denise, pieter, studio) = await DoorInTheWayAsync();
        var since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await Wants(partyId, denise).AddAsync(new Desire { Text = "let him in", PlaceId = studio.Id, TargetId = pieter, Since = since });
        await Wants(partyId, pieter).AddAsync(new Desire { Text = "stop knocking", PlaceId = studio.Id, TargetId = denise, Since = since });
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };
        var app = new RelationsApp(_fx.GrainFactory);
        var offer = (await Offers(partyId, denise, cast)).Single(a => a.Label.Contains("key"));

        var outcome = await app.ActAsync(Life(partyId, denise, cast), offer, "here, take this", null, default);

        Assert.Equal("gives Pieter a key to Denise's studio", outcome!.Summary);
        Assert.Contains(pieter, (await Fresh(partyId, studio.Id)).Keyholders);
        Assert.Equal(DesireStatus.Fulfilled, (await DoorWants(partyId, denise)).Single().Status);
        Assert.Equal(DesireStatus.Fulfilled, (await DoorWants(partyId, pieter)).Single().Status);
        Assert.DoesNotContain(await Offers(partyId, denise, cast), a => a.Verb!.StartsWith("givekey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskingForAKey_NeedsAYes_ThenItsYours()
    {
        var (partyId, denise, pieter, studio) = await DoorInTheWayAsync();
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };
        var app = new RelationsApp(_fx.GrainFactory);
        var ask = (await Offers(partyId, pieter, cast)).Single(a => a.Label.Contains("key"));

        Assert.Equal("has a quiet word with Denise", (await app.ActAsync(Life(partyId, pieter, cast), ask, "can I have a key?", null, default))!.Summary);
        Assert.Empty((await Fresh(partyId, studio.Id)).Keyholders);

        var seen = await app.PerceiveAsync(Life(partyId, denise, cast), default);
        Assert.Contains("Pieter asked you for a key to Denise's studio", seen!.Block);
        var outcome = await app.ActAsync(Life(partyId, denise, cast), seen.Affordances.Single(a => a.Label == "Say yes to Pieter"), "fine", null, default);

        Assert.Equal("gives Pieter a key to Denise's studio", outcome!.Summary);
        Assert.Contains(pieter, (await Fresh(partyId, studio.Id)).Keyholders);
    }

    [Fact]
    public async Task ANo_LeavesTheDoorShut_AndStopsAskingAgainForAWhile()
    {
        var (partyId, denise, pieter, studio) = await DoorInTheWayAsync();
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };
        await Relations(partyId).AskAsync(pieter, denise, AskKind.Key, studio.Id, studio.Name);

        await Relations(partyId).AnswerAsync(pieter, denise, yes: false);

        Assert.Empty((await Fresh(partyId, studio.Id)).Keyholders);
        Assert.DoesNotContain(await Offers(partyId, pieter, cast), a => a.Label.Contains("key"));
    }

    [Fact]
    public async Task AKey_SurvivesABreakUp_AndBothFeelPulledToDealWithIt()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await Relations(partyId).GiveKeyAsync(denise, pieter, studio.Id);
        var cast = new[] { (denise, "Denise"), (pieter, "Pieter") };

        await Relations(partyId).EndAsync(denise, pieter);

        Assert.Contains(pieter, (await Fresh(partyId, studio.Id)).Keyholders);
        Assert.Equal(KeyRules.Sore, (await Offers(partyId, pieter, cast)).Single(a => a.Verb!.StartsWith("returnkey", StringComparison.Ordinal)).Priority);
        Assert.Equal(KeyRules.Sore, (await Offers(partyId, denise, cast)).Single(a => a.Verb!.StartsWith("takekey", StringComparison.Ordinal)).Priority);
    }

    [Fact]
    public async Task AKey_CanBeGivenBack_OrTakenBack()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        var relations = Relations(partyId);

        await relations.GiveKeyAsync(denise, pieter, studio.Id);
        Assert.True(await relations.DropKeyAsync(pieter, denise, studio.Id, taken: false));
        Assert.Empty((await Fresh(partyId, studio.Id)).Keyholders);

        await relations.GiveKeyAsync(denise, pieter, studio.Id);
        Assert.True(await relations.DropKeyAsync(pieter, denise, studio.Id, taken: true));
        Assert.False(await relations.DropKeyAsync(pieter, denise, studio.Id, taken: true));
    }

    [Fact]
    public async Task OnlyTheTwoOfThem_AreToldAboutAKey()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        var hana = Guid.NewGuid();
        await Relations(partyId).GiveKeyAsync(denise, pieter, studio.Id);
        var places = _fx.Places(partyId);

        Assert.Equal("# Keys\n- You have a key to Denise's studio.\n", await places.GetKeyLinesAsync(pieter));
        Assert.Equal("# Keys\n- Pieter has a key to Denise's studio.\n", await places.GetKeyLinesAsync(denise));
        Assert.Equal(string.Empty, await places.GetKeyLinesAsync(hana));
    }

    [Fact]
    public async Task WithAKey_ThePlaceIsOffered_LikeAnyOther()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await Relations(partyId).GiveKeyAsync(denise, pieter, studio.Id);

        var seen = await new PlacesApp(_fx.GrainFactory).PerceiveAsync(Life(partyId, pieter, (denise, "Denise"), (pieter, "Pieter")), default);

        Assert.Contains(seen!.Affordances, a => a is { Verb: "go" } && a.TargetId == studio.Id && a.Label.EndsWith("(you have a key)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenAHolderLeavesTheWorld_TheirKeyGoes()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await Relations(partyId).GiveKeyAsync(denise, pieter, studio.Id);

        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).RemoveParticipantAsync(pieter);

        Assert.Empty((await Fresh(partyId, studio.Id)).Keyholders);
    }

    [Fact]
    public async Task AtTheDoor_ASecondRound_CanTurnIntoAWant_OnBothSides()
    {
        var (partyId, denise, pieter, studio) = await Thinking(_ => 0.9, async () =>
        {
            var world = await DoorInTheWayAsync();
            await Until(async () => (await DoorWants(world.PartyId, world.Denise)).Count > 0 && (await DoorWants(world.PartyId, world.Pieter)).Count > 0);
            return world;
        });

        var his = (await DoorWants(partyId, pieter)).Single();
        var hers = (await DoorWants(partyId, denise)).Single();
        Assert.Equal((DesireKind.Having, DesireSource.Play, denise, studio.Id), (his.Kind, his.Source, his.TargetId!.Value, his.PlaceId!.Value));
        Assert.Equal((DesireKind.Being, DesireSource.Play, pieter, studio.Id), (hers.Kind, hers.Source, hers.TargetId!.Value, hers.PlaceId!.Value));
        Assert.Equal("not to stand outside that door again", his.Text);
    }

    [Fact]
    public async Task ReluctanceIsAllowed_TheOwnerCanSimplyNotWantTo()
    {
        var (partyId, denise, pieter, _) = await Thinking(q => q.Contains("would rather hand") ? 0.3 : 0.9, async () =>
        {
            var world = await DoorInTheWayAsync();
            await Until(async () => (await DoorWants(world.PartyId, world.Pieter)).Count > 0);
            return world;
        });

        Assert.Single(await DoorWants(partyId, pieter));
        Assert.Empty(await DoorWants(partyId, denise));
    }

    [Fact]
    public async Task OneRoundAtTheDoor_IsntEnough()
    {
        var (partyId, denise, pieter, studio) = await CoupleWithStudioAsync();
        await BothAtTheCafeAsync(partyId, denise, pieter);

        await Thinking(_ => 0.9, async () =>
        {
            await _fx.Places(partyId).KnockAsync(pieter, studio.Id);
            await Task.Delay(100);
            return true;
        });

        Assert.Empty(await DoorWants(partyId, pieter));
    }

    [Fact]
    public async Task StrangersAtTheDoor_DontStartWanting()
    {
        var (partyId, denise, pieter, studio) = await ClosedStudioAsync();
        await BothAtTheCafeAsync(partyId, denise, pieter);

        await Thinking(_ => 0.9, async () =>
        {
            await _fx.Places(partyId).KnockAsync(pieter, studio.Id);
            await _fx.Places(partyId).KnockAsync(pieter, studio.Id);
            await Task.Delay(100);
            return true;
        });

        Assert.Empty(await DoorWants(partyId, pieter));
    }

    [Fact]
    public async Task AnotherRoundAtTheDoor_KeepsAWantAlive()
    {
        var (partyId, _, pieter, studio) = await DoorInTheWayAsync();
        var want = await Wants(partyId, pieter).AddAsync(new Desire { Text = "stop knocking", PlaceId = studio.Id, Since = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds() });

        await _fx.Places(partyId).KnockAsync(pieter, studio.Id);

        await Until(async () => (await DoorWants(partyId, pieter)).Single().TouchedAt > 0);
        Assert.True((await DoorWants(partyId, pieter)).Single(d => d.Id == want.Id).TouchedAt > 0);
    }
}
