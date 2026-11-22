using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

public partial class PlacesTest
{
    private async Task<double> WarmthAtAsync(Guid partyId, Guid who, Guid placeId)
        => Belonging.Warmth((await SelfOf(partyId, who).GetStateAsync()).Places, placeId, DateTimeOffset.Now);

    private async Task FeelAsync(Guid partyId, Guid who, Place place, double belonging, int times, double mood = 0.5)
    {
        for (var i = 0; i < times; i++) await SelfOf(partyId, who).FeelAtAsync(place.Id, place.Name, mood, belonging);
    }

    private async Task<(IPlacesGrain Places, PlacesApp App, Place Cafe)> AtTheLobbyAsync(Guid partyId, Guid who)
    {
        var places = _fx.Places(partyId);
        var snapshot = await places.GetSnapshotAsync();
        await places.MoveAsync(who, snapshot.Places.Single(p => p.Name == "the Lobby").Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        return (places, new PlacesApp(_fx.GrainFactory), snapshot.Places.Single(p => p.Name == "the Café"));
    }

    private async Task<IReadOnlyList<Affordance>> SeenAsync(PlacesApp app, Guid partyId, Guid who, Reserves body)
        => (await app.PerceiveAsync(LifeOf(partyId, who, "Denise", body), default))!.Affordances;

    private static Affordance GoTo(IEnumerable<Affordance> seen, Place place) => seen.Single(a => a.Verb == "go" && a.TargetId == place.Id);

    [Fact]
    public async Task AMealAtTheCafe_WarmsIt_AndSeveralGoodTimesMakeItAHaunt()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        var hungry = await WakeHungryAsync(partyId, denise);
        await places.MoveAsync(denise, cafe.Id, null);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var ctx = LifeOf(partyId, denise, "Denise", hungry);
        var order = Assert.Single((await app.PerceiveAsync(ctx, default))!.Affordances, a => a.Meets == Bio.Food);

        await app.ActAsync(ctx, order, "a cheese sandwich", null, default);

        var once = await WarmthAtAsync(partyId, denise, cafe.Id);
        Assert.InRange(once, 0.01, Belonging.Haunt - 0.01);
        await FeelAsync(partyId, denise, cafe, 0.8, 6);
        Assert.True(Belonging.IsHaunt(await WarmthAtAsync(partyId, denise, cafe.Id)));
    }

    [Fact]
    public async Task FromElsewhere_ARunDownBodyIsPulledToItsHaunt_AndAWellOneOnlyReminded()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var (_, app, cafe) = await AtTheLobbyAsync(partyId, denise);
        var before = GoTo(await SeenAsync(app, partyId, denise, Felt()), cafe);

        await FeelAsync(partyId, denise, cafe, 1, 4, mood: 0.9);
        var well = GoTo(await SeenAsync(app, partyId, denise, Felt()), cafe);
        var runDown = GoTo(await SeenAsync(app, partyId, denise, Felt() with { Battery = 0.3 }), cafe);

