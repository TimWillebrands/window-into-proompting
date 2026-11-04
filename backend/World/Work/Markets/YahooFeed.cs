using System.Text.Json.Nodes;

namespace PartyTown.World.Work.Markets;

/// <summary>Pure reading of Yahoo's unofficial chart API: it has no bid or ask, so a set spread stands in.</summary>
public static class YahooQuotes
{
    private const string Prefix = "yh:";
    /// <summary>Half the stand-in spread (ADR 0030), so round trips still cost money.</summary>
    public const double HalfSpread = 0.001;

    public static string Id(string symbol) => Prefix + symbol;

    public static string? SymbolOf(string id) => id.StartsWith(Prefix, StringComparison.Ordinal) ? id[Prefix.Length..] : null;

    /// <summary>Equities and funds from a search; everything else Yahoo finds is noise here.</summary>
    public static List<string> Symbols(JsonNode? search)
        => (search?["quotes"] as JsonArray ?? new JsonArray())
            .Where(q => q?["quoteType"]?.ToString() is "EQUITY" or "ETF" && !string.IsNullOrEmpty(q?["symbol"]?.ToString()))
            .Select(q => q!["symbol"]!.ToString())
            .ToList();

    /// <summary>One chart's meta; open only inside today's regular session.</summary>
    public static Quote? Read(JsonNode? chart, DateTimeOffset now)
    {
        var meta = chart?["chart"]?["result"]?[0]?["meta"];
        var symbol = meta?["symbol"]?.ToString();
        if (string.IsNullOrEmpty(symbol) || MarketText.Number(meta?["regularMarketPrice"]) is not > 0) return null;
        var price = MarketText.Number(meta?["regularMarketPrice"])!.Value;
        var regular = meta?["currentTradingPeriod"]?["regular"];
        var seconds = now.ToUnixTimeSeconds();
        return new Quote
        {
            Id = Id(symbol),
            Title = MarketText.Clean(meta?["longName"]?.ToString() ?? meta?["shortName"]?.ToString() ?? symbol),
            Bid = price * (1 - HalfSpread),
            Ask = price * (1 + HalfSpread),
            Previous = MarketText.Number(meta?["chartPreviousClose"]),
            Open = MarketText.Number(regular?["start"]) <= seconds && seconds < MarketText.Number(regular?["end"]),
        };
    }
}

/// <summary>Stocks through Yahoo's unofficial endpoints: keyless, no guarantees. A keyed vendor is one more feed.</summary>
public sealed class YahooFeed(IHttpClientFactory http, TimeProvider time, ILogger<YahooFeed> logger) : IMarketFeed
{
    private const string Base = "https://query1.finance.yahoo.com";

    public string Kind => MarketKinds.Stocks;

    public async Task<List<Quote>> SearchAsync(string? focus, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(focus)) return new List<Quote>();
        var found = await GetAsync($"/v1/finance/search?q={Uri.EscapeDataString(focus.Trim())}&quotesCount={limit}&newsCount=0", ct);
        return await QuotesAsync(YahooQuotes.Symbols(found).Take(limit).Select(YahooQuotes.Id).ToList(), ct);
    }

    public async Task<List<Quote>> QuotesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var charts = await Task.WhenAll(ids.Select(YahooQuotes.SymbolOf).OfType<string>().Distinct()
            .Select(s => GetAsync($"/v8/finance/chart/{Uri.EscapeDataString(s)}?interval=1d&range=1d", ct)));
        return charts.Select(c => YahooQuotes.Read(c, now)).OfType<Quote>().ToList();
    }

    private async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient("markets");
            // Yahoo turns away clients that don't look like browsers.
            using var request = new HttpRequestMessage(HttpMethod.Get, Base + path);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            using var response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            return await JsonNode.ParseAsync(body, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Yahoo read failed");
            return null;
        }
    }
}
