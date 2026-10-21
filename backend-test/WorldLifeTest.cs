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
public class WorldLifeTest : IClassFixture<WorldLifeTest.Fixture>
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
        Assert.Equal("Scripted title", thread.Title);
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

        var a = await clock.EnsurePlayerAsync("Tim");
        var b = await clock.EnsurePlayerAsync("tim");
        var other = await clock.EnsurePlayerAsync("Sam");

        Assert.Equal(a, b);
        Assert.NotEqual(a, other);
        var party = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetParty();
        Assert.Equal(DriverKind.User, party.Participants.Single(p => p.Id == a).Driver);
    }

    /// <summary>Answers the Initiative call by picking the menu line matching <see cref="ChooseOptionContaining"/>.</summary>
    public sealed class ScriptedLlm : IWorldLlm
    {
        public static volatile string? ChooseOptionContaining;
        public static readonly ConcurrentQueue<string> Prompts = new();

        public Task<string?> CompleteAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
            => Task.FromResult<string?>(null);

        public Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null, IReadOnlyList<string>? fields = null)
        {
            Prompts.Enqueue(user);
            if (user.Contains("# Options"))
            {
                var wanted = ChooseOptionContaining;
                var line = wanted is null ? null : user.Split('\n').FirstOrDefault(l => l.Contains(wanted));
                var number = line is null ? "0" : Regex.Match(line, @"^(\d+)\.").Groups[1].Value;
                return Task.FromResult<JsonObject?>(new JsonObject
                {
                    ["thought"] = "scripted thought", ["choice"] = number, ["intent"] = "say hello to everyone",
                });
            }
            return Task.FromResult<JsonObject?>(new JsonObject { ["title"] = "Scripted title", ["body"] = "Scripted body" });
        }
    }

    public sealed class Fixture : IAsyncLifetime
    {
        public TestCluster Cluster { get; private set; } = null!;
        public IGrainFactory GrainFactory => Cluster.GrainFactory;

        public Task InitializeAsync()
        {
            var builder = new TestClusterBuilder(1);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            Cluster = builder.Build();
            return Cluster.DeployAsync();
        }

        public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

        public async Task<(Guid PartyId, Guid Denise)> CreateWorldAsync()
        {
            var partyId = Guid.NewGuid();
            var denise = Guid.NewGuid();
            await GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty)
                .AddPersona(new Persona(denise, "Denise", "You are Denise.", "A hacker."));
            var participants = new List<PartyParticipant> { new() { Id = denise, Name = "Denise", Driver = DriverKind.LLM } };
            await GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty)
                .AddParty(new PartyInfo { Id = partyId, Name = "world", Participants = participants });
            await GrainFactory.GetGrain<IPartyGrain>(partyId).SetParticipants(participants);
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
                    services.AddSingleton(Mock.Of<ISystemOneJudge>(j => j.Enabled == false));
                    services.AddSingleton<IWorldApp, RoomsApp>();
                    services.AddSingleton<IWorldApp, MessengerApp>();
                    services.AddSingleton<IWorldApp, BoardApp>();
                    services.AddSingleton<IWorldApp, DiaryApp>();
                });
            }
        }
    }
}
