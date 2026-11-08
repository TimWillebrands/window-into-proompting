using System.Collections.Concurrent;
using BackendTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans.Hosting;
using Orleans.TestingHost;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Places;
using PartyTown.World.Reflex;

namespace BackendTest;

/// <summary>Reflex in a real cluster (ADR 0031): a scripted System One, real grains, across channels.</summary>
public class ReflexWorldTest(ReflexWorldTest.Fixture fx) : IClassFixture<ReflexWorldTest.Fixture>
{
    private IGrainFactory Grains => fx.GrainFactory;

    private async Task<(Guid Party, Guid Denise, Guid Hana)> WorldAsync(bool running = true, bool hanaHuman = false)
    {
        var (partyId, denise, hana) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Grains.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(denise, "Denise", "You are Denise.", "A designer."));
        await Grains.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(hana, "Hana", "You are Hana.", "A nurse."));
        var participants = new List<PartyParticipant>
        {
            new() { Id = denise, Name = "Denise", Driver = DriverKind.LLM },
            new() { Id = hana, Name = "Hana", Driver = hanaHuman ? DriverKind.User : DriverKind.LLM },
        };
        await Grains.GetGrain<IPartyRootGrain>(Guid.Empty).AddParty(new PartyInfo { Id = partyId, Name = "world", Participants = participants });
        await Grains.GetGrain<IPartyGrain>(partyId).SetParticipants(participants);
        await Grains.GetGrain<IWorldClockGrain>(partyId).UpdateAsync(running, null, null);
        return (partyId, denise, hana);
    }

    private IPersonaReflexGrain Reflex(Guid party, Guid who) => Grains.GetGrain<IPersonaReflexGrain>(WorldKeys.Life(party, who));

    private static Percept Dm(Guid from, Guid room, bool human = false, string text = "you around?", string name = "Hana")
        => new(PerceptKind.Direct, from, name, human, text, room);

    [Fact]
    public async Task PausedWorld_LeavesEveryMessageToTheNormalPath()
    {
        var (party, denise, hana) = await WorldAsync(running: false);
        ScriptedJudge.Pick(ReflexOptions.Soon);

        var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, Guid.NewGuid()));

        Assert.Equal(string.Empty, verdict.Note);
        Assert.True(verdict.Proceed);
        Assert.Empty(await Reflex(party, denise).GetIntentionsAsync());
    }

    [Fact]
    public async Task Now_ReplyGoesAheadAfterReadingTime()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Now);

        var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, Guid.NewGuid()));

        Assert.True(verdict.Proceed);
        Assert.False(verdict.Deliberate);
        Assert.True(verdict.LagMs > 0);
    }

    [Fact]
    public async Task Soon_BecomesAnIntention_AndTheHumanIsOwedByADeadline()
    {
        var (party, denise, hana) = await WorldAsync(hanaHuman: true);
        var room = Guid.NewGuid();
        ScriptedJudge.Pick(ReflexOptions.Soon);

        var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, room, human: true));

        Assert.False(verdict.Proceed);
        var held = Assert.Single(await Reflex(party, denise).GetIntentionsAsync());
        Assert.Equal(room, held.RoomId);
        Assert.NotNull(held.OwedBy);
        Assert.True(held.DueAt <= held.OwedBy);
        Assert.Contains("get back to Hana", await Reflex(party, denise).GetMindAsync());
    }

    [Fact]
    public async Task PersonaToPersona_NobodyIsOwed_SoItCanSlipTheirMind()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Soon);

        await Reflex(party, denise).PerceiveAsync(Dm(hana, Guid.NewGuid()));

        Assert.Null(Assert.Single(await Reflex(party, denise).GetIntentionsAsync()).OwedBy);
    }

    [Fact]
    public async Task FollowUp_WhileHeld_RefreshesTheIntention_WithoutAskingAgain_KeepingWhatTheyMeantToAnswer()
    {
        var (party, denise, hana) = await WorldAsync();
        var room = Guid.NewGuid();
        ScriptedJudge.Pick(ReflexOptions.Soon);
        await Reflex(party, denise).PerceiveAsync(Dm(hana, room));
        var asked = ScriptedJudge.Asked;
        var gist = Assert.Single(await Reflex(party, denise).GetIntentionsAsync()).Gist;

        var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, room, text: "hello??"));

        Assert.False(verdict.Proceed);
        Assert.Equal(asked, ScriptedJudge.Asked);
        Assert.Equal(gist, Assert.Single(await Reflex(party, denise).GetIntentionsAsync()).Gist);
    }

    [Fact]
    public async Task SpeakingInTheRoom_SettlesWhatWasOwedThere()
    {
        var (party, denise, hana) = await WorldAsync();
        var room = Guid.NewGuid();
        ScriptedJudge.Pick(ReflexOptions.Soon);
        await Reflex(party, denise).PerceiveAsync(Dm(hana, room));

        await Reflex(party, denise).NoteSpokeAsync(room);

        Assert.Empty(await Reflex(party, denise).GetIntentionsAsync());
    }

    [Fact]
    public async Task MidConversation_NoAppraisal_JustReadingTime()
    {
        var (party, denise, hana) = await WorldAsync();
        var room = Guid.NewGuid();
        ScriptedJudge.Pick(ReflexOptions.Leave);
        await Reflex(party, denise).NoteSpokeAsync(room);
        var asked = ScriptedJudge.Asked;

        var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, room));

        Assert.True(verdict.Proceed);
        Assert.Equal(asked, ScriptedJudge.Asked);
    }

    [Fact]
    public async Task TalkingElsewhere_RaisesLoad_SoRoomChatterGoesUnnoticed()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Speak);
        await Reflex(party, denise).NoteSpokeAsync(Guid.NewGuid());
        await Grains.GetGrain<IPlacesGrain>(party).SetActivityAsync(denise, "working on something", TimeSpan.FromMinutes(5));
        var asked = ScriptedJudge.Asked;

        // Habituated repeats from a stranger under load: the filter, not the model, drops these.
        for (var i = 0; i < 6; i++)
            await Reflex(party, denise).PerceiveAsync(new Percept(PerceptKind.Line, hana, "Hana", false, "anyway", Guid.NewGuid()));

        Assert.True(ScriptedJudge.Asked - asked < 6);
    }

    [Fact]
    public async Task RepeatedMessages_FromSomeoneTheyDontAnswer_KeepGettingThrough()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Leave);

        for (var i = 0; i < 8; i++)
            Assert.Equal(ReflexOptions.Leave, (await Reflex(party, denise).PerceiveAsync(Dm(hana, Guid.NewGuid()))).Note);
    }

    [Fact]
    public async Task BeingOnTheirPhone_DoesNotMakeThemHarderToReachByPhone()
    {
        var (party, denise, hana) = await WorldAsync();
        await Grains.GetGrain<IPlacesGrain>(party).NoteActivityAsync(denise, "messenger");
        ScriptedJudge.Pick(ReflexOptions.Now);

        await Reflex(party, denise).PerceiveAsync(Dm(hana, Guid.NewGuid()));

        Assert.Contains("free to be distracted", ScriptedJudge.Seen.Last());
    }

    [Fact]
    public async Task JudgeDown_ReplyGoesAheadAsUsual()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Fail();

        var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, Guid.NewGuid()));

        Assert.True(verdict.Proceed);
        Assert.Empty(await Reflex(party, denise).GetIntentionsAsync());
    }

    [Fact]
    public async Task Torn_HandsTheCallToSystemTwo()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Script(new Dictionary<string, double>
        {
            [ReflexOptions.Now] = 0.5, [ReflexOptions.Soon] = 0.5, [ReflexOptions.Brief] = 0, [ReflexOptions.Leave] = 0,
        });
        var torn = false;
        for (var i = 0; i < 40 && !torn; i++)
        {
            var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, Guid.NewGuid()));
            torn = verdict is { Proceed: true, Deliberate: true };
        }

        Assert.True(torn);
    }

    private async Task<Body> AtLobbyAsync(Guid party, Guid who, string? spot = null)
    {
        var places = Grains.GetGrain<IPlacesGrain>(party);
        var lobby = (await places.GetSnapshotAsync()).Places.First(p => p.Kind == PlaceKinds.Public);
        await places.MoveAsync(who, lobby.Id, spot);
        fx.Time.Advance(TimeSpan.FromMinutes(1));
        return (await places.GetSnapshotAsync()).Bodies.First(b => b.ParticipantId == who);
    }

    [Fact]
    public async Task Arrival_NextToThem_IsPerceived_NotLeftForTheirNextLifeTurn()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.CarryOn);
        var me = await AtLobbyAsync(party, denise);

        await AtLobbyAsync(party, hana, me.SpotId);

        await TestWait.UntilAsync(() => Task.FromResult(ScriptedJudge.Seen.Any(s => s.Contains("Hana just walked up and is now right next to Denise"))), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Nod_ShowsOnTheBody()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Nod);
        var me = await AtLobbyAsync(party, denise);

        await AtLobbyAsync(party, hana, me.SpotId);

        var places = Grains.GetGrain<IPlacesGrain>(party);
        await TestWait.UntilAsync(async () => (await places.GetBodyAsync(denise))?.Activity == "nods at Hana", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Move_WalksThemAwayFromWhoeverCameUp()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Move);
        var me = await AtLobbyAsync(party, denise);

        await AtLobbyAsync(party, hana, me.SpotId);

        var places = Grains.GetGrain<IPlacesGrain>(party);
        await TestWait.UntilAsync(async () => (await places.GetBodyAsync(denise))?.DestSpotId is { } dest && dest != me.SpotId, TimeSpan.FromSeconds(5));
    }

    private async Task<bool> SpokeAsync(Guid room, Guid who)
        => (await Grains.GetGrain<IChatGroupGrain>(room).GetMessagesUntilAsync(int.MaxValue)).Any(m => m.SenderId == who);

    [Fact]
    public async Task Greet_SpeaksInTheTalkRoom()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Greet);
        var me = await AtLobbyAsync(party, denise);

        await AtLobbyAsync(party, hana, me.SpotId);

        var lobby = (await Grains.GetGrain<IPlacesGrain>(party).GetSnapshotAsync()).Places.First(p => p.Id == me.PlaceId);
        await TestWait.UntilAsync(() => SpokeAsync(lobby.TalkRoomId!.Value, denise), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Greet_OutOfBudget_BetweenPersonas_IsANodInstead()
    {
        var (party, denise, hana) = await WorldAsync();
        await Grains.GetGrain<IWorldClockGrain>(party).UpdateAsync(null, null, 0);
        ScriptedJudge.Pick(ReflexOptions.Greet);
        var me = await AtLobbyAsync(party, denise);

        await AtLobbyAsync(party, hana, me.SpotId);

        var places = Grains.GetGrain<IPlacesGrain>(party);
        await TestWait.UntilAsync(async () => (await places.GetBodyAsync(denise))?.Activity == "nods at Hana", TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Brief_SendsALine_AndStillOwesTheRealAnswer()
    {
        var (party, denise, hana) = await WorldAsync(hanaHuman: true);
        var dm = await Grains.GetGrain<IPartyGrain>(party).GetOrCreateDirectChatAsync(hana, denise);
        ScriptedJudge.Pick(ReflexOptions.Brief);

        var verdict = await Reflex(party, denise).PerceiveAsync(Dm(hana, dm.Id, human: true));

        Assert.False(verdict.Proceed);
        Assert.StartsWith(ReflexVerdict.Holding, verdict.Note);
        Assert.Single(await Reflex(party, denise).GetIntentionsAsync());
        await TestWait.UntilAsync(() => SpokeAsync(dm.Id, denise), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TheQuickLineItself_DoesNotSettleWhatTheyOwe()
    {
        var (party, denise, hana) = await WorldAsync(hanaHuman: true);
        var dm = await Grains.GetGrain<IPartyGrain>(party).GetOrCreateDirectChatAsync(hana, denise);
        ScriptedJudge.Pick(ReflexOptions.Brief);
        await Reflex(party, denise).PerceiveAsync(Dm(hana, dm.Id, human: true));

        // With no model here the line stays in retries for seconds, so it's still in flight for this.
        await Reflex(party, denise).NoteSpokeAsync(dm.Id);

        Assert.Single(await Reflex(party, denise).GetIntentionsAsync());
        await Reflex(party, denise).NoteSpokeAsync(dm.Id);
        Assert.Empty(await Reflex(party, denise).GetIntentionsAsync());
    }

    [Fact]
    public async Task PutOff_ComesBackWhenDue_AndTheyGetBackToThem()
    {
        var (party, denise, hana) = await WorldAsync(hanaHuman: true);
        var dm = await Grains.GetGrain<IPartyGrain>(party).GetOrCreateDirectChatAsync(hana, denise);
        ScriptedJudge.Pick(ReflexOptions.Soon);
        await Reflex(party, denise).PerceiveAsync(Dm(hana, dm.Id, human: true));
        Assert.False(await SpokeAsync(dm.Id, denise));

        fx.Time.Advance(ReflexMath.OwedWithin);
        await Reflex(party, denise).SurfaceAsync();

        Assert.Empty(await Reflex(party, denise).GetIntentionsAsync());
        await TestWait.UntilAsync(() => SpokeAsync(dm.Id, denise), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Clock_LooksAtWhatsDue_OnThePulse()
    {
        var (party, denise, hana) = await WorldAsync(hanaHuman: true);
        var dm = await Grains.GetGrain<IPartyGrain>(party).GetOrCreateDirectChatAsync(hana, denise);
        ScriptedJudge.Pick(ReflexOptions.Soon);
        await Reflex(party, denise).PerceiveAsync(Dm(hana, dm.Id, human: true));
        fx.Time.Advance(ReflexMath.OwedWithin);

        await Grains.GetGrain<IWorldClockGrain>(party).RemindAsync(denise, 1);
        await Grains.GetGrain<IWorldClockGrain>(party).PulseAsync();

        await TestWait.UntilAsync(async () => (await Reflex(party, denise).GetIntentionsAsync()).Count == 0, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Knock_Open_LetsThemIn_WithoutWaitingForALifeTurn()
    {
        var (party, denise, hana) = await WorldAsync();
        ScriptedJudge.Pick(ReflexOptions.Open);
        var places = Grains.GetGrain<IPlacesGrain>(party);
        var snapshot = await places.GetSnapshotAsync();
        var home = snapshot.Places.First(p => p.OwnerId == denise);
        Assert.Equal(home.Id, snapshot.Bodies.First(b => b.ParticipantId == denise).PlaceId);

        Assert.True(await places.KnockAsync(hana, home.Id));

        await TestWait.UntilAsync(async () => (await places.GetSnapshotAsync()).Places.First(p => p.Id == home.Id).Guests.Contains(hana), TimeSpan.FromSeconds(5));
    }

    /// <summary>One script at a time: tests in a class run in sequence.</summary>
    public sealed class ScriptedJudge : ISystemOneJudge
    {
        private static Dictionary<string, double>? _weights;
        private static bool _down;
        private static int _asked;
        private static readonly ConcurrentQueue<string> States = new();

        public static void Pick(string option) => Script(new Dictionary<string, double> { [option] = 1 });

        public static void Script(Dictionary<string, double> weights) => (_weights, _down) = (weights, false);

        public static void Fail() => _down = true;

        public static int Asked => _asked;

        public static IReadOnlyCollection<string> Seen => States;

        public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
        {
            var answers = new SystemOneAnswers();
            if (!questions.TryGetValue(ReflexWeighing.Key, out var question) || question is not ChoiceQuestion choice) return Task.FromResult(answers);
            Interlocked.Increment(ref _asked);
            States.Enqueue(state);
            if (_down) throw new HttpRequestException("down");
            answers.Choices[ReflexWeighing.Key] = choice.Options.Keys.ToDictionary(o => o, o => _weights?.GetValueOrDefault(o) ?? 0);
            return Task.FromResult(answers);
        }
    }

    public sealed class Fixture : IAsyncLifetime
    {
        private TestCluster _cluster = default!;

        public IGrainFactory GrainFactory => _cluster.GrainFactory;

        public Noon Time => Noon.Instance;

        public async Task InitializeAsync()
        {
            var builder = new TestClusterBuilder { Options = { InitialSilosCount = 1 } };
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            _cluster = builder.Build();
            await _cluster.DeployAsync();
        }

        public async Task DisposeAsync() => await _cluster.DisposeAsync();

        private sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.ConfigureDefaults();
                siloBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<TimeProvider>(Noon.Instance);
                    services.AddSingleton(Mock.Of<IWorldLlm>());
                    services.AddSingleton(Mock.Of<IStanceConsolidator>());
                    services.AddSingleton<ConsolidationService>();
                    services.AddSingleton<WorldMemory>();
                    services.AddSingleton<ISystemOneJudge, ScriptedJudge>();
                    services.AddSingleton<PartyTown.World.Work.Workplaces>();
                    services.AddSingleton<PartyTown.World.Work.Responsibilities>();
                });
            }
        }
    }
}

/// <summary>A clock parked at midday, so nobody in a test is ever asleep.</summary>
public sealed class Noon : TimeProvider
{
    public static readonly Noon Instance = new();
    private static readonly DateTimeOffset Midday = new(2026, 6, 3, 12, 0, 0, TimeSpan.Zero);
    private long _ticks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => Midday.AddTicks(Interlocked.Read(ref _ticks));
}

/// <summary>Polls for something that happens off the call path.</summary>
internal static class TestWait
{
    public static async Task UntilAsync(Func<Task<bool>> done, TimeSpan within)
    {
        var deadline = DateTimeOffset.UtcNow + within;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await done()) return;
            await Task.Delay(200);
        }
        Assert.Fail("Timed out waiting.");
    }
}
