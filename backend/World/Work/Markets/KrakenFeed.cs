using System.Text.Json.Nodes;

namespace PartyTown.World.Work.Markets;

/// <summary>Pure reading of Kraken's public API: euro pairs, quoted with a real bid and ask.</summary>
public static class KrakenQuotes
{
    private const string Prefix = "kr:";
    public static readonly string[] Majors = { "BTC", "ETH", "SOL", "XRP", "ADA", "DOGE" };

    public static string Id(string pair) => Prefix + pair;

    public static string? PairOf(string id) => id.StartsWith(Prefix, StringComparison.Ordinal) ? id[Prefix.Length..] : null;

    /// <summary>Kraken calls bitcoin XBT; people don't.</summary>
    public static string Display(string wsname) => wsname.Replace("XBT/", "BTC/", StringComparison.Ordinal);

    /// <summary>Pair key → display name for the coins a focus names, or the majors when it names none.</summary>
    public static Dictionary<string, string> Pairs(JsonNode? result, string? focus)
    {
        var euro = (result as JsonObject ?? new JsonObject())
            .Where(p => p.Value?["status"]?.ToString() == "online" && p.Value?["wsname"]?.ToString().EndsWith("/EUR", StringComparison.Ordinal) == true)
            .ToDictionary(p => p.Key, p => Display(p.Value!["wsname"]!.ToString()));
        var words = MarketText.Words(focus);
        var named = euro.Where(p => words.Contains(p.Value.Split('/')[0])).ToList();
        var picked = named.Count > 0 ? named : euro.Where(p => Majors.Contains(p.Value.Split('/')[0])).ToList();
        return picked.ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>One Ticker entry; null when it has no usable bid or ask.</summary>
    public static Quote? Read(string pair, JsonNode? ticker, string? title = null)
    {
        var ask = MarketText.Number(ticker?["a"]?[0]);
        var bid = MarketText.Number(ticker?["b"]?[0]);
        if (ask is not > 0 || bid is not > 0) return null;
        return new Quote
        {
            Id = Id(pair), Title = MarketText.Clean(title), Bid = bid.Value, Ask = ask.Value,
            Previous = MarketText.Number(ticker?["o"]), Open = true,
        };
    }
}

/// <summary>Crypto through Kraken's public API; no key, read-only. Trades all night, unlike the people watching it.</summary>
public sealed class KrakenFeed(IHttpClientFactory http, ILogger<KrakenFeed> logger) : IMarketFeed
{
    private const string Base = "https://api.kraken.com/0/public";

    public string Kind => MarketKinds.Crypto;

    public async Task<List<Quote>> SearchAsync(string? focus, int limit, CancellationToken ct)
    {
        var pairs = KrakenQuotes.Pairs(await GetAsync("/AssetPairs", ct), focus);
        if (pairs.Count == 0) return new List<Quote>();
        var picked = pairs.Keys.Take(limit).ToList();
        var result = await GetAsync($"/Ticker?pair={string.Join(",", picked.Select(Uri.EscapeDataString))}", ct);
        return picked.Select(p => KrakenQuotes.Read(p, result?[p], pairs[p])).OfType<Quote>().ToList();
    }

    public async Task<List<Quote>> QuotesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var pairs = ids.Select(KrakenQuotes.PairOf).OfType<string>().Distinct().ToList();
        if (pairs.Count == 0) return new List<Quote>();
        var result = await GetAsync($"/Ticker?pair={string.Join(",", pairs.Select(Uri.EscapeDataString))}", ct);
        return pairs.Select(p => KrakenQuotes.Read(p, result?[p])).OfType<Quote>().ToList();
    }

    private async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient("markets");
            await using var body = await client.GetStreamAsync(Base + path, ct);
            var json = await JsonNode.ParseAsync(body, cancellationToken: ct);
            if (json?["error"] is JsonArray { Count: > 0 } errors) logger.LogWarning("Kraken said: {Errors}", WorldStream.Clip(errors.ToJsonString(), 200));
            return json?["result"];
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Kraken read failed");
            return null;
        }
    }
}
