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
                siloBuilder.ConfigureServices(services => services.AddSingleton<TimeProvider>(ManualTime.Instance));
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
