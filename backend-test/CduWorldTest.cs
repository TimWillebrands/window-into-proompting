using System.Collections.Concurrent;
using System.Text.Json;
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
using PartyTown.World.Work;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;
using PartyTown.World.Work.Markets;

namespace BackendTest;

/// <summary>A CDU Company in a real cluster (ADR 0039), against a fake CDU: Life moves it, the CDU reports, shifts drive the tide.</summary>
public class CduWorldTest : IClassFixture<CduWorldTest.Fixture>
{
    private readonly Fixture _fx;

    public CduWorldTest(Fixture fx) => _fx = fx;

    private Cytheurgy Site => _fx.Service<Cytheurgy>();
    private FakeCdu Cdu => (FakeCdu)_fx.Service<ICdu>();
    private ICduGrain Grain(Guid partyId) => _fx.GrainFactory.GetGrain<ICduGrain>(partyId);

    private static DateTimeOffset At(int hour) => new(2026, 11, 2, hour, 0, 0, TimeSpan.Zero);

    private async Task<Company> HireAsync(Guid partyId, Guid who, int start = 0, int end = 23 * 60 + 59)
    {
        var company = (await Site.FoundAsync(partyId, $"Vormwerk {Guid.NewGuid():N}", "keeps the Forms"))!;
        await _fx.Service<Desks>().HireAsync(partyId, company.Id, who, "Cytheurgist", Shifts.EveryDay, start, end);
        await StepAsync(partyId, who, 12);
        return company;
    }

