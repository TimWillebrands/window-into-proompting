using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

public partial class PlacesTest
{
    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Place Lobby)> OnTheSofasAsync()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var lobby = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Lobby");
        await places.MoveAsync(denise, lobby.Id, "sofas");
        await places.MoveAsync(pieter, lobby.Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await places.GetSnapshotAsync();
        return (partyId, denise, pieter, lobby);
    }

    [Fact]
    public async Task ABearing_ShowsRightNextToThem_NotAcrossTheRoom_AndNotOnceItHasWornOff()
    {
        var (partyId, denise, pieter, lobby) = await OnTheSofasAsync();
        var places = _fx.Places(partyId);
        await places.SetBearingAsync(pieter, Bearing.Spent);

        Assert.Contains("Pieter (right next to you; looks dead on their feet)", await places.GetSituationAsync(denise));
        Assert.DoesNotContain("dead on their feet", await places.GetSituationAsync(pieter));

        await places.MoveAsync(denise, lobby.Id, "window");
        _fx.Time.Advance(TimeSpan.FromMinutes(1));
        var across = await places.GetSituationAsync(denise);
        Assert.Contains("Pieter (", across);
        Assert.DoesNotContain("dead on their feet", across);

        await places.MoveAsync(denise, lobby.Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.Contains("looks dead on their feet", await places.GetSituationAsync(denise));

        _fx.Time.Advance(Bio.FeltFor);
        Assert.DoesNotContain("dead on their feet", await places.GetSituationAsync(denise));
    }

    [Fact]
    public async Task ABearing_ComesAfterAnAura_WithTheSamePunctuation()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await _fx.GrainFactory.GetGrain<IPersonaGrain>(pieter).SetAura("smells of sawdust");
        await places.MoveAsync(denise, cafe.Id, null);
        await places.JoinAsync(pieter, denise);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await places.SetBearingAsync(pieter, Bearing.Unwell);

        Assert.Contains("Pieter (right next to you; up close: smells of sawdust; looks unwell)", await places.GetSituationAsync(denise));
    }

    [Fact]
    public async Task SomeoneNextToYouWhoLooksDeadOnTheirFeet_IsWhatYoudAskAbout()
    {
        var (partyId, denise, pieter, _) = await OnTheSofasAsync();
        var places = _fx.Places(partyId);
        await places.SetActivityAsync(pieter, "reading the paper", null);
        await places.SetBearingAsync(pieter, Bearing.Spent);

        var seen = (await new PlacesApp(_fx.GrainFactory).PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(), (pieter, "Pieter")), default))!;

        var talk = Assert.Single(seen.Affordances, a => a.Verb == "say");
        Assert.Equal("Ask Pieter if they're all right, right here (looks dead on their feet)", talk.Label);
    }

    [Fact]
    public async Task AHostWhoIsNotHungry_IsOfferedToCookForAFamishedGuest_AndDoingItFeedsThem()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        await places.LetInAsync(denise, pieter);
        await places.MoveAsync(pieter, home.Id, null);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await WakeHungryAsync(partyId, pieter);
        await places.SetBearingAsync(pieter, Bearing.Famished);

        var ctx = LifeOf(partyId, denise, "Denise", Felt(), (pieter, "Pieter"));
        var seen = (await app.PerceiveAsync(ctx, default))!.Affordances;
        var cook = Assert.Single(seen, a => a.Verb!.StartsWith("eat:"));
        Assert.Equal(("Make something to eat for Pieter (they look like they haven't eaten all day)", 2, "eat:cook", (string?)null),
            (cook.Label, cook.Priority, cook.Verb, cook.Meets));

        var done = await app.ActAsync(ctx, cook, "a big bowl of pasta", null, default);

        Assert.Equal(("eat", "makes lunch for Pieter at home", pieter), (done!.Action, done.Summary, done.WithPersonaId));
        Assert.Equal(Hunger.None, (await SelfOf(partyId, pieter).LiveBodyAsync(false, SelfMath.Alone)).Hunger);
        var fed = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == pieter);
        Assert.Equal(("eating", (string?)null), (fed.Activity, Bearing.Shown(fed, _fx.Time.GetUtcNow().ToUnixTimeMilliseconds())));
        Assert.DoesNotContain("haven't eaten all day", await places.GetSituationAsync(denise));
        Assert.DoesNotContain((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Verb!.StartsWith("eat:"));
    }

    [Fact]
    public async Task AFamishedEater_NoLongerLooksIt_OnceTheyEat_ButLookingUnwellStays()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        async Task<string?> ShownAsync() => Bearing.Shown((await places.GetBodyAsync(denise))!, _fx.Time.GetUtcNow().ToUnixTimeMilliseconds());

        await places.SetBearingAsync(denise, Bearing.Famished);
        Assert.True(await places.EatAsync(denise, home.Id, null, "making lunch"));
        Assert.Null(await ShownAsync());

        await places.SetBearingAsync(denise, Bearing.Unwell);
        Assert.True(await places.EatAsync(denise, home.Id, null, "making lunch"));
        Assert.Equal(Bearing.Unwell, await ShownAsync());
    }

    [Fact]
    public async Task ASleeper_ShowsNoBearing_AndIsNotAskedIfTheyreAllRight()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var theirs = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == pieter);
        _fx.Time.Advance(UntilLocal(20 * 60 + 30));
        Assert.True(await places.TurnInAsync(pieter));
        await places.LetInAsync(pieter, denise);
        await places.MoveAsync(denise, theirs.Id, "bed");
        _fx.Time.Advance(TimeSpan.FromMinutes(1));
        await places.GetAsleepAsync();
        _fx.Time.Advance(TimeSpan.FromMinutes(29));
        Assert.Contains(pieter, await places.GetAsleepAsync());
        Assert.DoesNotContain(denise, await places.GetAsleepAsync());
        await places.SetBearingAsync(pieter, Bearing.Unwell);

        var situation = await places.GetSituationAsync(denise);
        Assert.Contains("Pieter (right next to you", situation);
        Assert.DoesNotContain(Bearing.Unwell, situation);
        var seen = (await new PlacesApp(_fx.GrainFactory).PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(), (pieter, "Pieter")), default))!;
        Assert.DoesNotContain(seen.Affordances, a => a.Label.Contains("all right"));
    }

    [Fact]
    public async Task NoCookingIsOffered_ForAGuestWhoIsMerelyHungry_OrBySomeoneWhoDoesNotLiveThere()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        await places.LetInAsync(denise, pieter);
        await places.MoveAsync(pieter, home.Id, null);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        await places.SetBearingAsync(pieter, Bearing.Of(Felt(fed: 0.25)));
        var host = (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", Felt(), (pieter, "Pieter")), default))!.Affordances;
        Assert.DoesNotContain(host, a => a.Verb!.StartsWith("eat:"));

        await places.SetBearingAsync(denise, Bearing.Famished);
        var guest = (await app.PerceiveAsync(LifeOf(partyId, pieter, "Pieter", Felt(), (denise, "Denise")), default))!.Affordances;
        Assert.DoesNotContain(guest, a => a.Verb!.StartsWith("eat:"));
    }

    [Fact]
    public async Task SpentAtHalfPastEight_TheyTurnInEarly_SleepThroughTheNight_AndRiseInTheMorning()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        await places.GetSnapshotAsync();
        var lived = await SelfOf(partyId, denise).LiveBodyAsync(false, SelfMath.Alone);
        var (bed, wake) = Sleep.Window(denise);
        _fx.Time.Advance(UntilLocal(20 * 60 + 30));
        Assert.Empty(await places.GetAsleepAsync());

        await places.SetBearingAsync(denise, Bearing.Spent);
        var ctx = LifeOf(partyId, denise, "Denise", Felt(rested: 0.1));
        var early = Assert.Single((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Verb == "bed");
        Assert.Equal(("Turn in early", Bio.Seated, Bio.Rest), (early.Label, early.Priority, early.Meets));
        var done = await app.ActAsync(ctx, early, "go to bed", null, default);
        Assert.Equal(("bed", "turns in early"), (done!.Action, done.Summary));
        Assert.Null((await places.GetBodyAsync(denise))!.Bearing);

        _fx.Time.Advance(TimeSpan.FromMinutes(1));
        await places.GetAsleepAsync();
        _fx.Time.Advance(TimeSpan.FromMinutes(29));
        var atNine = await places.GetAsleepAsync();
        Assert.Contains(denise, atNine);
        Assert.DoesNotContain(pieter, atNine);

        _fx.Time.Advance(UntilLocal(bed + 5));
        Assert.Contains(denise, await places.GetAsleepAsync());
        _fx.Time.Advance(UntilLocal(3 * 60));
        Assert.Contains(denise, await places.GetAsleepAsync());
        var asleepSince = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise).AsleepSince!.Value;

        _fx.Time.Advance(UntilLocal(wake + 5));
        Assert.DoesNotContain(denise, await places.GetAsleepAsync());
        Assert.Null((await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise).TurnedInUntil);
        Assert.True(_fx.Time.GetUtcNow().ToUnixTimeMilliseconds() - asleepSince > TimeSpan.FromHours(Bio.FullNight).TotalMilliseconds);
        var risen = await RisenAsync(partyId, denise, lived.UpSince);
        Assert.True(risen.UpSince > lived.UpSince);
    }

    [Fact]
    public async Task TurningInEarly_IsOfferedOnlyLate_AndOnlyWhenSpentOrUnwell()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        await places.GetSnapshotAsync();

        async Task<Affordance?> BedAsync(Reserves r)
            => (await app.PerceiveAsync(LifeOf(partyId, denise, "Denise", r), default))!.Affordances.SingleOrDefault(a => a.Verb == "bed");

        Assert.Null(await BedAsync(Felt(rested: 0.1)));
        _fx.Time.Advance(UntilLocal(20 * 60 + 30));
        Assert.Null(await BedAsync(Felt(rested: 0.3)));
        Assert.Equal(3, (await BedAsync(Felt(rested: 0.3) with { Ailing = true }))!.Priority);
        Assert.Equal(Bio.Seated, (await BedAsync(Felt(rested: 0.1)))!.Priority);
    }

    [Fact]
    public async Task AHuman_CannotTurnIn()
    {
        var (partyId, _, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var human = Guid.NewGuid();
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var participants = (await party.GetParty()).Participants.ToList();
        participants.Add(new PartyParticipant { Id = human, Name = "Tim", Driver = DriverKind.User });
        await party.SetParticipants(participants);
        await places.GetSnapshotAsync();
        _fx.Time.Advance(UntilLocal(20 * 60 + 30));

        Assert.False(await places.TurnInAsync(human));
        Assert.Null((await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == human).TurnedInUntil);
    }
}
