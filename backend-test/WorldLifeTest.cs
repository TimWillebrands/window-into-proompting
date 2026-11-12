using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BackendTest.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Orleans.Hosting;
using Orleans.TestingHost;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Apps;

namespace BackendTest;

/// <summary>A whole Life turn (ADR 0018) in a real cluster: perceive Apps → choose → act, with a scripted model.</summary>
public partial class WorldLifeTest : IClassFixture<WorldLifeTest.Fixture>
{
    private readonly Fixture _fx;

    public WorldLifeTest(Fixture fx) => _fx = fx;

    [Fact]
    public async Task LifeTurn_ChoosingBoardPost_WritesThreadAuthoredByPersona()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        ScriptedLlm.ChooseOptionContaining = "Write a new post";

        var activity = await _fx.GrainFactory
            .GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

        Assert.Equal("board", activity.App);
        Assert.Equal("post", activity.Action);
        var threads = await _fx.GrainFactory.GetGrain<IBoardGrain>(partyId).GetThreadsAsync();
        var thread = Assert.Single(threads);
        Assert.Equal(denise, thread.AuthorId);
        Assert.Equal("Scripted post", thread.Body);
        Assert.Equal("Scripted post", thread.Title);
    }

    [Fact]
    public async Task LifeTurn_BoardPostThatIsOnlyANoteAboutWriting_Fizzles_AndPublishesNothing()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        ScriptedLlm.ChooseOptionContaining = "Write a new post";
        ScriptedLlm.Post = "Post something light, in English, under 40 words. Avoid stale words.";
        try
        {
            var activity = await _fx.GrainFactory
                .GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

            Assert.Equal("fizzle", activity.Action);
            Assert.Empty(await _fx.GrainFactory.GetGrain<IBoardGrain>(partyId).GetThreadsAsync());
        }
        finally { ScriptedLlm.Post = "Scripted post"; }
    }

    [Fact]
    public async Task LifeTurn_ChoosingPlan_SchedulesGatheringHostedByPersona()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync(withRoom: true);
        ScriptedLlm.ChooseOptionContaining = "Plan a get-together";
        // Real clock: within 90 min of her bedtime nobody plans (GatheringRules.MayPlan).
        var day = await _fx.GrainFactory.GetGrain<PartyTown.World.Self.IPersonaSelfGrain>(WorldKeys.Life(partyId, denise)).GetDayWindowAsync();
        var (bed, wake) = PartyTown.World.Places.Sleep.Window(denise, day.FirstBeatMinute, day.LastBeatMinute);
        var local = DateTimeOffset.Now;
        if (!GatheringRules.MayPlan(local.Hour * 60 + local.Minute, bed, wake)) return;

        var activity = await _fx.GrainFactory
            .GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

        Assert.Equal("plan", activity.Action);
        var g = Assert.Single(await _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId).GetGatheringsAsync());
        Assert.Equal(denise, g.HostId);
        Assert.Contains(denise, g.AttendeeIds);
        Assert.True(g.StartsAt > DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task DueGathering_StartsOnceAndOnlyWhenDue()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync(withRoom: true);
        var calendar = _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await calendar.PlanAsync(denise, "Denise", "Soon", Guid.NewGuid(), "x", now + 60_000);
        var past = await calendar.PlanAsync(denise, "Denise", "Now", Guid.NewGuid(), "x", now - 1);

        var started = await calendar.StartDueAsync(now);
        var again = await calendar.StartDueAsync(now);

        Assert.Equal(past.Id, Assert.Single(started).Id);
        Assert.Empty(again);
    }

    [Fact]
    public async Task DuePlaceGathering_WalksAttendeesThere()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var places = _fx.GrainFactory.GetGrain<PartyTown.World.Places.IPlacesGrain>(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId)
            .PlanAsync(denise, "Denise", "Coffee", Guid.Empty, cafe.Name, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1, cafe.Id);
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        await clock.UpdateAsync(true, 5, 0);

        await clock.PulseAsync();

        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        // Real clock: at night she sleeps through it instead.
        Assert.True(body.DestPlaceId == cafe.Id || body.PlaceId == cafe.Id || body.Asleep);
        var started = await clock.GetActivitiesAsync(10, null);
        Assert.Contains(started, a => a.Action == "start" && a.Summary.Contains("at the Café"));
    }

    [Fact]
    public async Task LifeTurn_ChoosingZero_Rests()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        ScriptedLlm.ChooseOptionContaining = null;

        var activity = await _fx.GrainFactory
            .GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

        Assert.Equal("rest", activity.Action);
        Assert.Empty(await _fx.GrainFactory.GetGrain<IBoardGrain>(partyId).GetThreadsAsync());
    }

    [Fact]
    public async Task Player_IsCreatedOnceAndSurvivesAsUserDriven()
    {
        var (partyId, _) = await _fx.CreateWorldAsync();
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);

        var a = await clock.ClaimPlayerAsync("user_a", "Sparky");
        var again = await clock.ClaimPlayerAsync("user_a", "Renamed");
        var taken = await clock.ClaimPlayerAsync("user_b", "sparky");

        Assert.Equal(a, again);
        Assert.Null(taken);
        Assert.Equal(a, await clock.FindPlayerAsync("user_a"));
        var party = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetParty();
        Assert.Equal(DriverKind.User, party.Participants.Single(p => p.Id == a).Driver);
    }

    [Fact]
    public async Task PlayerOnAuto_LivesLikeAPersona_AndComesBackUnchanged()
    {
        var (partyId, _) = await _fx.CreateWorldAsync();
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var places = _fx.GrainFactory.GetGrain<PartyTown.World.Places.IPlacesGrain>(partyId);
        var player = (await clock.ClaimPlayerAsync("user_a", "Sparky"))!.Value;
        var auto = _fx.Auto;
        await places.GetSnapshotAsync();
        await places.SetAppearanceAsync(player, new Dictionary<string, string?> { ["hair"] = "red" });
        await places.SetIdleAsync(player, true);
        Assert.Null(await auto.SheetAsync(player));

        await auto.SetAsync(partyId, player, true, "make Denise laugh");

        Assert.True(await _fx.GrainFactory.IsOnAutoAsync(partyId, player));
        Assert.Equal(player, await clock.FindPlayerAsync("user_a"));
        var sheet = await auto.SheetAsync(player);
        Assert.Contains("Sparky", sheet);
        Assert.Equal("make Denise laugh", await auto.MissionAsync(partyId, player));
        Assert.Contains(player, await _fx.GrainFactory.HumansAsync(partyId));
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == player);
        Assert.False(body.IsHuman);
        Assert.Null(body.Activity);
        Assert.Equal("red", body.Appearance!.Hair);
        Assert.True(body.Appearance.Kept);
        Assert.False((await _fx.GrainFactory.GetGrain<PartyTown.World.Work.IOccupationGrain>(WorldKeys.Life(partyId, player)).GetAsync()).Seeded);

        ScriptedLlm.ChooseOptionContaining = "Write a new post";
        var turn = await _fx.GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, player)).LiveAsync(null);
        Assert.Equal("post", turn.Action);
        Assert.Equal(player, Assert.Single(await _fx.GrainFactory.GetGrain<IBoardGrain>(partyId).GetThreadsAsync()).AuthorId);
        Assert.Contains(ScriptedLlm.Prompts, p => p.Contains("Make Denise laugh."));

        await auto.SetAsync(partyId, player, false, null);

        Assert.False(await _fx.GrainFactory.IsOnAutoAsync(partyId, player));
        Assert.Equal(DriverKind.User, (await party.GetParty()).Participants.Single(p => p.Id == player).Driver);
        Assert.True((await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == player).IsHuman);
        Assert.Equal(sheet, await auto.SheetAsync(player));
        Assert.Null(await auto.MissionAsync(partyId, player));
    }

    [Fact]
    public async Task PlayerOnAuto_SheetComesFromPublicTrace_NotDms()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var player = (await clock.ClaimPlayerAsync("user_a", "Sparky"))!.Value;
        await _fx.GrainFactory.GetGrain<IBoardGrain>(partyId).CreateThreadAsync(player, "Sparky", "I love sourdough");
        var dm = await party.GetOrCreateDirectChatAsync(player, denise);
        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).SendNewMessageAsync(player, "my secret", null, null);

        var evidence = await _fx.Auto.EvidenceAsync(partyId, player, "Sparky");
        await _fx.Auto.SetAsync(partyId, player, true, null);

        Assert.Contains("sourdough", evidence);
        Assert.DoesNotContain("my secret", evidence);
        Assert.Contains("Sparky", await _fx.Auto.SheetAsync(player));
    }

    [Fact]
    public async Task Mission_NewOneReplacesTheOld_BlankClearsIt()
    {
        var (partyId, _) = await _fx.CreateWorldAsync();
        var player = (await _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId).ClaimPlayerAsync("user_a", "Sparky"))!.Value;

        await _fx.Auto.SetAsync(partyId, player, true, "find a job");
        await _fx.Auto.SetAsync(partyId, player, true, "find a job");
        await _fx.Auto.SetAsync(partyId, player, true, "learn to bake");

        Assert.Equal("learn to bake", await _fx.Auto.MissionAsync(partyId, player));
        var acquired = (await _fx.GrainFactory.GetGrain<PartyTown.World.Desires.IDesiresGrain>(WorldKeys.Life(partyId, player)).GetAsync()).Acquired;
        Assert.Equal(2, acquired.Count);

        await _fx.Auto.SetAsync(partyId, player, true, " ");

        Assert.Null(await _fx.Auto.MissionAsync(partyId, player));
    }

    [Fact]
    public async Task RemovedPlayer_LeavesNoGatheringBehind()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var calendar = _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId);
        var player = (await _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId).ClaimPlayerAsync("user_a", "Sparky"))!.Value;
        var at = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        await calendar.PlanAsync(player, "Sparky", "Mine", Guid.NewGuid(), "x", at);
        var hers = await calendar.PlanAsync(denise, "Denise", "Hers", Guid.NewGuid(), "x", at);
        await calendar.RsvpAsync(hers.Id, player, true);

        Assert.True(await new PlayerRemoval(_fx.GrainFactory).RemoveAsync(partyId, player));

        var left = Assert.Single(await calendar.GetGatheringsAsync());
        Assert.Equal(hers.Id, left.Id);
        Assert.DoesNotContain(player, left.AttendeeIds);
    }

    [Fact]
    public async Task PlayerOnAuto_CanStillBeRemoved()
    {
        var (partyId, _) = await _fx.CreateWorldAsync();
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var player = (await clock.ClaimPlayerAsync("user_a", "Sparky"))!.Value;
        await _fx.Auto.SetAsync(partyId, player, true, null);

        Assert.True(await new PlayerRemoval(_fx.GrainFactory).RemoveAsync(partyId, player));

        Assert.Null(await clock.FindPlayerAsync("user_a"));
    }

    [Fact]
    public async Task RemovedPlayer_TakesTheirTraceAndCanMakeANewCharacter()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var board = _fx.GrainFactory.GetGrain<IBoardGrain>(partyId);
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var places = _fx.GrainFactory.GetGrain<PartyTown.World.Places.IPlacesGrain>(partyId);
        var player = (await clock.ClaimPlayerAsync("user_a", "Sparky"))!.Value;

        var mine = await board.CreateThreadAsync(player, "Sparky", "mine");
        var hers = await board.CreateThreadAsync(denise, "Denise", "hers");
        await board.ReplyAsync(hers.Id, player, "Sparky", "my reply");
        await board.ReplyAsync(hers.Id, denise, "Denise", "her reply");
        await board.RespectAsync(hers.Id, null, player, "Sparky", true);
        await clock.RecordAsync(new WorldActivity { PersonaId = player, PersonaName = "Sparky", App = "board", Action = "post" });
        await clock.RecordAsync(new WorldActivity { PersonaId = denise, WithPersonaId = player, App = "messenger", Action = "dm" });
        await clock.RecordAsync(new WorldActivity { PersonaId = denise, App = "board", Action = "post" });
        var dm = await party.GetOrCreateDirectChatAsync(player, denise);
        await places.GetSnapshotAsync();

        Assert.True(await new PlayerRemoval(_fx.GrainFactory).RemoveAsync(partyId, player));

        var threads = await board.GetThreadsAsync();
        var kept = Assert.Single(threads);
        Assert.Equal(hers.Id, kept.Id);
        Assert.Equal("her reply", Assert.Single(kept.Replies).Body);
        Assert.Empty(kept.Respect);
        Assert.DoesNotContain(await clock.GetActivitiesAsync(50, null), a => a.PersonaId == player || a.WithPersonaId == player);
        Assert.DoesNotContain(await party.GetChatGroups(), g => g.Id == dm.Id);
        Assert.DoesNotContain((await party.GetParty()).Participants, p => p.Id == player);
        var snapshot = await places.GetSnapshotAsync();
        Assert.DoesNotContain(snapshot.Bodies, b => b.ParticipantId == player);
        Assert.DoesNotContain(snapshot.Places, p => p.OwnerId == player);
        Assert.False(await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).HasPersonaId(player));
        Assert.Null(await clock.FindPlayerAsync("user_a"));
        Assert.NotEqual(player, await clock.ClaimPlayerAsync("user_a", "Sparky"));
    }

    [Fact]
    public async Task Removal_LeavesPersonasAlone()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();

        Assert.False(await new PlayerRemoval(_fx.GrainFactory).RemoveAsync(partyId, denise));

        Assert.Contains((await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetParty()).Participants, p => p.Id == denise);
    }

    /// <summary>Voices the Initiative pick; <see cref="MenuJudge"/> picks the option matching <see cref="ChooseOptionContaining"/>.</summary>
    public sealed class ScriptedLlm : IWorldLlm
    {
        public static volatile string? ChooseOptionContaining;
        public static volatile string Post = "Scripted post";
        public static readonly ConcurrentQueue<string> Prompts = new();

        public Task<string?> CompleteAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
            => Task.FromResult<string?>(null);

        /// <summary>A JSON answer for this system prompt; null falls through to the defaults.</summary>
        public static Func<string, JsonObject?>? Json;

        /// <summary>A JSON answer for this system and user prompt, asked before <see cref="Json"/>.</summary>
        public static Func<string, string, JsonObject?>? JsonWith;

        public Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null, IReadOnlyList<string>? fields = null, IReadOnlyList<string>? flags = null, Func<JsonObject, string?>? refuse = null)
        {
            Prompts.Enqueue(user);
            if (JsonWith?.Invoke(system, user) is { } with) return Task.FromResult<JsonObject?>(with);
            if (Json?.Invoke(system) is { } scripted) return Task.FromResult<JsonObject?>(scripted);
            if (user.Contains("# What you've decided"))
                return Task.FromResult<JsonObject?>(new JsonObject { ["thought"] = "scripted thought", ["intent"] = "say hello to everyone" });
            return Task.FromResult<JsonObject?>(new JsonObject { ["title"] = "Scripted title", ["post"] = Post, ["body"] = "Scripted body", ["respect"] = "no" });
        }
    }

    public sealed class Fixture : IAsyncLifetime
    {
        public TestCluster Cluster { get; private set; } = null!;
        public IGrainFactory GrainFactory => Cluster.GrainFactory;
        public PlayerAuto Auto => new(GrainFactory, new ScriptedLlm(), Microsoft.Extensions.Logging.Abstractions.NullLogger<PlayerAuto>.Instance);

        public Task InitializeAsync()
        {
            var builder = new TestClusterBuilder(1);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            Cluster = builder.Build();
            return Cluster.DeployAsync();
        }

        public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

        public async Task<(Guid PartyId, Guid Denise)> CreateWorldAsync(bool withRoom = false)
        {
            var partyId = Guid.NewGuid();
            var denise = Guid.NewGuid();
            await GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty)
                .AddPersona(new Persona(denise, "Denise", "You are Denise.", "A hacker."));
            var participants = new List<PartyParticipant> { new() { Id = denise, Name = "Denise", Driver = DriverKind.LLM } };
            await GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty)
                .AddParty(new PartyInfo { Id = partyId, Name = "world", Participants = participants });
            if (withRoom)
            {
                var pieter = Guid.NewGuid();
                await GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty)
                    .AddPersona(new Persona(pieter, "Pieter", "You are Pieter.", "An architect."));
                participants.Add(new PartyParticipant { Id = pieter, Name = "Pieter", Driver = DriverKind.LLM });
            }
            await GrainFactory.GetGrain<IPartyGrain>(partyId).SetParticipants(participants);
            if (withRoom)
                await GrainFactory.GetGrain<IPartyGrain>(partyId).CreateChatGroup("kitchen");
            return (partyId, denise);
        }

        private sealed class SiloConfigurator : ISiloConfigurator
        {
            public void Configure(ISiloBuilder siloBuilder)
            {
                siloBuilder.ConfigureDefaults();
                siloBuilder.ConfigureServices(services =>
                {
                    services.AddSingleton<IWorldLlm, ScriptedLlm>();
                    services.AddSingleton(Mock.Of<IStanceConsolidator>());
                    services.AddSingleton<ConsolidationService>();
                    services.AddSingleton<WorldMemory>();
                    services.AddSingleton<ISystemOneJudge>(new MenuJudge(() => ScriptedLlm.ChooseOptionContaining));
                    services.AddSingleton<IWorldApp, RoomsApp>();
                    services.AddSingleton<IWorldApp, MessengerApp>();
                    services.AddSingleton<IWorldApp, BoardApp>();
                    services.AddSingleton<IWorldApp, DiaryApp>();
                    services.AddSingleton<IWorldApp, CalendarApp>();
                    services.AddSingleton<IWorldApp, PartyTown.World.Places.PlacesApp>();
                    services.AddSingleton<PartyTown.World.Work.Workplaces>();
                    services.AddSingleton<PartyTown.World.Work.Responsibilities>();
                });
            }
        }
    }
}
