using System.Globalization;
using System.Text.Json.Nodes;

namespace PartyTown.World.Work.Markets;

/// <summary>Pure reading of Polymarket's Gamma API: one question becomes one Quote per outcome.</summary>
public static class PolymarketQuotes
{
    private const string Prefix = "pm:";
    /// <summary>Questions this settled carry no story; ones this wide can't be dealt in.</summary>
    private const double MinMid = 0.03, MaxMid = 0.97, MaxSpread = 0.1;
    private const double FallbackHalfSpread = 0.01;

    public static string Id(string market, int outcome) => $"{Prefix}{market}:{outcome.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The Gamma market id behind an Instrument id; null when it's not ours.</summary>
    public static string? MarketOf(string id)
    {
        if (!id.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var end = id.LastIndexOf(':');
        return end > Prefix.Length ? id[Prefix.Length..end] : null;
    }

    /// <summary>Only the Market settles (ADR 0030): endDate lies both ways, so it's never read.</summary>
    public static List<Quote> Read(JsonNode? market)
    {
        var quotes = new List<Quote>();
        var id = market?["id"]?.ToString();
        var outcomes = MarketText.Strings(market?["outcomes"]);
        var prices = MarketText.Strings(market?["outcomePrices"])
            .Select(p => double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN).ToList();
        if (string.IsNullOrEmpty(id) || outcomes.Count != 2 || prices.Count != 2 || prices.Any(double.IsNaN)) return quotes;

        var closed = MarketText.Flag(market?["closed"]);
        var settled = closed && string.Equals(market?["umaResolutionStatus"]?.ToString(), "resolved", StringComparison.OrdinalIgnoreCase);
        var open = !closed && MarketText.Flag(market?["active"]) && MarketText.Flag(market?["acceptingOrders"]);
        var bid = MarketText.Number(market?["bestBid"]) ?? Math.Max(0, prices[0] - FallbackHalfSpread);
        var ask = MarketText.Number(market?["bestAsk"]) ?? Math.Min(1, prices[0] + FallbackHalfSpread);
        var before = MarketText.Number(market?["oneDayPriceChange"]) is { } change ? prices[0] - change : (double?)null;
        var question = MarketText.Clean(market?["question"]?.ToString(), MarketText.MaxTitle - 16);

        for (var i = 0; i < 2; i++)
            quotes.Add(new Quote
            {
                Id = Id(id, i),
                Title = $"{question} ({MarketText.Clean(outcomes[i], 14)})",
                // The second outcome is the first one's mirror: buying No is selling Yes.
                Bid = i == 0 ? bid : 1 - ask,
                Ask = i == 0 ? ask : 1 - bid,
                Previous = i == 0 ? before : 1 - before,
                Open = open,
                Settled = settled ? prices[i] : null,
            });
        return quotes;
    }

    /// <summary>Worth watching: still open, not a foregone conclusion, tight enough to deal in.</summary>
    public static bool Watchable(Quote quote)
        => quote is { Open: true, Settled: null } && quote.Mid is >= MinMid and <= MaxMid && quote.Ask - quote.Bid <= MaxSpread;
}

/// <summary>Prediction markets through Polymarket's public Gamma API; no key, read-only.</summary>
public sealed class PolymarketFeed(IHttpClientFactory http, ILogger<PolymarketFeed> logger) : IMarketFeed
{
    private const string Base = "https://gamma-api.polymarket.com";

    public string Kind => MarketKinds.Predictions;

    public async Task<List<Quote>> SearchAsync(string? focus, int limit, CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(focus)
            ? "/markets?active=true&closed=false&order=volume24hr&ascending=false&limit=40"
            : $"/public-search?q={Uri.EscapeDataString(focus.Trim())}&limit_per_type=20&events_status=active";
        var json = await GetAsync(path, ct);
        var markets = json is JsonArray list
            ? list.AsEnumerable()
            : (json?["events"] as JsonArray)?.SelectMany(e => e?["markets"] as JsonArray ?? new JsonArray()) ?? Enumerable.Empty<JsonNode?>();
        return markets
            .OrderByDescending(m => MarketText.Number(m?["volume24hr"]) ?? 0)
            .SelectMany(PolymarketQuotes.Read)
            .Where(PolymarketQuotes.Watchable)
            .Take(limit)
            .ToList();
    }

    public async Task<List<Quote>> QuotesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var markets = ids.Select(PolymarketQuotes.MarketOf).OfType<string>().Distinct().ToList();
        if (markets.Count == 0) return new List<Quote>();
        var query = string.Join("&", markets.Select(m => $"id={Uri.EscapeDataString(m)}"));
        // By id, Gamma silently leaves out closed markets unless asked for only those; a Settlement hides there.
        var both = await Task.WhenAll(
            GetAsync($"/markets?{query}&limit={markets.Count}", ct),
            GetAsync($"/markets?{query}&closed=true&limit={markets.Count}", ct));
        var wanted = ids.ToHashSet();
        return both.OfType<JsonArray>()
            .SelectMany(a => a)
            .SelectMany(PolymarketQuotes.Read)
            .Where(q => wanted.Contains(q.Id))
            .GroupBy(q => q.Id)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient("markets");
            await using var body = await client.GetStreamAsync(Base + path, ct);
            return await JsonNode.ParseAsync(body, cancellationToken: ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Polymarket read failed");
            return null;
        }
    }
}
