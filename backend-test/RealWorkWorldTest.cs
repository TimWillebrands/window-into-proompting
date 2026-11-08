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
using PartyTown.World.Economy;
using PartyTown.World.Work;
using PartyTown.World.Work.Companies;
using PartyTown.World.Work.Markets;
using PartyTown.World.Work.Practices;

namespace BackendTest;

/// <summary>Companies and Practices in a real cluster (ADR 0030), at noon, with a human in the cast.</summary>
public class RealWorkWorldTest : IClassFixture<RealWorkWorldTest.Fixture>
{
    private readonly Fixture _fx;

    public RealWorkWorldTest(Fixture fx) => _fx = fx;

    private static readonly DateTimeOffset Later = DateTimeOffset.UtcNow.AddMinutes(10);

    private async Task<Company> FoundAsync(Guid partyId, string name, string instrument)
    {
        FakeMarket.Quotes[instrument] = Rain(instrument);
        FakeMarket.Search = instrument;
        return (await _fx.Service<Desks>().FoundAsync(partyId, name, "trades rain", MarketKinds.Predictions, instrument, 10_000))!;
    }

    private static Quote Rain(string id, double? settled = null) => new()
    {
        Id = id, Title = "Will it rain? (Yes)", Bid = 0.40, Ask = 0.42, Open = settled is null, Settled = settled,
    };

    private Task<OccupationView?> HireAsync(Guid partyId, Guid company, Guid who)
        => _fx.Service<Desks>().HireAsync(partyId, company, who, "trader", Shifts.EveryDay, 0, 23 * 60 + 59);

    private ICompaniesGrain Companies(Guid partyId) => _fx.GrainFactory.GetGrain<ICompaniesGrain>(partyId);