    private Task StepAsync(Guid partyId, Guid who, int hour)
        => _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who)).StepAsync(At(hour), asleep: false);

    private Task<WorldActivity> LiveAsync(Guid partyId, Guid who, string choose)
    {
        Scripted.Choose = choose;
        return _fx.GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, who)).LiveAsync(null);
    }

    private Task WatchAsync(Guid partyId) => Site.WatchAsync(partyId, CancellationToken.None);

    private async Task<List<WorldActivity>> ToldAsync(Guid partyId)
        => (await _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId).GetActivitiesAsync(50, null)).Where(a => a.App == CduScreen.AppId).ToList();

    [Fact]
    public async Task Founding_MakesACduCompanyWithNoMarket()
    {
        var (partyId, _, _) = await _fx.CreateWorldAsync();

        var company = (await Site.FoundAsync(partyId, "Vormwerk", "keeps the Forms"))!;

        Assert.Equal((CompanyKinds.Cdu, string.Empty), (company.Kind, company.Market));
        Assert.Null(await Site.FoundAsync(partyId, "vormwerk", "x"));
    }

    [Fact]
    public async Task Life_TalkingOnShift_ReachesTheCduAsThem()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);

        var activity = await LiveAsync(partyId, denise, "Tell the CDU");

        Assert.Equal(("cdu", "talk"), (activity.App, activity.Action));
        var talk = Assert.Single(Cdu.Talks, t => t.From.Id == denise.ToString());
        Assert.Equal(("Denise", "Keep the doors shut", "talk", "big-one"), (talk.From.Name, talk.Text, talk.Kind, talk.Model));
        var said = Assert.Single((await Grain(partyId).GetAsync()).Exchanges);
        Assert.Equal((CduRules.Queued, denise, "big-one"), (said.Status, said.SpeakerId, said.Model));
    }

    [Fact]
    public async Task News_AReply_ReachesOnlyTheSpeaker_AndTheirWordsAreAnswered()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);
        await _fx.Service<Desks>().HireAsync(partyId, company.Id, pieter, "Cytheurgist", Shifts.EveryDay, 0, 23 * 60 + 59);
        await LiveAsync(partyId, denise, "Tell the CDU");
        var turn = Cdu.Talks.Last(t => t.From.Id == denise.ToString());

        Cdu.Tell("replied", "I moved two Forms.\n\nWhich room first?", new { turn = turn.Id, asked = true, commits = 2 });
        await WatchAsync(partyId);
        await WatchAsync(partyId);

        var said = Assert.Single((await Grain(partyId).GetAsync()).Exchanges);
        Assert.Equal((CduRules.Done, true, 2), (said.Status, said.Asked, said.Commits));
        var told = Assert.Single(await ToldAsync(partyId), a => a.Action == "replied");
        Assert.Equal("Denise", told.PersonaName);
        Assert.Contains("moved the Forms: 2 commits on the trunk", told.Summary);
    }

    [Fact]
    public async Task Life_AnOpenQuestion_IsAnswered()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        await LiveAsync(partyId, denise, "Tell the CDU");
        Cdu.Tell("replied", "Which room first?", new { turn = Cdu.Talks.Last(t => t.From.Id == denise.ToString()).Id, asked = true, commits = 0 });
        await WatchAsync(partyId);

        Scripted.Message = "The kitchen";
        try
        {
            var activity = await LiveAsync(partyId, denise, "Answer the CDU");
            Assert.Equal("answer", activity.Action);
        }
        finally { Scripted.Message = "Keep the doors shut"; }

        var answer = Cdu.Talks.Last(t => t.From.Id == denise.ToString());
        Assert.Equal(("answer", "The kitchen"), (answer.Kind, answer.Text));
        Assert.Null(CduRules.OpenQuestion((await Grain(partyId).GetAsync()).Exchanges));
    }

    [Fact]
    public async Task Life_Looking_TakesInTheTrunkAndTheAgora()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        Cdu.Ahead = new List<CduCommit> { new("s1", "fix: shut the doors", "Denise") };
        Cdu.Pr = new CduPr(9, "u", "fix: shut the doors", "OPEN", "s1", "failing");
        try
        {
            var activity = await LiveAsync(partyId, denise, "Look at what the CDU");

            Assert.Equal("look", activity.Action);
            Assert.Contains("\"fix: shut the doors\" for Denise", activity.Summary);
            Assert.Contains("its checks fail", activity.Summary);
        }
        finally { (Cdu.Ahead, Cdu.Pr) = (new List<CduCommit>(), null); }
    }

    [Fact]
    public async Task Shifts_StartAndEnd_DriveTheTide()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise, start: 9 * 60, end: 17 * 60);
        var (tidesIn, tidesOut) = (Cdu.TidesIn, Cdu.TidesOut);

        await WatchAsync(partyId);
        await WatchAsync(partyId);
        Assert.Equal((tidesIn + 1, tidesOut), (Cdu.TidesIn, Cdu.TidesOut));

        await StepAsync(partyId, denise, 18);
        await WatchAsync(partyId);
        Assert.Equal((tidesIn + 1, tidesOut + 1), (Cdu.TidesIn, Cdu.TidesOut));
    }

    [Fact]
    public async Task Tide_ACduThatDidntAnswer_GetsTheEdgeAgain()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        var tidesIn = Cdu.TidesIn;
        Cdu.Dark = true;
        try { await WatchAsync(partyId); }
        finally { Cdu.Dark = false; }

        await WatchAsync(partyId);

        Assert.Equal(tidesIn + 1, Cdu.TidesIn);
        Assert.True((await Grain(partyId).GetAsync()).OnShift);
    }

    [Fact]
    public async Task News_AVerdict_ReachesAllStaff()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);
        await _fx.Service<Desks>().HireAsync(partyId, company.Id, pieter, "Cytheurgist", Shifts.EveryDay, 0, 23 * 60 + 59);
        await WatchAsync(partyId);

        Cdu.Tell("merged", "fix: shut the doors", new { number = 9 });
        await WatchAsync(partyId);

        var told = Assert.Single(await ToldAsync(partyId), a => a.Action == "merged");
        Assert.Equal("the CDU", told.PersonaName);
        Assert.Contains("The One merged PR #9", told.Summary);
    }

    [Fact]
    public async Task News_APartyNewToTheCdu_StartsFromNow()
    {
        Cdu.Tell("closed", "feat: an old idea", new { number = 1 });
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);

        await WatchAsync(partyId);

        Assert.DoesNotContain(await ToldAsync(partyId), a => a.Action == "closed");
        Assert.True((await Grain(partyId).GetAsync()).News > 0);
    }

    private async Task<AppPerception?> PerceiveAsync(Guid partyId, Guid who)
    {
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var ctx = new LifeContext(partyId, cast.First(c => c.Id == who), cast, new HashSet<Guid>(), new List<ChatGroupInfo>(), new List<WorldActivity>(),
            new Noon().GetUtcNow(), new Dictionary<Guid, PartyTown.World.Self.AttentionLook>());
        return await _fx.Service<IEnumerable<IWorldApp>>().OfType<CduApp>().Single().PerceiveAsync(ctx, CancellationToken.None);
    }

    [Fact]
    public async Task Perceive_TheCduIsASecret_NobodyElseSeesIt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);

        var staff = await PerceiveAsync(partyId, denise);

        Assert.Contains("Forms are code", staff!.Block);
        Assert.Null(await PerceiveAsync(partyId, pieter));
    }

    [Fact]
    public async Task Perceive_OffShift_IsAGlanceWithNothingToDo()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise, start: 13 * 60, end: 14 * 60);

        var glance = await PerceiveAsync(partyId, denise);

        Assert.Empty(glance!.Affordances);
        Assert.DoesNotContain("Forms", glance.Block);
    }

    [Fact]
    public async Task Perceive_ACytheurgistAtWork_GetsTheDeskNotABareGetOnWithIt()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var ctx = new LifeContext(partyId, cast.First(c => c.Id == denise), cast, new HashSet<Guid>(), new List<ChatGroupInfo>(), new List<WorldActivity>(),
            new Noon().GetUtcNow(), new Dictionary<Guid, PartyTown.World.Self.AttentionLook>());

        var work = await _fx.Service<IEnumerable<IWorldApp>>().OfType<WorkApp>().Single().PerceiveAsync(ctx, CancellationToken.None);

        Assert.DoesNotContain(work!.Affordances, a => a.Verb == "work");
        Assert.Contains(work.Affordances, a => a.Verb == "leave");
        Assert.Contains((await PerceiveAsync(partyId, denise))!.Affordances, a => a.Verb == CduScreen.VerbTalk && a.TargetId == company.Id);
    }

    [Fact]
    public async Task Perceive_ACytheurgist_GetsNoTradingScreen()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var ctx = new LifeContext(partyId, cast.First(c => c.Id == denise), cast, new HashSet<Guid>(), new List<ChatGroupInfo>(), new List<WorldActivity>(),
            new Noon().GetUtcNow(), new Dictionary<Guid, PartyTown.World.Self.AttentionLook>());

        var desk = _fx.Service<IEnumerable<IWorldApp>>().OfType<DeskApp>().Single();

        Assert.Null(await desk.PerceiveAsync(ctx, CancellationToken.None));
    }

    public sealed class FakeCdu : ICdu
    {
        public sealed record Talked(string Id, CduSpeaker From, string Text, string Kind, string? Model);

        public readonly ConcurrentQueue<Talked> Talks = new();
        private readonly List<CduEntry> _news = new();
        private int _tidesIn;
        private int _tidesOut;
        public volatile bool Dark;
        public List<CduCommit> Ahead = new();
        public CduPr? Pr;

        public int TidesIn => _tidesIn;
        public int TidesOut => _tidesOut;
        public bool Enabled => true;

        public void Tell(string kind, string text, object data)
        {
            lock (_news) _news.Add(new CduEntry(_news.Count + 1, 1, kind, text, JsonSerializer.SerializeToElement(data)));
        }

        private void Reachable()
        {
            if (Dark) throw new CduException("connection refused");
        }

        public Task<CduStatus> StatusAsync(CancellationToken ct)
        {
            Reachable();
            return Task.FromResult(new CduStatus(false, null, 0, "careful", new CduTrunk("h", Ahead), Pr, new CduAllowance(0.5, 5), 0, _news.Count, null,
                new List<CduModel> { new("free-one", "free preview", "free", 0, 0), new("big-one", "capable", "capable", 1, 4) }));
        }

        public Task<CduTurn> TalkAsync(CduSpeaker from, string text, string kind, string? model, CancellationToken ct)
        {
            Reachable();
            var turn = new Talked($"t{Guid.NewGuid():N}", from, text, kind, model);
            Talks.Enqueue(turn);
            return Task.FromResult(new CduTurn(turn.Id, from, text, kind, "queued", 1, null, null, null, new List<CduCommit>(), null, null));
        }

        public Task<List<CduEntry>> LinesAsync(long after, CancellationToken ct)
        {
            Reachable();
            return Task.FromResult(new List<CduEntry>());
        }

        public Task<List<CduEntry>> NewsAsync(long after, CancellationToken ct)
        {
            Reachable();
            lock (_news) return Task.FromResult(_news.Where(n => n.N > after).ToList());
        }

        public Task TideInAsync(CancellationToken ct)
        {
            Reachable();
            Interlocked.Increment(ref _tidesIn);
            return Task.CompletedTask;
        }

        public Task TideOutAsync(CancellationToken ct)
        {
            Reachable();
            Interlocked.Increment(ref _tidesOut);
            return Task.CompletedTask;
        }
    }

    /// <summary>Picks the menu line matching <see cref="Choose"/>, and voices the desk's structured calls.</summary>
    public sealed class Scripted : IWorldLlm
    {
        public static volatile string? Choose;
        public static volatile string Message = "Keep the doors shut";
        public static volatile string Mind = "big-one";

        public Task<string?> CompleteAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
            => Task.FromResult<string?>(null);

        public Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null, IReadOnlyList<string>? fields = null)
        {
            if (user.Contains("# Options"))
            {
                var wanted = Choose;
                var line = wanted is null ? null : user.Split('\n').FirstOrDefault(l => l.Contains(wanted));
                var number = line is null ? "0" : Regex.Match(line, @"^(\d+)\.").Groups[1].Value;
                return Task.FromResult<JsonObject?>(new JsonObject { ["thought"] = "scripted", ["choice"] = number, ["intent"] = "get on with it" });
            }
            return Task.FromResult<JsonObject?>(new JsonObject { ["message"] = Message, ["mind"] = Mind });
        }

        public IWorldLlm For(Guid personaId) => this;
    }

    private sealed class Noon : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    public sealed class Fixture : IAsyncLifetime
    {
        public TestCluster Cluster { get; private set; } = null!;

        public IGrainFactory GrainFactory => Cluster.GrainFactory;

        public T Service<T>() where T : notnull => Cluster.GetSiloServiceProvider().GetRequiredService<T>();

        public Task InitializeAsync()
        {
            var builder = new TestClusterBuilder(1);
            builder.AddSiloBuilderConfigurator<SiloConfigurator>();
            Cluster = builder.Build();
            return Cluster.DeployAsync();
        }

        public Task DisposeAsync() => Cluster.DisposeAsync().AsTask();

        public async Task<(Guid PartyId, Guid Denise, Guid Pieter)> CreateWorldAsync()
        {
            var (partyId, denise, pieter) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var personas = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
            await personas.AddPersona(new Persona(denise, "Denise", "You are Denise.", "A hacker."));
            await personas.AddPersona(new Persona(pieter, "Pieter", "You are Pieter.", "An architect."));
            var participants = new List<PartyParticipant>
            {
                new() { Id = denise, Name = "Denise", Driver = DriverKind.LLM },
                new() { Id = pieter, Name = "Pieter", Driver = DriverKind.LLM },
            };
            await GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty).AddParty(new PartyInfo { Id = partyId, Name = "world", Participants = participants });
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
                    services.AddSingleton<TimeProvider, Noon>();
                    services.AddSingleton<IWorldLlm, Scripted>();
                    services.AddSingleton(Mock.Of<IStanceConsolidator>());
                    services.AddSingleton<ConsolidationService>();
                    services.AddSingleton<WorldMemory>();
                    services.AddSingleton(Mock.Of<ISystemOneJudge>(j => j.Enabled == false));
                    services.AddSingleton<Workplaces>();
                    services.AddSingleton<Responsibilities>();
                    services.AddSingleton<MarketFeeds>();
                    services.AddSingleton<Desks>();
                    services.AddSingleton<ICdu, FakeCdu>();
                    services.AddSingleton<Cytheurgy>();
                    services.AddSingleton<IWorldApp, CduApp>();
                    services.AddSingleton<IWorldApp, DeskApp>();
                    services.AddSingleton<IWorldApp, WorkApp>();
                });
            }
        }
    }
}
