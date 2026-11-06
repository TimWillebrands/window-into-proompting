using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Relations;
using PartyTown.World.Work;

namespace BackendTest;

/// <summary>Built Homes (ADR 0035) and Studios: a Home from a description, and a job that leaves Pieces behind.</summary>
public partial class PlacesTest
{
    private const string Herenhuis = "een statig herenhuis";

    private static JsonObject HomeDesign(string spots = """
        {"id": "sofa", "name": "the chesterfield", "at": "on", "capacity": 3, "seat": true},
        {"id": "desk", "name": "the study desk", "capacity": 1, "seat": true},
        {"id": "kitchen", "name": "the kitchen", "at": "in", "capacity": 3},
        {"id": "bed", "name": "the four-poster bed", "capacity": 1}
        """) => (JsonObject)JsonNode.Parse("""
        {"name": "the townhouse", "description": "tall rooms and a marble hall", "spots": [SPOTS],
         "scene": {"map": ["#ww######", "#.......#", "#.......#", "D.......#", "#.......#", "#.......#", "#########"],
                   "spots": {"sofa": {"at": [2, 2]}, "desk": {"at": [6, 2]}, "kitchen": {"at": [2, 4]}, "bed": {"at": [6, 4]}}}}
        """.Replace("SPOTS", spots))!;

