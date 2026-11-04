using System.Collections.Concurrent;
using System.Globalization;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.World.Places;
using PartyTown.World.Work.Markets;

namespace PartyTown.World.Work.Companies;

/// <summary>Pure: when a Company reads its Market, and how a result lands on the people in it.</summary>
public static class DeskRules
{
    public static readonly TimeSpan MarkEvery = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan WatchEvery = TimeSpan.FromHours(6);
    public const int WatchSize = 12;
    public const double DefaultCapital = 100_000;
    public const double MinCapital = 1_000;
    public const double MaxCapital = 10_000_000;

    public static bool Due(long? last, TimeSpan every, long now) => last is not { } at || now - at >= every.TotalMilliseconds;

    /// <summary>How a result moves the mood, as p(better): stronger the more of the Book it was.</summary>
    public static double Better(double amount, double capital)
        => capital <= 0 ? 0.5 : Math.Clamp(0.5 + amount / capital * 5, 0.2, 0.8);

    public static string Credits(double amount) => Math.Round(amount).ToString("#,0", CultureInfo.InvariantCulture);

    public static string Signed(double amount) => $"{(amount >= 0 ? "+" : "-")}{Credits(Math.Abs(amount))}";

    public static string Percent(double fraction) => $"{(fraction >= 0 ? "+" : "-")}{Math.Abs(fraction * 100).ToString("0.0", CultureInfo.InvariantCulture)}%";

    public static string Settled(Company company, Trade trade)
        => $"{trade.Title} settled for {company.Name}: {Signed(trade.Realized ?? 0)} credits.";

    public static string Moved(Company company, double move)
        => $"{company.Name}'s book is {Percent(move)} today, at {Credits(company.Book.Value)} credits.";
}

/// <summary>Where a Company meets its Market (ADR 0030): the I/O around <see cref="ICompaniesGrain"/>, and word of results reaching its staff.</summary>
public sealed class Desks(IGrainFactory grains, MarketFeeds feeds, WorldMemory memory, ILogger<Desks> logger)
{
    private static readonly TimeSpan ReadBudget = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<Guid, byte> _refreshing = new();

    /// <summary>Founds a Company with an office of its own; null when the name is taken or the Market isn't known.</summary>
    public async Task<Company?> FoundAsync(Guid partyId, string name, string mandate, string market, string? focus, double? capital)
    {
        if (feeds.For(market) is null) return null;
        var places = grains.GetGrain<IPlacesGrain>(partyId);
        var localeId = await places.LocaleAsync(name, null);
        // Authors aren't held to the Opening budget; a full map leaves the office offscreen.
        var opened = await places.OpenAsync(localeId, "office", "author");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var company = await Companies(partyId).FoundAsync(new Company
        {
            Id = Guid.NewGuid(), Name = name, Mandate = mandate, Market = market, Focus = focus, FoundedAt = now,
            Workplace = opened?.Name ?? name,
            Book = BookRules.Open(Math.Clamp(capital ?? DeskRules.DefaultCapital, DeskRules.MinCapital, DeskRules.MaxCapital)),
        });
        if (company is not null) await RefreshCompanyAsync(partyId, company, DateTimeOffset.UtcNow);
        return company is null ? null : await Companies(partyId).GetAsync(company.Id);
    }

