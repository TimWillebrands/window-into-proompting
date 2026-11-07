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
using PartyTown.World.Work;
using PartyTown.World.Work.Companies;
using PartyTown.World.Work.Markets;
using PartyTown.World.Work.Works;

namespace BackendTest;

/// <summary>A Works Company in a real cluster (ADR 0039), against a fake Runner: Life drives it, the Runner reports it.</summary>
public class WorksWorldTest : IClassFixture<WorksWorldTest.Fixture>
{
    private readonly Fixture _fx;

    public WorksWorldTest(Fixture fx) => _fx = fx;

    private Worksite Site => _fx.Service<Worksite>();
    private FakeRunner Runner => (FakeRunner)_fx.Service<IRunner>();
    private ICommissionsGrain Commissions(Guid partyId) => _fx.GrainFactory.GetGrain<ICommissionsGrain>(partyId);

    private async Task<Company> HireAsync(Guid partyId, Guid who, int start = 0, int end = 23 * 60 + 59)
    {
        var company = (await Site.FoundAsync(partyId, $"The Works {Guid.NewGuid():N}", "keeps the world running"))!;
        await _fx.Service<Desks>().HireAsync(partyId, company.Id, who, "Architect", Shifts.EveryDay, start, end);
        await _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who)).StepAsync(new Noon().GetUtcNow(), asleep: false);
        return company;
    }

    private Task<WorldActivity> LiveAsync(Guid partyId, Guid who, string choose)
    {
        Scripted.Choose = choose;
        return _fx.GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, who)).LiveAsync(null);
    }

    private async Task<Commission> CommissionedAsync(Guid partyId, Guid who)
    {
        await LiveAsync(partyId, who, "Commission a change");
        return Assert.Single(await Commissions(partyId).GetAllAsync(), c => c.Kind == CommissionKind.Change);
    }

    private async Task<Commission> ReadAsync(Guid partyId, Guid id, bool verdicts = false)
    {
        await Site.WatchAsync(partyId, verdicts, CancellationToken.None);
        return (await Commissions(partyId).GetAsync(id))!;
    }

    [Fact]
    public async Task Founding_MakesAWorksCompanyWithNoMarket()
    {
        var (partyId, _, _) = await _fx.CreateWorldAsync();

        var company = (await Site.FoundAsync(partyId, "Gemeentewerken", "keeps the world running"))!;

        Assert.Equal((CompanyKinds.Works, string.Empty), (company.Kind, company.Market));
        Assert.Null(await Site.FoundAsync(partyId, "gemeentewerken", "x"));
    }

    [Fact]
    public async Task Life_CommissioningOnShift_StartsARunWithTheirBuilderAndBudget()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);

        var activity = await LiveAsync(partyId, denise, "Commission a change");

        Assert.Equal(("works", "commission"), (activity.App, activity.Action));
        var commission = Assert.Single(await Commissions(partyId).GetAllAsync());
        Assert.Equal((CommissionStatus.Working, "Denise", company.Id), (commission.Status, commission.ArchitectName, commission.CompanyId));
        var run = Runner.Runs[commission.RunId!];
        Assert.Equal(("dear-one", 3.5, "change"), (run.Model, run.CapUsd, run.Kind));
        Assert.Contains("What should change: Keep the doors shut", run.Prompt);
    }

    [Fact]
    public async Task Run_WaitingOnTheArchitect_BringsTheFactsAndLinesBack()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        var commission = await CommissionedAsync(partyId, denise);

        Runner.Lines[commission.RunId!].Add(new RunnerLine(1, 1, "tool", "write a.cs"));
        Runner.Finish(commission.RunId!, 2, "All done, trust me.");
        var read = await ReadAsync(partyId, commission.Id);

        Assert.Equal((CommissionStatus.Waiting, 2, 1L), (read.Status, read.Files, read.Cursor));
        Assert.Equal("All done, trust me.", read.Report);
        var told = await _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId).GetActivitiesAsync(20, null);
        Assert.Contains(told, a => a.App == "works" && a.Action == "result" && a.Summary.Contains("2 files changed"));
    }

    [Fact]
    public async Task Life_SteeringTheBuilder_SendsTheNextPrompt()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        var commission = await CommissionedAsync(partyId, denise);
        Runner.Finish(commission.RunId!, 1, "done");
        await ReadAsync(partyId, commission.Id);

        var activity = await LiveAsync(partyId, denise, "Tell the builder");

        Assert.Equal("steer", activity.Action);
        Assert.Equal(CommissionStatus.Working, (await Commissions(partyId).GetAsync(commission.Id))!.Status);
        Assert.Equal(("Do it properly", 2d), Runner.Prompts[commission.RunId!].Last());
    }

    [Fact]
    public async Task Life_Proposing_ReachesTheSpiritsAndAMergeIsReadFromTheRunner()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        var commission = await CommissionedAsync(partyId, denise);
        Runner.Finish(commission.RunId!, 1, "done");
        await ReadAsync(partyId, commission.Id);

        await LiveAsync(partyId, denise, "to the world-spirits");
        Assert.Equal(("Mend the doors", "Denise"), (Runner.Proposed[commission.RunId!].Title, Runner.Proposed[commission.RunId!].Author));

        Runner.Propose(commission.RunId!, 7, commission.Id);
        var proposed = await ReadAsync(partyId, commission.Id);
        Assert.Equal((CommissionStatus.Proposed, 7), (proposed.Status, proposed.Proposition));
        Assert.Equal("failed: backend-tests", WorksScreen.ChecksLine(proposed));

        Runner.PropositionStatus[7] = "merged";
        Assert.Equal(CommissionStatus.Accepted, (await ReadAsync(partyId, commission.Id, verdicts: true)).Status);
    }

    [Fact]
    public async Task Rejecting_ReachesStaffWithTheSpiritsNote()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);
        var commission = await CommissionedAsync(partyId, denise);
        Runner.Finish(commission.RunId!, 1, "done");
        await ReadAsync(partyId, commission.Id);
        await LiveAsync(partyId, denise, "to the world-spirits");
        Runner.Propose(commission.RunId!, 8, commission.Id);
        var proposed = await ReadAsync(partyId, commission.Id);

        Assert.True(await Site.RejectAsync(partyId, proposed, "too loud", CancellationToken.None));

        var rejected = (await Commissions(partyId).GetAsync(commission.Id))!;
        Assert.Equal((CommissionStatus.Rejected, "too loud"), (rejected.Status, rejected.Note));
    }

    [Fact]
    public async Task Filing_ASecondChange_IsRefusedWhileOneIsOpen()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);
        await CommissionedAsync(partyId, denise);

        var second = await Site.OrderAsync(partyId, company, new ParticipantView(denise, "Denise", DriverKind.LLM), CommissionKind.Change,
            new CommissionDraft("Another", "why", "brief", "free-one", 1), CancellationToken.None);

        Assert.Null(second);
    }

    [Fact]
    public async Task Survey_RunsReadOnlyBesideWorkWaitingOnTheArchitect()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);
        var commission = await CommissionedAsync(partyId, denise);
        Runner.Finish(commission.RunId!, 1, "done");
        await ReadAsync(partyId, commission.Id);

        var survey = (await Site.OrderAsync(partyId, company, new ParticipantView(denise, "Denise", DriverKind.LLM), CommissionKind.Survey,
            new CommissionDraft("How do doors work?", "why", "How do doors work?", "free-one", 0.25), CancellationToken.None))!;

        Assert.Equal("survey", Runner.Runs[survey.RunId!].Kind);
        Assert.Contains("Change nothing.", Runner.Runs[survey.RunId!].Prompt);
        Runner.Answer(survey.RunId!, "Doors are grains.");
        var read = await ReadAsync(partyId, survey.Id);
        Assert.Equal((CommissionStatus.Answered, "Doors are grains."), (read.Status, read.Report));
    }

    [Fact]
    public async Task Life_AskingTheBuilder_FilesASurvey()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);

        var activity = await LiveAsync(partyId, denise, "Ask the builder");

        Assert.Equal("commission", activity.Action);
        Assert.Equal(CommissionKind.Survey, Assert.Single(await Commissions(partyId).GetAllAsync()).Kind);
    }

    [Fact]
    public async Task Start_TheRunnerRefusing_EndsAsAFailedCommissionNotAnException()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);
        Runner.RefuseStart = true;
        try
        {
            var commission = await Site.OrderAsync(partyId, company, new ParticipantView(denise, "Denise", DriverKind.LLM), CommissionKind.Change,
                new CommissionDraft("Mend", "why", "brief", "free-one", 1), CancellationToken.None);

            Assert.Equal(CommissionStatus.Failed, commission!.Status);
            Assert.Contains("the builder is busy", commission.Error);
        }
        finally { Runner.RefuseStart = false; }
    }

    [Fact]
    public async Task Start_AnAnswerLostOnTheWay_StaysFiledAndIsPickedUpByTheWatch()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var company = await HireAsync(partyId, denise);
        Runner.LoseStartAnswer = true;
        Commission? commission;
        try
        {
            commission = await Site.OrderAsync(partyId, company, new ParticipantView(denise, "Denise", DriverKind.LLM), CommissionKind.Change,
                new CommissionDraft("Mend", "why", "brief", "free-one", 1), CancellationToken.None);
        }
        finally { Runner.LoseStartAnswer = false; }

        Assert.Equal((CommissionStatus.Filed, null), (commission!.Status, commission.RunId));
        var picked = await ReadAsync(partyId, commission.Id);
        Assert.Equal(CommissionStatus.Working, picked.Status);
        Assert.Single(Runner.Commissions.Values, c => c == commission.Id);
    }

    private async Task<AppPerception?> PerceiveAsync(Guid partyId, Guid who)
    {
        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        var cast = await party.GetCastAsync();
        var ctx = new LifeContext(partyId, cast.First(c => c.Id == who), cast, new HashSet<Guid>(), new List<ChatGroupInfo>(), new List<WorldActivity>(),
            new Noon().GetUtcNow(), new Dictionary<Guid, PartyTown.World.Self.AttentionLook>());
        return await _fx.Service<IEnumerable<IWorldApp>>().OfType<WorksApp>().Single().PerceiveAsync(ctx, CancellationToken.None);
    }

    [Fact]
    public async Task Perceive_TheWorksIsASecret_NobodyElseSeesIt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise);

        var staff = await PerceiveAsync(partyId, denise);

        Assert.Contains("this world is made", staff!.Block);
        Assert.Null(await PerceiveAsync(partyId, pieter));
    }

    [Fact]
    public async Task Perceive_OffShift_IsAGlanceWithNothingToDo()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        await HireAsync(partyId, denise, start: 13 * 60, end: 14 * 60);

        var glance = await PerceiveAsync(partyId, denise);

        Assert.Empty(glance!.Affordances);
        Assert.DoesNotContain("this world is made", glance.Block);
    }

    [Fact]
    public async Task Perceive_AWorksArchitect_GetsNoTradingScreen()
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

    public sealed class FakeRunner : IRunner
    {
        public sealed record Started(string Kind, string Prompt, string Model, double CapUsd);

        public readonly ConcurrentDictionary<string, Started> Runs = new();
        public readonly ConcurrentDictionary<string, RunnerRun> State = new();
        public readonly ConcurrentDictionary<string, List<RunnerLine>> Lines = new();
        public readonly ConcurrentDictionary<string, List<(string Text, double AddUsd)>> Prompts = new();
        public readonly ConcurrentDictionary<string, (string Title, string Author)> Proposed = new();
        public readonly ConcurrentDictionary<int, string> PropositionStatus = new();
        public readonly ConcurrentDictionary<int, (string Commission, string? Note)> Known = new();
        public readonly ConcurrentDictionary<string, Guid> Commissions = new();
        public volatile bool RefuseStart;
        public volatile bool LoseStartAnswer;

        public bool Enabled => true;

        public Task<RunnerStatus> StatusAsync(CancellationToken ct) => Task.FromResult(new RunnerStatus(false, 40,
            new List<RunnerModel> { new("free-one", "quick", 0, 0), new("dear-one", "careful", 3, 15) }));

        private static RunnerRun New(string id) => new(id, "working", 1, 0, 0, null, null, null, null, null, new List<RunnerCheck>());

        public Task<RunnerRun> StartAsync(Guid commission, string kind, string prompt, string model, double capUsd, CancellationToken ct)
        {
            if (RefuseStart) throw new RunnerException("409: the builder is busy", 409);
            var known = Runs.Keys.FirstOrDefault(k => Commissions.TryGetValue(k, out var c) && c == commission);
            if (known is not null) return Task.FromResult(State[known]);
            var id = $"r{Guid.NewGuid():N}";
            Runs[id] = new Started(kind, prompt, model, capUsd);
            State[id] = New(id);
            Lines[id] = new List<RunnerLine>();
            Prompts[id] = new List<(string, double)>();
            Commissions[id] = commission;
            if (LoseStartAnswer) throw new RunnerException("The request was canceled due to the configured HttpClient.Timeout");
            return Task.FromResult(State[id]);
        }

        public void Finish(string id, int files, string report)
            => State[id] = State[id] with { Status = "idle", Usd = 0.5, Tokens = 1000, Report = report, Diff = new RunnerDiff(files, 10, 2, new List<string> { "a.cs" }) };

        public void Answer(string id, string report) => State[id] = State[id] with { Status = "done", Report = report };

        public void Propose(string id, int number, Guid commission)
        {
            State[id] = State[id] with { Status = "proposed", Proposition = number, Checks = new List<RunnerCheck> { new("backend-build", true, 20, string.Empty), new("backend-tests", false, 40, "Failed: 1") } };
            PropositionStatus[number] = "open";
            Known[number] = (commission.ToString(), null);
        }

        public Task<RunnerRead> ReadAsync(string runId, long after, CancellationToken ct)
            => Task.FromResult(new RunnerRead(Lines[runId].Where(l => l.N > after).ToList(), State[runId]));

        public Task<RunnerRun> PromptAsync(string runId, string text, double addUsd, CancellationToken ct)
        {
            Prompts[runId].Add((text, addUsd));
            State[runId] = State[runId] with { Status = "working" };
            return Task.FromResult(State[runId]);
        }

        public Task<RunnerRun> ProposeAsync(string runId, string title, string why, string author, CancellationToken ct)
        {
            Proposed[runId] = (title, author);
            State[runId] = State[runId] with { Status = "checking" };
            return Task.FromResult(State[runId]);
        }

        public Task<RunnerRun> DropAsync(string runId, CancellationToken ct)
        {
            State[runId] = State[runId] with { Status = "dropped" };
            return Task.FromResult(State[runId]);
        }

        public Task<List<RunnerProposition>> PropositionsAsync(CancellationToken ct) => Task.FromResult(Known
            .Select(k => new RunnerProposition(k.Key, k.Value.Commission, "t", "b", PropositionStatus[k.Key], k.Value.Note)).ToList());

        public Task RejectAsync(int proposition, string? note, CancellationToken ct)
        {
            PropositionStatus[proposition] = "rejected";
            Known[proposition] = (Known[proposition].Commission, note);
            return Task.CompletedTask;
        }

        public Task<string> PatchAsync(int proposition, CancellationToken ct) => Task.FromResult("From 0000");
    }

    /// <summary>Picks the menu line matching <see cref="Choose"/>, and voices the desk's structured calls.</summary>
    public sealed class Scripted : IWorldLlm
    {
        public static volatile string? Choose;

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
            return Task.FromResult<JsonObject?>(new JsonObject
            {
                ["title"] = "Mend the doors", ["why"] = "They bang", ["brief"] = "Keep the doors shut", ["builder"] = "dear-one", ["budget"] = "3.5",
                ["question"] = "How do doors work?", ["message"] = "Do it properly", ["add_budget"] = "2",
            });
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
                    services.AddSingleton<IRunner, FakeRunner>();
                    services.AddSingleton<Worksite>();
                    services.AddSingleton<IWorldApp, WorksApp>();
                    services.AddSingleton<IWorldApp, DeskApp>();
                });
            }
        }
    }
}