        Assert.DoesNotContain(Belonging.YoursHook, before.Label);
        Assert.StartsWith("Go to the Café", well.Label);
        Assert.EndsWith(Belonging.YoursHook, well.Label);
        Assert.EndsWith(Belonging.YoursHook, runDown.Label);
        Assert.Equal(before.Priority, well.Priority);
        Assert.Equal(well.Priority + 1, runDown.Priority);
    }

    [Fact]
    public async Task AWellBody_IsOfferedExactlyOnePlaceItHasNeverBeen()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var (places, app, _) = await AtTheLobbyAsync(partyId, denise);

        var flat = await SeenAsync(app, partyId, denise, Felt());
        await SelfOf(partyId, denise).ShiftValenceAsync(0.9, "the Lobby");
        var well = await SeenAsync(app, partyId, denise, Felt());
        var tired = await SeenAsync(app, partyId, denise, Felt(rested: 0.3));

        Assert.DoesNotContain(flat, a => a.Label.Contains(Belonging.NewHook));
        Assert.DoesNotContain(tired, a => a.Label.Contains(Belonging.NewHook));
        var fresh = Assert.Single(well, a => a.Label.Contains(Belonging.NewHook));
        Assert.EndsWith(Belonging.NewHook, fresh.Label);
        Assert.Equal(flat.Single(a => a.Verb == "go" && a.TargetId == fresh.TargetId).Priority + 1, fresh.Priority);
        var visited = await places.GetVisitDaysAsync(denise);
        Assert.False(visited.ContainsKey(fresh.TargetId!.Value));
        Assert.Equal(PlaceKinds.Public, (await places.GetSnapshotAsync()).Places.Single(p => p.Id == fresh.TargetId).Kind);
    }

    [Fact]
    public async Task APlaceTheyArePutOffBy_LosesAPull_AndSaysNothing()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var (_, app, cafe) = await AtTheLobbyAsync(partyId, denise);
        var before = GoTo(await SeenAsync(app, partyId, denise, Felt()), cafe);

        await FeelAsync(partyId, denise, cafe, 0, 4);
        var after = GoTo(await SeenAsync(app, partyId, denise, Felt()), cafe);

        Assert.True(Belonging.IsPutOff(await WarmthAtAsync(partyId, denise, cafe.Id)));
        Assert.Equal(before.Priority - 1, after.Priority);
        Assert.DoesNotContain(Belonging.YoursHook, after.Label);
        Assert.DoesNotContain(Belonging.NewHook, after.Label);
    }

    [Fact]
    public async Task TheBodysOwnHook_WinsOverAHaunt()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var (_, app, cafe) = await AtTheLobbyAsync(partyId, denise);
        var hungry = Felt(fed: 0.25) with { Battery = 0.3 };
        var before = GoTo(await SeenAsync(app, partyId, denise, hungry), cafe);

        await FeelAsync(partyId, denise, cafe, 1, 4, mood: 0.9);
        var after = GoTo(await SeenAsync(app, partyId, denise, hungry), cafe);

        Assert.Equal("Go to the Café (to get something to eat)", after.Label);
        Assert.Equal(before.Priority, after.Priority);
    }

    [Fact]
    public async Task AtAHaunt_RestlessnessComesLater()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var app = new PlacesApp(_fx.GrainFactory);
        var snapshot = await places.GetSnapshotAsync();
        var cafe = snapshot.Places.Single(p => p.Name == "the Café");
        var centre = snapshot.Places.Single(p => p.Name == "the city centre");
        await places.MoveAsync(denise, cafe.Id, null);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await places.GetSituationAsync(denise);
        _fx.Time.Advance(TimeSpan.FromMinutes(60));
        var restless = GoTo(await SeenAsync(app, partyId, denise, Felt()), centre);

        await FeelAsync(partyId, denise, cafe, 1, 4);
        var settled = GoTo(await SeenAsync(app, partyId, denise, Felt()), centre);

        Assert.Equal(restless.Priority - 1, settled.Priority);
    }

    private sealed class LiftingJudge : ISystemOneJudge
    {
        public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
        {
            var answers = new SystemOneAnswers();
            foreach (var (key, question) in questions)
                if (question is ChoiceQuestion) answers.Choices[key] = new Dictionary<string, double> { ["better"] = 1, ["same"] = 0, ["worse"] = 0 };
            return Task.FromResult(answers);
        }
    }

    private static Moment Talk(Guid partyId, string source, Guid threadId, Guid a, Guid b) => new(partyId, source, threadId,
        new List<MomentLine> { new(a, "hey, how was your day?", 1), new(b, "honestly great, this place helps", 2) }, 1,
        new List<ParticipantView> { new(a, "Denise", DriverKind.LLM), new(b, "Pieter", DriverKind.LLM) });

    [Fact]
    public async Task AGoodTalkAtAPlace_WarmsThePlace_AndADmDoesNot()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(denise, cafe.Id, null);
        await places.JoinAsync(pieter, denise);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var talkRoom = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == cafe.Id).TalkRoomId!.Value;
        var dm = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetOrCreateDirectChatAsync(denise, pieter);
        var appraisal = new MomentAppraisal(new LiftingJudge(), _fx.GrainFactory, NullLogger<MomentAppraisal>.Instance);

        await appraisal.AppraiseAsync(Talk(partyId, MomentSources.Places, talkRoom, denise, pieter), new HashSet<Guid> { denise });
        await appraisal.AppraiseAsync(Talk(partyId, MomentSources.Messenger, dm.Id, pieter, denise), new HashSet<Guid> { pieter });

        Assert.True(await WarmthAtAsync(partyId, denise, cafe.Id) > 0);
        var theirs = await SelfOf(partyId, pieter).GetStateAsync();
        Assert.Empty(theirs.Places);
        Assert.True(SelfMath.Valence(theirs, DateTimeOffset.Now) > 0);
    }
}