    /// <summary>Puts someone on the staff; the job's hours are theirs, the workplace is the Company's.</summary>
    public async Task<OccupationView?> HireAsync(Guid partyId, Guid companyId, Guid who, string title, int days, int start, int end)
    {
        if (await Companies(partyId).GetAsync(companyId) is not { } company) return null;
        return await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who)).SetAsync(new Occupation
        {
            Title = title, Workplace = company.Workplace, Days = days, Start = start, End = end, CompanyId = companyId,
        });
    }

    /// <summary>One order, at a quote read just now; Life and HTTP both come through here.</summary>
    public async Task<TradeResult> TradeAsync(Guid partyId, Guid companyId, TradeOrder order)
    {
        if (await Companies(partyId).GetAsync(companyId) is not { } company) return TradeResult.No("That Company is gone.");
        if (feeds.For(company.Market) is not { } feed) return TradeResult.No("The screens are down.");
        using var cts = new CancellationTokenSource(ReadBudget);
        var quotes = await feed.QuotesAsync(new List<string> { order.InstrumentId }, cts.Token);
        if (quotes.FirstOrDefault(q => q.Id == order.InstrumentId) is not { } quote) return TradeResult.No("The screens are down.");
        return await Companies(partyId).TradeAsync(companyId, quote, order, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>The clock's step: runs only while the World runs, so a pause catches up as one gap (ADR 0030).</summary>
    public async Task RefreshAsync(Guid partyId, DateTimeOffset now)
    {
        if (!_refreshing.TryAdd(partyId, 0)) return;
        try
        {
            foreach (var company in await Companies(partyId).GetAllAsync())
                await RefreshCompanyAsync(partyId, company, now);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Company refresh failed"); }
        finally { _refreshing.TryRemove(partyId, out _); }
    }

    private async Task RefreshCompanyAsync(Guid partyId, Company company, DateTimeOffset now)
    {
        if (feeds.For(company.Market) is not { } feed) return;
        var at = now.ToUnixTimeMilliseconds();
        using var cts = new CancellationTokenSource(ReadBudget);
        if (DeskRules.Due(company.WatchedAt, DeskRules.WatchEvery, at))
        {
            var found = await feed.SearchAsync(company.Focus, DeskRules.WatchSize, cts.Token);
            // A failed search is no reason to forget what you were watching.
            if (found.Count > 0)
            {
                await Companies(partyId).WatchAsync(company.Id, found, at);
                company.Watchlist = found;
            }
        }
        if (!DeskRules.Due(company.MarkedAt, DeskRules.MarkEvery, at)) return;
        var ids = company.Watchlist.Select(q => q.Id).Concat(company.Book.Positions.Select(p => p.InstrumentId)).Distinct().ToList();
        if (ids.Count == 0) return;
        var quotes = await feed.QuotesAsync(ids, cts.Token);
        var news = await Companies(partyId).MarkAsync(company.Id, quotes, now.ToLocalTime(), at);
        if (news.Any && await Companies(partyId).GetAsync(company.Id) is { } after) await TellAsync(partyId, after, news);
    }

    /// <summary>Prices aren't Events (ADR 0030): only Settlements and big days become memory, for staff alone.</summary>
    private async Task TellAsync(Guid partyId, Company company, BookNews news)
    {
        var lines = news.Settled.Select(t => (Text: DeskRules.Settled(company, t), Amount: t.Realized ?? 0)).ToList();
        if (news.BigMove is { } move) lines.Add((DeskRules.Moved(company, move), company.Book.Value * move));

        var clock = grains.GetGrain<IWorldClockGrain>(partyId);
        var staff = await StaffAsync(partyId, company.Id);
        foreach (var (text, amount) in lines)
        {
            await clock.RecordAsync(new WorldActivity { PersonaName = company.Name, App = DeskApp.AppId, Action = "result", Summary = text, TargetId = company.Id });
            foreach (var person in staff)
            {
                try
                {
                    await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(partyId, person.Id))
                        .ShiftValenceAsync(DeskRules.Better(amount, company.Book.Capital), $"the book at {company.Name}");
                }
                catch (Exception ex) { logger.LogDebug(ex, "Mood from the book failed for {Who}", person.Id); }
            }
        }
        if (staff.Count == 0) return;
        await clock.NotifyAsync(staff.Select(s => s.Id).ToList());
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var moment = lines.Select(l => new MomentLine(Narrator.PersonaId, $"[At work] {l.Text}", ts)).ToList();
        _ = memory.CaptureAsync(new Moment(partyId, MomentSources.Work, company.Id, moment, moment.Count - 1, staff), CancellationToken.None);
    }

    /// <summary>Everyone whose Occupation names this Company, whoever drives them.</summary>
    public async Task<List<ParticipantView>> StaffAsync(Guid partyId, Guid companyId)
    {
        var cast = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Where(c => c.Id != Narrator.PersonaId).ToList();
        var jobs = await Task.WhenAll(cast.Select(c => grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, c.Id)).GetAsync()));
        return cast.Where((_, i) => jobs[i].Job?.CompanyId == companyId)
            .Select(c => new ParticipantView(c.Id, c.Name, c.DefaultDriver)).ToList();
    }

    private ICompaniesGrain Companies(Guid partyId) => grains.GetGrain<ICompaniesGrain>(partyId);
}