    private IWalletGrain Wallet(Guid partyId, Guid who) => _fx.GrainFactory.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, who));

    [Fact]
    public async Task Founding_OpensAnOfficeAndWatchesTheMarket()
    {
        var (partyId, _, _, _) = await _fx.CreateWorldAsync();

        var company = await FoundAsync(partyId, "Polder Capital", "found-1");

        Assert.Equal("found-1", Assert.Single(company.Watchlist).Id);
        Assert.Equal(10_000, company.Book.Value);
        var places = (await _fx.GrainFactory.GetGrain<PartyTown.World.Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places;
        Assert.Contains(places, p => p.Name == company.Workplace && p.Kind == PartyTown.World.Places.PlaceKinds.Public);
    }

    [Fact]
    public async Task Founding_TheSameNameTwice_IsRefused()
    {
        var (partyId, _, _, _) = await _fx.CreateWorldAsync();
        await FoundAsync(partyId, "Polder Capital", "found-2");

        Assert.Null(await _fx.Service<Desks>().FoundAsync(partyId, "polder capital", "x", MarketKinds.Predictions, null, null));
    }

    [Fact]
    public async Task Hiring_LinksTheJobAndEditingHoursKeepsIt()
    {
        var (partyId, denise, _, _) = await _fx.CreateWorldAsync();
        var company = await FoundAsync(partyId, "Polder Capital", "hire-1");

        await HireAsync(partyId, company.Id, denise);
        var occupation = _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise));
        var edited = await occupation.SetAsync(new Occupation { Title = "senior trader", Workplace = company.Workplace, Days = Shifts.Weekdays });

        Assert.Equal(company.Id, edited.Job!.CompanyId);
        Assert.Equal("Denise", Assert.Single(await _fx.Service<Desks>().StaffAsync(partyId, company.Id)).Name);
    }

    [Fact]
    public async Task Trade_MovesTheBookAndNeverTheWallet()
    {
        var (partyId, denise, _, _) = await _fx.CreateWorldAsync();
        var company = await FoundAsync(partyId, "Polder Capital", "trade-1");
        await HireAsync(partyId, company.Id, denise);
        var wallet = (await Wallet(partyId, denise).GetAsync()).Balance;

        var result = await _fx.Service<Desks>().TradeAsync(partyId, company.Id, new TradeOrder("trade-1", TradeSide.Buy, 0.1, denise, "Denise", "wet", 0.7));

        Assert.Equal(0.42, result.Trade!.Price);
        var book = (await Companies(partyId).GetAsync(company.Id))!.Book;
        Assert.Equal(9_000 - 1, book.Cash, 6);
        Assert.Equal("Will it rain? (Yes)", Assert.Single(book.Positions).Title);
        Assert.Equal(wallet, (await Wallet(partyId, denise).GetAsync()).Balance);
    }

    [Fact]
    public async Task Trade_WithTheMarketDown_IsRefused()
    {
        var (partyId, _, _, _) = await _fx.CreateWorldAsync();
        var company = await FoundAsync(partyId, "Polder Capital", "down-1");
        FakeMarket.Quotes.TryRemove("down-1", out _);

        var result = await _fx.Service<Desks>().TradeAsync(partyId, company.Id, new TradeOrder("down-1", TradeSide.Buy, 0.1, null, null, null, null));

        Assert.Equal("The screens are down.", result.Refusal);
    }

    [Fact]
    public async Task Settlement_PaysTheBookScoresTheForecastAndTellsTheFloor()
    {
        var (partyId, denise, _, _) = await _fx.CreateWorldAsync();
        var company = await FoundAsync(partyId, "Polder Capital", "settle-1");
        await HireAsync(partyId, company.Id, denise);
        var desks = _fx.Service<Desks>();
        await desks.TradeAsync(partyId, company.Id, new TradeOrder("settle-1", TradeSide.Buy, 0.1, denise, "Denise", "wet", 0.8));
        var wallet = (await Wallet(partyId, denise).GetAsync()).Balance;

        FakeMarket.Quotes["settle-1"] = Rain("settle-1", settled: 1);
        await desks.RefreshAsync(partyId, Later);

        var book = (await Companies(partyId).GetAsync(company.Id))!.Book;
        Assert.Empty(book.Positions);
        Assert.True(book.Value > 10_000);
        Assert.Equal(0.04, BookRules.ScoreOf(book, denise)!.Value, 6);
        Assert.Equal(wallet, (await Wallet(partyId, denise).GetAsync()).Balance);
        var log = await _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId).GetActivitiesAsync(10, null);
        Assert.Contains(log, a => a.App == DeskApp.AppId && a.Action == "result" && a.Summary.Contains("settled for Polder Capital"));
    }

    [Fact]
    public async Task Refresh_AfterAFailedRead_LeavesThePositionOpenAndStale()
    {
        var (partyId, denise, _, _) = await _fx.CreateWorldAsync();
        var company = await FoundAsync(partyId, "Polder Capital", "stale-1");
        var desks = _fx.Service<Desks>();
        await desks.TradeAsync(partyId, company.Id, new TradeOrder("stale-1", TradeSide.Buy, 0.1, denise, "Denise", null, null));

        FakeMarket.Quotes.TryRemove("stale-1", out _);
        await desks.RefreshAsync(partyId, Later);

        Assert.False(Assert.Single((await Companies(partyId).GetAsync(company.Id))!.Book.Positions).Fresh);
    }

    [Fact]
    public async Task Life_OnShift_TradesAndTheOutcomeComesFromTheBook()
    {
        var (partyId, denise, _, _) = await _fx.CreateWorldAsync();
        var company = await FoundAsync(partyId, "Polder Capital", "life-1");
        await HireAsync(partyId, company.Id, denise);
        var occupation = _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise));
        await occupation.StepAsync(DateTimeOffset.Now, asleep: false);
        Assert.True((await occupation.GetAsync()).OnShift);
        Scripted.Choose = "Make a trade";

        var activity = await _fx.GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

        Assert.Equal("trade", activity.Action);
        Assert.Contains("bought", activity.Summary);
        var trade = Assert.Single((await Companies(partyId).GetAsync(company.Id))!.Book.Trades);
        Assert.Equal("scripted thesis", trade.Thesis);
        Assert.Equal(0.7, trade.Forecast);
    }

    private async Task PractiseAsync(Guid partyId, Guid who, int fee = 40, string workplace = "Pieter's practice")
        => await _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who)).SetAsync(new Occupation
        {
            Title = "career coach", Workplace = workplace, Days = Shifts.EveryDay, Start = 0, End = 23 * 60 + 59,
            Practice = new Practice { Field = "logistics", Approach = "blunt", Fee = fee },
        });

    private Sessions Booking => _fx.Service<Sessions>();

    private ISessionsGrain Book(Guid partyId) => _fx.GrainFactory.GetGrain<ISessionsGrain>(partyId);

    [Fact]
    public async Task Booking_HasTheSameRulesForEveryone()
    {
        var (partyId, denise, pieter, player) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        var now = DateTimeOffset.UtcNow;

        Assert.Equal("You can't book yourself.", (await Booking.BookAsync(partyId, pieter, pieter, "x", now)).Refusal);
        Assert.Equal("They don't take clients.", (await Booking.BookAsync(partyId, pieter, denise, "x", now)).Refusal);
        var human = await Booking.BookAsync(partyId, player, pieter, "stuck", now);
        var persona = await Booking.BookAsync(partyId, denise, pieter, "stuck too", now);
        Assert.NotNull(human.Session);
        Assert.NotNull(persona.Session);
        Assert.NotEqual(human.Session!.StartsAt, persona.Session!.StartsAt);
        Assert.Equal("They already have a session booked.", (await Booking.BookAsync(partyId, player, pieter, "again", now)).Refusal);
    }

    [Fact]
    public async Task Booking_ClientWhoCantPay_IsRefused()
    {
        var (partyId, denise, pieter, _) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter, fee: 500);
        await Wallet(partyId, denise).SpendAsync((await Wallet(partyId, denise).GetAsync()).Balance, "gone");

        Assert.Contains("can't afford", (await Booking.BookAsync(partyId, denise, pieter, "x", DateTimeOffset.UtcNow)).Refusal);
    }

    private PartyTown.World.Places.IPlacesGrain Places(Guid partyId) => _fx.GrainFactory.GetGrain<PartyTown.World.Places.IPlacesGrain>(partyId);

    /// <summary>Books, starts and lets the walking finish; a human walks over by hand, as they would.</summary>
    private async Task<PracticeSession> RunUntilStartedAsync(Guid partyId, Guid client, Guid practitioner, bool clientWalksOver = true)
    {
        var (session, refusal) = await Booking.BookAsync(partyId, client, practitioner, "stuck at work", DateTimeOffset.UtcNow);
        Assert.Null(refusal);
        var due = DateTimeOffset.FromUnixTimeMilliseconds(session!.StartsAt).AddSeconds(1);
        await Booking.StepAsync(partyId, due);
        Noon.Advance(TimeSpan.FromMinutes(2));
        if (clientWalksOver) await Places(partyId).JoinAsync(client, practitioner);
        Noon.Advance(TimeSpan.FromMinutes(2));
        await Places(partyId).GetAsleepAsync();
        await Booking.StepAsync(partyId, due.AddSeconds(10));
        var started = (await Book(partyId).GetSessionsAsync()).Single(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Running, started.Status);
        return started;
    }

    private Task SayAsync(PracticeSession session, Guid who, string text)
        => _fx.GrainFactory.GetGrain<IChatGroupGrain>(session.TalkRoomId!.Value).SendNewMessageAsync(who, text, null, null);

    private static DateTimeOffset Over(PracticeSession session)
        => DateTimeOffset.FromUnixTimeMilliseconds(session.StartedAt!.Value).Add(PracticeRules.Length).AddMinutes(1);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Session_IsOrdinaryPlaceTalk_SoEarshotDecidesWhoHearsIt(bool atTheirSpot)
    {
        var (partyId, denise, pieter, player) = await _fx.CreateWorldAsync();
        // An office: its desks have room for a third person, a clinic's reception doesn't.
        await PractiseAsync(partyId, pieter, workplace: "Pieter's office");
        var session = await RunUntilStartedAsync(partyId, player, pieter);
        Assert.True(session.Opened);
        var room = (await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetChatGroups()).Single(g => g.Id == session.TalkRoomId);
        Assert.Equal(ChatGroupKinds.Place, room.Kind);

        var coach = (await Places(partyId).GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == pieter);
        // An office has a door (ADR 0041): the one overhearing is someone it lets in.
        await Places(partyId).SetAccessAsync(coach.PlaceId, PartyTown.World.Places.PlaceAccess.Staff, new List<Guid> { denise });
        if (atTheirSpot) await Places(partyId).JoinAsync(denise, pieter);
        else await Places(partyId).MoveAsync(denise, coach.PlaceId, "window");
        Noon.Advance(TimeSpan.FromMinutes(2));
        await Places(partyId).GetAsleepAsync();
        var said = await _fx.GrainFactory.GetGrain<IChatGroupGrain>(session.TalkRoomId!.Value).SendNewMessageAsync(player, "I lied on my CV.", null, null);

        Assert.Equal(atTheirSpot, said.HeardBy!.Contains(denise));
    }

    [Fact]
    public async Task Session_AtAnOffscreenWorkplace_OpensItAsAGatheringWould()
    {
        var (partyId, denise, pieter, _) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        var where = (await _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, pieter)).GetAsync()).Job!.LocaleId;
        Assert.NotNull(where);

        var session = await RunUntilStartedAsync(partyId, denise, pieter, clientWalksOver: false);

        var snapshot = await Places(partyId).GetSnapshotAsync();
        Assert.Equal(PartyTown.World.Places.PlaceKinds.Public, snapshot.Places.Single(p => p.Id == where).Kind);
        Assert.True(session.Opened);
        var client = snapshot.Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(where, client.PlaceId);
    }

    [Fact]
    public async Task Session_ClientNeverShows_IsMissedAndFree()
    {
        var (partyId, _, pieter, player) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        var session = await RunUntilStartedAsync(partyId, player, pieter, clientWalksOver: false);
        var balance = (await Wallet(partyId, player).GetAsync()).Balance;

        Assert.False(session.Opened);
        await Booking.StepAsync(partyId, Over(session));

        Assert.Equal(SessionStatus.Missed, (await Book(partyId).GetSessionsAsync()).Single(s => s.Id == session.Id).Status);
        Assert.Equal(balance, (await Wallet(partyId, player).GetAsync()).Balance);
    }

    [Fact]
    public async Task Session_ThatTookPlace_ChargesOnceLeavesHomeworkAndHoldsTheClientToIt()
    {
        var (partyId, _, pieter, player) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        var session = await RunUntilStartedAsync(partyId, player, pieter);
        var (clientBefore, coachBefore) = ((await Wallet(partyId, player).GetAsync()).Balance, (await Wallet(partyId, pieter).GetAsync()).Balance);

        await SayAsync(session, player, "I can't get an interview.");
        await SayAsync(session, pieter, "Send your CV to Bol by Friday.");
        await Booking.StepAsync(partyId, Over(session));
        await Booking.StepAsync(partyId, Over(session).AddMinutes(1));

        var done = (await Book(partyId).GetSessionsAsync()).Single(s => s.Id == session.Id);
        Assert.Equal(SessionStatus.Done, done.Status);
        Assert.True(done.Paid);
        Assert.Equal(clientBefore - 40, (await Wallet(partyId, player).GetAsync()).Balance);
        Assert.Equal(coachBefore + 40, (await Wallet(partyId, pieter).GetAsync()).Balance);
        var homework = Assert.Single(await Book(partyId).GetHomeworkAsync());
        Assert.Equal("Send the CV to Bol", homework.Text);
        var owed = await _fx.Service<Responsibilities>().RenderAsync(partyId, player, DateTimeOffset.UtcNow);
        Assert.Contains("Pieter is expecting you to: Send the CV to Bol", owed);
    }

    [Fact]
    public async Task Session_NobodySpeaks_IsMissedAndFree()
    {
        var (partyId, _, pieter, player) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        var session = await RunUntilStartedAsync(partyId, player, pieter);
        var balance = (await Wallet(partyId, player).GetAsync()).Balance;

        await Booking.StepAsync(partyId, Over(session));

        Assert.Equal(SessionStatus.Missed, (await Book(partyId).GetSessionsAsync()).Single(s => s.Id == session.Id).Status);
        Assert.Equal(balance, (await Wallet(partyId, player).GetAsync()).Balance);
    }

    [Fact]
    public async Task Session_OneSided_IsMissedNotCharged()
    {
        var (partyId, _, pieter, player) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        var session = await RunUntilStartedAsync(partyId, player, pieter);
        var balance = (await Wallet(partyId, player).GetAsync()).Balance;

        await SayAsync(session, pieter, "Are you there?");
        await Booking.StepAsync(partyId, Over(session));

        Assert.Equal(SessionStatus.Missed, (await Book(partyId).GetSessionsAsync()).Single(s => s.Id == session.Id).Status);
        Assert.Equal(balance, (await Wallet(partyId, player).GetAsync()).Balance);
    }

    [Fact]
    public async Task Practice_PaysByFeeNotByShift()
    {
        var (partyId, _, pieter, _) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        var occupation = _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, pieter));
        var wallet = Wallet(partyId, pieter);
        var before = (await wallet.GetAsync()).Balance;
        var start = DateTimeOffset.Now.AddHours(-4);
        await occupation.StepAsync(start, asleep: false);
        var record = (await occupation.LeaveEarlyAsync(DateTimeOffset.Now))!;

        var paid = await _fx.Service<Workplaces>().LeaveEarlyAsync(partyId, pieter, (await occupation.GetAsync()).Job!, record);

        Assert.Equal(0, paid);
        Assert.Equal(before, (await wallet.GetAsync()).Balance);
    }

    [Fact]
    public async Task Life_OffersAPractice_AndBookingItIsARealSession()
    {
        var (partyId, denise, pieter, _) = await _fx.CreateWorldAsync();
        await PractiseAsync(partyId, pieter);
        Scripted.Choose = "Book a session with Pieter";

        var activity = await _fx.GrainFactory.GetGrain<IPersonaLifeGrain>(WorldKeys.Life(partyId, denise)).LiveAsync(null);

        Assert.Equal("book", activity.Action);
        var session = Assert.Single(await Book(partyId).GetSessionsAsync());
        Assert.Equal(denise, session.ClientId);
        Assert.Equal("say hello to everyone", session.Reason);
    }

    /// <summary>A Market that answers from a table; removing a quote is a failed read.</summary>
    public sealed class FakeMarket : IMarketFeed
    {
        public static readonly ConcurrentDictionary<string, Quote> Quotes = new();
        public static volatile string? Search;

        public string Kind => MarketKinds.Predictions;

        public Task<List<Quote>> SearchAsync(string? focus, int limit, CancellationToken ct)
            => Task.FromResult(Quotes.Values.Where(q => q.Id == focus).ToList());

        public Task<List<Quote>> QuotesAsync(IReadOnlyList<string> ids, CancellationToken ct)
            => Task.FromResult(ids.Where(Quotes.ContainsKey).Select(id => Copy(Quotes[id])).ToList());

        // Feeds answering by id carry no name, as Kraken's does.
        private static Quote Copy(Quote q) => new() { Id = q.Id, Bid = q.Bid, Ask = q.Ask, Open = q.Open, Settled = q.Settled };
    }

    /// <summary>Voices the pick <see cref="MenuJudge"/> makes from <see cref="Choose"/>, and answers the Apps' structured calls.</summary>
    public sealed class Scripted : IWorldLlm
    {
        public static volatile string? Choose;

        public Task<string?> CompleteAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null)
            => Task.FromResult<string?>(null);

        public Task<JsonObject?> CompleteJsonAsync(JobComplexity preferred, string system, string user, CancellationToken ct, double? temperature = null, IReadOnlyList<string>? fields = null)
        {
            if (user.Contains("# What you've decided"))
                return Task.FromResult<JsonObject?>(new JsonObject { ["thought"] = "scripted", ["intent"] = "say hello to everyone" });
            return Task.FromResult<JsonObject?>(new JsonObject
            {
                ["line"] = "1", ["side"] = "buy", ["percent"] = "10", ["chance"] = "70", ["thesis"] = "scripted thesis",
                ["homework"] = "Send the CV to Bol", ["days"] = "2", ["kept"] = "none",
            });
        }
    }

    /// <summary>Around midday on a Monday, so nobody sleeps through a test run at night; moved by hand so walks can finish.</summary>
    public sealed class Noon : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
        private static long _ticks;

        public static void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

        public override DateTimeOffset GetUtcNow() => Start.AddTicks(Interlocked.Read(ref _ticks));

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

        /// <summary>Two Personas and a human, who differ only by Driver.</summary>
        public async Task<(Guid PartyId, Guid Denise, Guid Pieter, Guid Player)> CreateWorldAsync()
        {
            var (partyId, denise, pieter, player) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            var personas = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
            await personas.AddPersona(new Persona(denise, "Denise", "You are Denise.", "A hacker."));
            await personas.AddPersona(new Persona(pieter, "Pieter", "You are Pieter.", "An architect."));
            await personas.AddPersona(new Persona(player, "Tim", "A human.", "A human."));
            var participants = new List<PartyParticipant>
            {
                new() { Id = denise, Name = "Denise", Driver = DriverKind.LLM },
                new() { Id = pieter, Name = "Pieter", Driver = DriverKind.LLM },
                new() { Id = player, Name = "Tim", Driver = DriverKind.User },
            };
            await GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty).AddParty(new PartyInfo { Id = partyId, Name = "world", Participants = participants });
            await GrainFactory.GetGrain<IPartyGrain>(partyId).SetParticipants(participants);
            return (partyId, denise, pieter, player);
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
                    services.AddSingleton<ISystemOneJudge>(new MenuJudge(() => Scripted.Choose));
                    services.AddSingleton<Workplaces>();
                    services.AddSingleton<Responsibilities>();
                    services.AddSingleton<IMarketFeed, FakeMarket>();
                    services.AddSingleton<MarketFeeds>();
                    services.AddSingleton<Desks>();
                    services.AddSingleton<Sessions>();
                    services.AddSingleton<IWorldApp, WorkApp>();
                    services.AddSingleton<IWorldApp, DeskApp>();
                    services.AddSingleton<IWorldApp, PracticeApp>();
                });
            }
        }
    }
}
