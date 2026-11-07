using PartyTown.World.Work.Markets;

namespace PartyTown.World.Work.Companies;

/// <summary>What a Company does all day: trades a Book (ADR 0030) or works on the code of the world (ADR 0039).</summary>
public static class CompanyKinds
{
    public const string Trading = "trading";
    public const string Works = "works";
}

/// <summary>An outfit in one Party with a workplace, staff and a Mandate (ADR 0030). Staff are Occupations that name it.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Companies.Company")]
public sealed class Company
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public string Name { get; set; } = string.Empty;
    /// <summary>What it's set up to do, in one line for prompts.</summary>
    [Id(2)] public string Mandate { get; set; } = string.Empty;
    /// <summary>One of <see cref="MarketKinds"/>.</summary>
    [Id(3)] public string Market { get; set; } = MarketKinds.Predictions;
    /// <summary>What to look for in that Market: a search term, or coins and tickers.</summary>
    [Id(4)] public string? Focus { get; set; }
    [Id(5)] public string Workplace { get; set; } = string.Empty;
    [Id(6)] public Book Book { get; set; } = new();
    [Id(7)] public List<Quote> Watchlist { get; set; } = new();
    [Id(8)] public long? MarkedAt { get; set; }
    [Id(9)] public long? WatchedAt { get; set; }
    [Id(10)] public long FoundedAt { get; set; }
    /// <summary>One of <see cref="CompanyKinds"/>.</summary>
    [Id(11)] public string Kind { get; set; } = CompanyKinds.Trading;
}

/// <summary>The Party's Companies (key = Party id). Pure state: reading Markets happens in <see cref="Desks"/>, outside the grain.</summary>
public sealed class CompaniesGrain(
    [PersistentState(stateName: "companies", storageName: "parties")]
    IPersistentState<CompaniesState> state)
    : Grain, ICompaniesGrain
{
    public Task<List<Company>> GetAllAsync() => Task.FromResult(state.State.Companies.ToList());

    public Task<Company?> GetAsync(Guid id) => Task.FromResult(Find(id));

    public async Task<Company?> FoundAsync(Company company)
    {
        if (state.State.Companies.Any(c => string.Equals(c.Name, company.Name, StringComparison.OrdinalIgnoreCase))) return null;
        state.State.Companies.Add(company);
        await state.WriteStateAsync();
        return company;
    }

    public async Task<TradeResult> TradeAsync(Guid id, Quote quote, TradeOrder order, long now)
    {
        if (Find(id) is not { } company) return TradeResult.No("That Company is gone.");
        // Feeds answering by id carry no name; the one from the Watchlist or Position stands.
        if (quote.Title.Length == 0) quote.Title = TitleOf(company, quote.Id);
        var result = BookRules.Apply(company.Book, quote, order, now);
        if (result.Trade is not null) await state.WriteStateAsync();
        return result;
    }

    public async Task WatchAsync(Guid id, List<Quote> watchlist, long now)
    {
        if (Find(id) is not { } company) return;
        company.Watchlist = watchlist;
        company.WatchedAt = now;
        await state.WriteStateAsync();
    }

    public async Task<BookNews> MarkAsync(Guid id, List<Quote> quotes, DateTimeOffset local, long now)
    {
        if (Find(id) is not { } company) return new BookNews(new List<Trade>(), null);
        var fresh = quotes.ToDictionary(q => q.Id);
        foreach (var watched in company.Watchlist.ToList())
        {
            if (!fresh.TryGetValue(watched.Id, out var quote)) continue;
            (watched.Bid, watched.Ask, watched.Previous, watched.Open, watched.Settled) = (quote.Bid, quote.Ask, quote.Previous, quote.Open, quote.Settled);
        }
        company.Watchlist.RemoveAll(q => q.Settled is not null);
        var news = BookRules.Mark(company.Book, quotes, local, now);
        company.MarkedAt = now;
        await state.WriteStateAsync();
        return news;
    }

    private Company? Find(Guid id) => state.State.Companies.FirstOrDefault(c => c.Id == id);

    private static string TitleOf(Company company, string instrumentId)
        => company.Book.Positions.FirstOrDefault(p => p.InstrumentId == instrumentId)?.Title
            ?? company.Watchlist.FirstOrDefault(q => q.Id == instrumentId)?.Title
            ?? instrumentId;
}

[Alias("PartyTown.World.Work.Companies.ICompaniesGrain")]
public interface ICompaniesGrain : IGrainWithGuidKey
{
    [Alias("GetAllAsync")] Task<List<Company>> GetAllAsync();
    [Alias("GetAsync")] Task<Company?> GetAsync(Guid id);
    /// <summary>Null when the name is taken.</summary>
    [Alias("FoundAsync")] Task<Company?> FoundAsync(Company company);
    /// <summary>Fills an order at <paramref name="quote"/>; the one way into a Book, for Life and HTTP alike.</summary>
    [Alias("TradeAsync")] Task<TradeResult> TradeAsync(Guid id, Quote quote, TradeOrder order, long now);
    [Alias("WatchAsync")] Task WatchAsync(Guid id, List<Quote> watchlist, long now);
    /// <summary>Takes in a read of the Market; returns what's worth telling.</summary>
    [Alias("MarkAsync")] Task<BookNews> MarkAsync(Guid id, List<Quote> quotes, DateTimeOffset local, long now);
}

[GenerateSerializer, Alias("PartyTown.World.Work.Companies.CompaniesState")]
public sealed class CompaniesState
{
    [Id(0)] public List<Company> Companies { get; set; } = new();
}
