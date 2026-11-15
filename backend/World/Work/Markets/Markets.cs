using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartyTown.World.Work.Markets;

public static class MarketKinds
{
    public const string Predictions = "predictions";
    public const string Crypto = "crypto";
    public const string Stocks = "stocks";
    public static readonly string[] All = { Predictions, Crypto, Stocks };
}

[GenerateSerializer, Alias("PartyTown.World.Work.Markets.Quote")]
public sealed class Quote
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    [Id(1)] public string Title { get; set; } = string.Empty;
    [Id(2)] public double Bid { get; set; }
    [Id(3)] public double Ask { get; set; }
    [Id(4)] public double? Previous { get; set; }
    [Id(5)] public bool Open { get; set; }
    [Id(6)] public double? Settled { get; set; }

    public double Mid => (Bid + Ask) / 2;
}

public interface IMarketFeed
{
    string Kind { get; }

    Task<List<Quote>> SearchAsync(string? focus, int limit, CancellationToken ct);

    Task<List<Quote>> QuotesAsync(IReadOnlyList<string> ids, CancellationToken ct);
}

public sealed class MarketFeeds(IEnumerable<IMarketFeed> feeds)
{
    public IMarketFeed? For(string kind) => feeds.FirstOrDefault(f => f.Kind == kind);
}

public static class MarketText
{
    public const int MaxTitle = 120;

    public static string Clean(string? text, int max = MaxTitle)
        => WorldStream.Clip(new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim(), max);

    public static double? Number(JsonNode? node)
        => node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;

    public static double? Percent(JsonNode? node)
        => node is not null && double.TryParse(node.ToString().Replace("%", string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;

    public static bool Flag(JsonNode? node) => string.Equals(node?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

    public static List<string> Strings(JsonNode? node)
    {
        var raw = node?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        try { return (JsonNode.Parse(raw) as JsonArray)?.Select(n => n?.ToString() ?? string.Empty).ToList() ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    public static List<string> Words(string? focus)
        => (focus ?? string.Empty).Split(new[] { ' ', ',', ';', '/', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToUpperInvariant()).Where(w => w.Length > 0).Distinct().ToList();
}