    private async Task<T> WithDesign<T>(Func<JsonObject?> design, Func<Task<T>> act)
    {
        ScriptedLlm.Instance.Json = (system, _) => system.Contains("You design one new place") ? design() : null;
        try { return await act(); }
        finally { ScriptedLlm.Instance.Json = (_, _) => null; }
    }

    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Relation Couple)> BuiltHomeAsync()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        await Relations(partyId).SetHomeAsync(couple.Id, denise);
        await BodyOf(partyId, pieter);
        Assert.True(await WithDesign(() => HomeDesign(), () => Relations(partyId).NewHomeAsync(couple.Id, denise, Herenhuis)));
        return (partyId, denise, pieter, couple);
    }

    private async Task<Place> Built(Guid partyId) => (await _fx.Places(partyId).GetSnapshotAsync()).Places.Single(p => p.Name == Herenhuis);

    [Fact]
    public async Task ABuiltHome_ReplacesTheHomesTheyHad()
    {
        var (partyId, denise, pieter, _) = await BuiltHomeAsync();

        var snapshot = await _fx.Places(partyId).GetSnapshotAsync();
        var home = await Built(partyId);
        Assert.Single(snapshot.Places, p => p.Kind == PlaceKinds.Home);
        Assert.Equal(denise, home.OwnerId);
        Assert.Equal(new[] { pieter }, home.Housemates);
        Assert.All(snapshot.Bodies, b => Assert.Equal(home.Id, b.PlaceId));
        Assert.StartsWith("You're at home", await _fx.Places(partyId).GetSituationAsync(pieter));
        Assert.True((await Relations(partyId).GetAsync()).Relations.Single().LiveTogether);
    }

    [Fact]
    public async Task ABuiltHome_SeatsBothInOneBed_AndTheyGoToSleepThere()
    {
        var (partyId, denise, pieter, _) = await BuiltHomeAsync();
        var places = _fx.Places(partyId);
        Assert.Equal(2, (await Built(partyId)).Spots.Single(s => s.Id == "bed").Capacity);

        _fx.Time.Advance(UntilLocal(120));
        await places.GetAsleepAsync();
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var asleep = await places.GetAsleepAsync();

        Assert.Contains(denise, asleep);
        Assert.Contains(pieter, asleep);
    }

    [Fact]
    public async Task ABuiltHome_ComesWithItsFurni() =>
        Assert.Contains((await Built((await BuiltHomeAsync()).PartyId)).Furni, f => f.Kind == FurniKinds.Bed);

    [Fact]
    public async Task ADesign_MissingABed_IsSentBack()
    {
        var (partyId, denise, _, couple) = await CoupleAsync();
        var tries = 0;

        var built = await WithDesign(
            () => ++tries == 1
                ? HomeDesign("""{"id": "sofa", "name": "the sofa", "at": "on"}, {"id": "desk", "name": "the desk"}""")
                : HomeDesign(),
            () => Relations(partyId).NewHomeAsync(couple.Id, denise, Herenhuis));

        Assert.True(built);
        Assert.Equal(2, tries);
        Assert.Contains("spots must include: kitchen, bed", ScriptedLlm.Instance.Users.Last(u => u.StartsWith("Design:")));
    }

    [Fact]
    public async Task AFailedDesign_ChangesNothing()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();

        Assert.False(await WithDesign<bool>(() => null, () => Relations(partyId).NewHomeAsync(couple.Id, denise, Herenhuis)));

        var homes = (await _fx.Places(partyId).GetSnapshotAsync()).Places.Where(p => p.Kind == PlaceKinds.Home).ToList();
        Assert.Equal(new[] { denise, pieter }.Order(), homes.Select(h => h.OwnerId!.Value).Order());
        Assert.False((await Relations(partyId).GetAsync()).Relations.Single().LiveTogether);
    }

    [Fact]
    public async Task ABuild_RefusesToEvictSomeoneElse()
    {
        var (partyId, denise, pieter, couple) = await CoupleAsync();
        var hana = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(hana, "Hana", "You are Hana.", "A baker."));
        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant> { new() { Id = hana, Name = "Hana", Driver = DriverKind.LLM } });
        Assert.True(await _fx.Places(partyId).MoveInAsync(hana, denise));

        Assert.False(await WithDesign(() => HomeDesign(), () => Relations(partyId).NewHomeAsync(couple.Id, denise, Herenhuis)));

        Assert.Contains(hana, (await HomeOwnedBy(partyId, denise)).Housemates);
    }

    [Fact]
    public async Task ABuild_NeedsTheHostToBeOneOfThem()
    {
        var (partyId, _, _, couple) = await CoupleAsync();
        Assert.False(await WithDesign(() => HomeDesign(), () => Relations(partyId).NewHomeAsync(couple.Id, Guid.NewGuid(), Herenhuis)));
    }

    [Fact]
    public async Task AfterASplit_TheBuiltHomeKeepsItsName_AndTheOneLeavingGetsAPlainHome()
    {
        var (partyId, denise, pieter, _) = await BuiltHomeAsync();

        Assert.True(await Relations(partyId).EndAsync(denise, pieter));

        var home = await Built(partyId);
        var own = await HomeOwnedBy(partyId, pieter);
        Assert.Empty(home.Housemates);
        Assert.Equal("Pieter's place", own.Name);
        Assert.Equal(own.Id, (await BodyOf(partyId, pieter)).PlaceId);
    }

    [Fact]
    public async Task WhenTheBuiltHomesOwnerLeaves_TheHousemateStillHasSomewhereToLive()
    {
        var (partyId, denise, pieter, _) = await BuiltHomeAsync();
        var places = _fx.Places(partyId);

        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).RemoveParticipantAsync(denise);
        await places.RemoveParticipantAsync(denise);

        var snapshot = await places.GetSnapshotAsync();
        var own = snapshot.Places.Single(p => p.Kind == PlaceKinds.Home);
        Assert.Equal(pieter, own.OwnerId);
        Assert.Equal(own.Id, snapshot.Bodies.Single().PlaceId);
    }

    [Fact]
    public async Task ABuiltHome_CantBeTakenDownLikeAMadePlace()
    {
        var (partyId, _, _, _) = await BuiltHomeAsync();
        Assert.False(await _fx.Places(partyId).RemovePlaceAsync((await Built(partyId)).Id));
    }

    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Place Studio)> StudioAsync()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var locale = await places.LocaleAsync("a shared studio in the city centre", denise);
        await places.ClaimAsync(locale, denise, "Denise's studio");
        var studio = (await places.OpenAsync(locale, null, "author"))!;
        await places.MoveAsync(denise, studio.Id, "easel");
        await BodyOf(partyId, denise);
        return (partyId, denise, pieter, studio);
    }

    [Fact]
    public async Task AStudioLocale_OpensByItsName_WithAnEasel_AndIsWhoeverClaimedIt()
    {
        var (_, denise, _, studio) = await StudioAsync();

        Assert.Equal(Art.Studio, studio.Archetype);
        Assert.Equal("Denise's studio", studio.Name);
        Assert.Equal(denise, studio.MadeBy);
        Assert.Contains(studio.Furni, f => f.Kind == FurniKinds.Easel);
        Assert.Equal(PlaceRole.Owner, Roles.Of(studio, denise, null, new Dictionary<Guid, int>()));
    }

    [Fact]
    public async Task AHomeCantBeClaimed()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        Assert.Null(await _fx.Places(partyId).ClaimAsync((await HomeOwnedBy(partyId, denise)).Id, pieter, "mine now"));
    }

    [Fact]
    public async Task APiece_HangsWithTheFinishedWork_InTheMakersWords()
    {
        var (partyId, denise, _, studio) = await StudioAsync();
        var places = _fx.Places(partyId);

        var done = await places.MakeArtAsync(denise, "I'll paint a charcoal study of the canal. It's for Lena.");

        Assert.Equal("finishes a charcoal study of the canal at Denise's studio", done);
        var snapshot = await places.GetSnapshotAsync();
        var piece = snapshot.Places.Single(p => p.Id == studio.Id).Furni.Single(f => f.Kind == FurniKinds.Decor);
        Assert.Equal((Art.Wall, denise), (piece.SpotId, piece.OwnerId!.Value));
        var body = snapshot.Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(("easel", "at the easel"), (body.SpotId, body.Activity));
    }

    [Fact]
    public async Task Pieces_AreCappedPerDay()
    {
        var (partyId, denise, _, _) = await StudioAsync();
        var places = _fx.Places(partyId);

        Assert.NotNull(await places.MakeArtAsync(denise, "a study"));
        _fx.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(await places.MakeArtAsync(denise, "another study"));
        _fx.Time.Advance(TimeSpan.FromSeconds(1));

        Assert.Null(await places.MakeArtAsync(denise, "one too many"));
    }

    [Fact]
    public async Task NoEasel_NoPiece()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        Assert.Null(await _fx.Places(partyId).MakeArtAsync(denise, "a study"));
    }

    [Fact]
    public async Task VisitorsSeeTheWorkOnShow()
    {
        var (partyId, denise, pieter, studio) = await StudioAsync();
        var places = _fx.Places(partyId);
        await places.MakeArtAsync(denise, "a charcoal study of the canal");

        await places.MoveAsync(pieter, studio.Id, null);
        await BodyOf(partyId, pieter);

        Assert.Contains("On display: a charcoal study of the canal", await places.GetSituationAsync(pieter));
    }

    private async Task<(WorkApp App, LifeContext Life)> OnShiftAtStudioAsync(Guid partyId, Guid who, Guid studioId, params (Guid Id, string Name)[] cast)
    {
        var job = _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who));
        await job.SetAsync(new Occupation { Title = "artist", Workplace = "the studio", PlaceId = studioId, Days = 127, Start = 0, End = 1439 });
        await job.StepAsync(_fx.Time.GetLocalNow(), asleep: false);
        return (new WorkApp(_fx.GrainFactory, new Workplaces(_fx.GrainFactory, NullLogger<Workplaces>.Instance)), Life(partyId, who, cast));
    }

    [Fact]
    public async Task AnArtistOnShift_CanFinishAPiece_UntilTheDaysDone()
    {
        var (partyId, denise, pieter, studio) = await StudioAsync();
        var (app, life) = await OnShiftAtStudioAsync(partyId, denise, studio.Id, (denise, "Denise"), (pieter, "Pieter"));

        var offer = (await app.PerceiveAsync(life, default))!.Affordances.Single(a => a.Verb == "make");
        var outcome = await app.ActAsync(life, offer, "a charcoal study of the canal", null, default);

        Assert.Equal("finishes a charcoal study of the canal at Denise's studio", outcome!.Summary);

        _fx.Time.Advance(TimeSpan.FromSeconds(1));
        await _fx.Places(partyId).MakeArtAsync(denise, "another");
        Assert.DoesNotContain((await app.PerceiveAsync(Life(partyId, denise, (denise, "Denise"), (pieter, "Pieter")), default))!.Affordances, a => a.Verb == "make");
    }

    [Fact]
    public async Task OtherJobs_HaveNoEaselToFinishAnythingAt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var cafe = (await _fx.Places(partyId).GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await _fx.Places(partyId).MoveAsync(denise, cafe.Id, null);
        await BodyOf(partyId, denise);
        var (app, life) = await OnShiftAtStudioAsync(partyId, denise, cafe.Id, (denise, "Denise"), (pieter, "Pieter"));

        Assert.DoesNotContain((await app.PerceiveAsync(life, default))!.Affordances, a => a.Verb == "make");
    }

    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Place Studio)> ClosedStudioAsync()
    {
        var (partyId, denise, pieter, studio) = await StudioAsync();
        await _fx.Places(partyId).ClaimAsync(studio.Id, denise, null, closed: true);
        return (partyId, denise, pieter, studio);
    }

    [Fact]
    public async Task ClosingAPlace_SendsEveryoneElseHome_AndKeepsThemOut()
    {
        var (partyId, denise, pieter, studio) = await StudioAsync();
        var places = _fx.Places(partyId);
        await places.MoveAsync(pieter, studio.Id, null);
        await BodyOf(partyId, pieter);

        await places.ClaimAsync(studio.Id, denise, null, closed: true);

        Assert.Equal((await HomeOwnedBy(partyId, pieter)).Id, (await BodyOf(partyId, pieter)).PlaceId);
        Assert.Equal(studio.Id, (await BodyOf(partyId, denise)).PlaceId);
        Assert.Null(await places.MoveAsync(pieter, studio.Id, null));
    }

    [Fact]
    public async Task AnInvitation_FromAClosedPlace_IsToThatPlace_NotYourHome()
    {
        var (partyId, denise, pieter, studio) = await ClosedStudioAsync();
        var places = _fx.Places(partyId);

        Assert.True(await places.LetInAsync(denise, pieter));

        var snapshot = await places.GetSnapshotAsync();
        Assert.Contains(pieter, snapshot.Places.Single(p => p.Id == studio.Id).Guests);
        Assert.Empty((await HomeOwnedBy(partyId, denise)).Guests);
        Assert.NotNull(await places.MoveAsync(pieter, studio.Id, null));
    }

    [Fact]
    public async Task AKnock_ReachesTheOwnerOnlyWhileTheyreIn()
    {
        var (partyId, denise, pieter, studio) = await ClosedStudioAsync();
        var places = _fx.Places(partyId);

        Assert.True(await places.KnockAsync(pieter, studio.Id));
        Assert.Contains(pieter, await places.GetKnocksAsync(denise));

        await places.MoveAsync(denise, (await HomeOwnedBy(partyId, denise)).Id, null);
        await BodyOf(partyId, denise);
        Assert.False(await places.KnockAsync(pieter, studio.Id));
    }

    [Fact]
    public async Task GuestsAreForgotten_WhenTheOwnerLeaves()
    {
        var (partyId, denise, pieter, studio) = await ClosedStudioAsync();
        var places = _fx.Places(partyId);
        await places.LetInAsync(denise, pieter);

        await places.MoveAsync(denise, (await HomeOwnedBy(partyId, denise)).Id, null);

        Assert.Empty((await places.GetSnapshotAsync()).Places.Single(p => p.Id == studio.Id).Guests);
    }

    [Fact]
    public async Task AnOpenPlace_StillLetsAnyoneIn()
    {
        var (partyId, _, pieter, studio) = await StudioAsync();
        Assert.NotNull(await _fx.Places(partyId).MoveAsync(pieter, studio.Id, null));
    }

    [Fact]
    public async Task AClosedPlace_IsNotOfferedToStrangers_ButIsToWhoeverWasInvited()
    {
        var (partyId, denise, pieter, _) = await ClosedStudioAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        LifeContext Him() => Life(partyId, pieter, (denise, "Denise"), (pieter, "Pieter"));

        var stranger = (await app.PerceiveAsync(Him(), default))!;
        Assert.DoesNotContain(stranger.Affordances, a => a.Verb == "go" && a.Label.Contains("studio"));
        Assert.DoesNotContain("studio", stranger.Block);

        await places.LetInAsync(denise, pieter);

        Assert.Contains((await app.PerceiveAsync(Him(), default))!.Affordances, a => a.Label == "Go over to Denise's studio (they invited you)");
    }

    [Fact]
    public async Task TheOwner_CanInvitePeople_ButNotDecorateAsIfTheyLivedThere()
    {
        var (partyId, denise, pieter, _) = await ClosedStudioAsync();
        var seen = await new PlacesApp(_fx.GrainFactory).PerceiveAsync(Life(partyId, denise, (denise, "Denise"), (pieter, "Pieter")), default);

        Assert.Contains(seen!.Affordances, a => a.Verb == "invite" && a.Label == "Invite Pieter over to Denise's studio");
        Assert.DoesNotContain(seen.Affordances, a => a.Verb == "decorate");
    }

    [Fact]
    public async Task AGathering_CanBeCalledOff()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var calendar = _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId);
        var gathering = await calendar.PlanAsync(denise, "Denise", "Drinks", Guid.NewGuid(), "#kitchen", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());

        Assert.True(await calendar.CancelAsync(gathering.Id));

        Assert.Empty(await calendar.GetGatheringsAsync());
        Assert.False(await calendar.CancelAsync(gathering.Id));
    }

    [Theory]
    [InlineData("I'll paint a charcoal study of the canal. It's for Lena.", "a charcoal study of the canal")]
    [InlineData("I'm going to finally finish the red nude", "the red nude")]
    [InlineData("Let me work on the unfinished sculpture!", "the unfinished sculpture")]
    [InlineData("a small bronze of Lena", "a small bronze of Lena")]
    [InlineData("the charcoal-and-plaster study on the easel — the standing figure, half gone", "the charcoal-and-plaster study on the easel")]
    public void APiece_IsTitledByWhatItIs(string intent, string title) => Assert.Equal(title, Art.Title(intent));
}
