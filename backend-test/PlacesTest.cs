using BackendTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.TestingHost;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>Places phase 1: one Body per Participant, walking takes time, Place talk cast follows occupants.</summary>
public class PlacesTest : IClassFixture<PlacesTest.Fixture>
{
    private readonly Fixture _fx;

    public PlacesTest(Fixture fx) => _fx = fx;

    [Fact]
    public async Task EveryParticipantGetsOneBodyAtTheirOwnHome()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();

        var snapshot = await _fx.Places(partyId).GetSnapshotAsync();

        Assert.Equal(new[] { denise, pieter }.Order(), snapshot.Bodies.Select(b => b.ParticipantId).Order());
        Assert.DoesNotContain(snapshot.Bodies, b => b.ParticipantId == Narrator.PersonaId);
        foreach (var body in snapshot.Bodies)
        {
            var home = snapshot.Places.Single(p => p.Id == body.PlaceId);
            Assert.Equal(PlaceKinds.Home, home.Kind);
            Assert.Equal(body.ParticipantId, home.OwnerId);
        }
    }

    [Fact]
    public async Task WalkingTakesTime_ThenTheBodyArrives()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");

        var walking = await places.MoveAsync(denise, cafe.Id, "window-table");
        Assert.NotNull(walking);
        Assert.Equal(Posture.Walking, walking.Posture);
        Assert.Contains("on your way to the Café", await places.GetSituationAsync(denise));

        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var situation = await places.GetSituationAsync(denise);

        Assert.StartsWith("You're at the Café, sitting at the window table.", situation);
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(cafe.Id, body.PlaceId);
        Assert.False(body.IsWalking);
    }

    [Fact]
    public async Task OthersHomesAreNotEnterable()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var pietersHome = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == pieter);

        Assert.Null(await places.MoveAsync(denise, pietersHome.Id, null));
    }

    [Fact]
    public async Task PlaceTalkCast_FollowsWhoIsThere()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");

        await places.MoveAsync(denise, cafe.Id, null);
        await places.JoinAsync(pieter, denise);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == cafe.Id);

        Assert.NotNull(cafe.TalkRoomId);
        var room = _fx.GrainFactory.GetGrain<IChatGroupGrain>(cafe.TalkRoomId.Value);
        Assert.True((await room.GetParticipantIdsAsync()).SetEquals(new[] { denise, pieter }));
        var info = (await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetChatGroups()).Single(g => g.Id == cafe.TalkRoomId);
        Assert.Equal(ChatGroupKinds.Place, info.Kind);
        Assert.Contains("Pieter (right next to you)", await places.GetSituationAsync(denise));

        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        await places.MoveAsync(denise, home.Id, null);

        Assert.True((await room.GetParticipantIdsAsync()).SetEquals(new[] { pieter }));
    }

    [Fact]
    public async Task TextingShowsOnTheBody()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        await places.GetSnapshotAsync();
        var dm = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetOrCreateDirectChatAsync(denise, pieter);

        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).SendNewMessageAsync(denise, "you around?", null, null);

        string? activity = null;
        for (var i = 0; i < 50 && activity is null; i++)
        {
            await Task.Delay(100);
            activity = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise).Activity;
        }
        Assert.Equal("on their phone, texting", activity);
        Assert.Contains("on your phone, texting", await places.GetSituationAsync(denise));
    }

    [Fact]
    public async Task ADryPlantShowsInTheSituation_AndWateringFixesIt()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);
        Assert.Contains(home.Furni, f => f.Kind == FurniKinds.Plant);

        _fx.Time.Advance(TimeSpan.FromHours(9));

        Assert.Contains("Around you: the plant on the windowsill looks a bit dry", await places.GetSituationAsync(denise));
        var done = await places.ActOnFurniAsync(denise, home.Id, "plant", care: true, intent: null);
        Assert.Equal("waters the plant on the windowsill at Denise's place", done);
        Assert.DoesNotContain("dry", await places.GetSituationAsync(denise));
        Assert.Contains("watering the plant", await places.GetSituationAsync(denise));
    }

    [Fact]
    public async Task Furni_OnlyWhereYourBodyIs()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var pietersHome = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == pieter);

        Assert.Null(await places.ActOnFurniAsync(denise, pietersHome.Id, "coffee", care: false, intent: null));
    }

    [Fact]
    public async Task PlaceTalk_OnlyYourSpotHears_TheRestOverhear()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(denise, cafe.Id, "counter");
        await places.MoveAsync(pieter, cafe.Id, "booth");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == cafe.Id);
        var talk = _fx.GrainFactory.GetGrain<IChatGroupGrain>(cafe.TalkRoomId!.Value);

        var aside = await talk.SendNewMessageAsync(denise, "honestly Pieter has been impossible today", null, null);

        Assert.Equal(new[] { denise }, aside.HeardBy);
        Assert.Equal(Volumes.Talk, aside.Volume);
        Assert.DoesNotContain(await talk.GetMessagesUntilAsync(int.MaxValue, pieter), m => m.MessageId == aside.MessageId);
        Assert.Contains(await talk.GetMessagesUntilAsync(int.MaxValue, denise), m => m.MessageId == aside.MessageId);
        List<Overheard> overheard = new();
        for (var i = 0; i < 50 && overheard.Count == 0; i++)
        {
            await Task.Delay(100);
            overheard = await places.GetOverheardAsync(pieter);
        }
        var o = Assert.Single(overheard);
        Assert.Contains(pieter, o.NameHeardBy);

        await places.SetVoiceAsync(denise, Volumes.Shout, null);
        var shout = await talk.SendNewMessageAsync(denise, "anyone want a coffee?", null, null);
        Assert.Contains(pieter, shout.HeardBy!);
    }

    [Fact]
    public async Task AtNight_PersonasWalkHomeToBed_FallAsleep_AndWakeInTheMorning()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(denise, cafe.Id, null);
        var (bed, wake) = Sleep.Window(denise);

        _fx.Time.Advance(UntilLocal(bed + 5));
        await places.GetAsleepAsync();
        Assert.Contains("on your way to your place", await places.GetSituationAsync(denise));
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        Assert.Contains(denise, await places.GetAsleepAsync());
        Assert.Equal("You're asleep in bed at home.", await places.GetSituationAsync(denise));

        _fx.Time.Advance(UntilLocal(wake + 5));
        Assert.DoesNotContain(denise, await places.GetAsleepAsync());
        Assert.Contains("just got up", await places.GetSituationAsync(denise));
    }

    /// <summary>How far to push the clock to reach a local minute-of-day next.</summary>
    private TimeSpan UntilLocal(int minuteOfDay)
    {
        var local = _fx.Time.GetLocalNow();
        var nowMinute = local.Hour * 60 + local.Minute;
        var delta = ((minuteOfDay % 1440) - nowMinute + 1440) % 1440;
        return TimeSpan.FromMinutes(delta == 0 ? 1440 : delta);
    }

    [Fact]
    public async Task KnockingWhenTheyreHome_AndBeingLetIn()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var snapshot = await places.GetSnapshotAsync();
        var pietersHome = snapshot.Places.Single(p => p.OwnerId == pieter);

        Assert.Null(await places.MoveAsync(denise, pietersHome.Id, null));
        Assert.True(await places.KnockAsync(denise, pietersHome.Id));
        Assert.Equal(new[] { denise }, await places.GetKnocksAsync(pieter));

        Assert.True(await places.LetInAsync(pieter, denise));
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        Assert.Contains("at Pieter's place", await places.GetSituationAsync(denise));
        Assert.Empty(await places.GetKnocksAsync(pieter));

        // When the owner leaves, the door closes behind them.
        var cafe = snapshot.Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(pieter, cafe.Id, null);
        await places.MoveAsync(denise, cafe.Id, null);
        Assert.Null(await places.MoveAsync(denise, pietersHome.Id, null));
    }

    [Fact]
    public async Task Decorating_IsAtHomeOnly_TwiceADay_AndVisitorsSeeIt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var home = (await places.GetSnapshotAsync()).Places.Single(p => p.OwnerId == denise);

        Assert.Equal("puts up a Lisp poster at Denise's place", await places.DecorateAsync(denise, "put up a Lisp poster"));
        Assert.NotNull(await places.DecorateAsync(denise, "a monstera by the window"));
        Assert.Null(await places.DecorateAsync(denise, "a third thing today"));

        await places.KnockAsync(pieter, home.Id);
        await places.LetInAsync(denise, pieter);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await places.DecorateAsync(pieter, "a poster in Denise's place"));
        Assert.Contains("On display: a monstera by the window; a Lisp poster", await places.GetSituationAsync(pieter));
        Assert.DoesNotContain("On display", await places.GetSituationAsync(denise));
    }

    [Fact]
    public async Task EveryBody_HasALook()
    {
        var (partyId, _, _) = await _fx.CreateWorldAsync();
        var bodies = (await _fx.Places(partyId).GetSnapshotAsync()).Bodies;
        Assert.All(bodies, b => Assert.Contains(b.Appearance!.HairStyle, AppearanceRules.Styles));
    }

    [Fact]
    public async Task OnlyHumansDoze_AndWakeWhenBack()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var human = Guid.NewGuid();
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var participants = (await party.GetParty()).Participants.ToList();
        participants.Add(new PartyParticipant { Id = human, Name = "Tim", Driver = DriverKind.User });
        await party.SetParticipants(participants);
        await places.GetSnapshotAsync();

        await places.SetIdleAsync(denise, true);
        await places.SetIdleAsync(human, true);
        Assert.DoesNotContain("dozing", await places.GetSituationAsync(denise));
        Assert.Contains("dozing off in front of the screen", await places.GetSituationAsync(human));

        await places.SetIdleAsync(human, false);
        Assert.DoesNotContain("dozing", await places.GetSituationAsync(human));
    }

    [Fact]
    public async Task AShout_CarriesThroughTheDoor_AsTone()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var snapshot = await places.GetSnapshotAsync();
        var cafe = snapshot.Places.Single(p => p.Name == "the Café");
        var lobby = snapshot.Places.Single(p => p.Name == "the Lobby");
        await places.MoveAsync(denise, cafe.Id, null);
        await places.MoveAsync(pieter, lobby.Id, null);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == cafe.Id);

        await places.SetVoiceAsync(denise, Volumes.Shout, null);
        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(cafe.TalkRoomId!.Value).SendNewMessageAsync(denise, "PIETER, your coffee's ready", null, null);

        List<Overheard> heard = new();
        for (var i = 0; i < 50 && heard.Count == 0; i++)
        {
            await Task.Delay(100);
            heard = await places.GetOverheardAsync(pieter);
        }
        var (line, _, name) = Assert.Single(Overhearing.Lines(heard, pieter, lobby));
        Assert.StartsWith("Through the door from the Café, Denise is calling out", line);
        Assert.True(name);
    }

    [Fact]
    public async Task ALookCanBePickedByHand_OnThePaletteOnly()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        await places.GetSnapshotAsync();

        var look = await places.SetAppearanceAsync(denise, new Dictionary<string, string?> { ["hair"] = "Pink", ["top"] = "neon-plaid" });

        Assert.Equal("pink", look!.Hair);
        Assert.Contains(look.Top, AppearanceRules.Colours);
        Assert.True(look.FromBio);
    }

    [Fact]
    public async Task RoomsAndDms_StayHeardByEveryone()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var room = await party.CreateChatGroup("kitchen");
        var dm = await party.GetOrCreateDirectChatAsync(denise, pieter);

        var inRoom = await _fx.GrainFactory.GetGrain<IChatGroupGrain>(room.Id).SendNewMessageAsync(denise, "hi all", null, null);
        var inDm = await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).SendNewMessageAsync(denise, "hi you", null, null);

        Assert.Null(inRoom.HeardBy);
        Assert.Null(inDm.HeardBy);
        Assert.Single(await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).GetMessagesUntilAsync(int.MaxValue, pieter));
    }

    public sealed class Fixture : IAsyncLifetime
    {
        public TestCluster Cluster { get; private set; } = null!;
        public IGrainFactory GrainFactory => Cluster.GrainFactory;
        public ManualTime Time => ManualTime.Instance;

        public Task InitializeAsync()
        {
            var builder = new TestClusterBuilder(1);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            Cluster = builder.Build();
            return Cluster.DeployAsync();
        }

        public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

        public IPlacesGrain Places(Guid partyId) => GrainFactory.GetGrain<IPlacesGrain>(partyId);

        public async Task<(Guid PartyId, Guid Denise, Guid Pieter)> CreateWorldAsync()
        {
            // Every world starts at local noon, so earlier tests' nights don't send everyone to bed.
            var local = Time.GetLocalNow();
            var toNoon = (12 * 60 - (local.Hour * 60 + local.Minute) + 1440) % 1440;
            Time.Advance(TimeSpan.FromMinutes(toNoon == 0 ? 1440 : toNoon));

            var partyId = Guid.NewGuid();
            var denise = Guid.NewGuid();
            var pieter = Guid.NewGuid();
            var library = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
            await library.AddPersona(new Persona(denise, "Denise", "You are Denise.", "A hacker."));
            await library.AddPersona(new Persona(pieter, "Pieter", "You are Pieter.", "An architect."));
            var participants = new List<PartyParticipant>
            {
                new() { Id = denise, Name = "Denise", Driver = DriverKind.LLM },
                new() { Id = pieter, Name = "Pieter", Driver = DriverKind.LLM },
            };
            await GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty)
                .AddParty(new PartyInfo { Id = partyId, Name = "world", Participants = participants });
            await GrainFactory.GetGrain<IPartyGrain>(partyId).SetParticipants(participants);
            return (partyId, denise, pieter);
        }

        private sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.ConfigureDefaults();
                siloBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<TimeProvider>(ManualTime.Instance);
                    services.AddSingleton(Moq.Mock.Of<PartyTown.Services.SystemOne.ISystemOneJudge>(j => j.Enabled == false));
                    services.AddSingleton(Moq.Mock.Of<PartyTown.Services.Memory.IStanceConsolidator>());
                    services.AddSingleton<PartyTown.Services.Memory.ConsolidationService>();
                    services.AddSingleton<PartyTown.World.WorldMemory>();
                    services.AddSingleton(Moq.Mock.Of<PartyTown.World.IWorldLlm>());
                });
            }
        }
    }

    /// <summary>Wall clock the test can push forward; shared because the silo is in-proc.</summary>
    public sealed class ManualTime : TimeProvider
    {
        public static readonly ManualTime Instance = new();
        private long _offsetTicks;

        public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddTicks(Interlocked.Read(ref _offsetTicks));
    }
}
