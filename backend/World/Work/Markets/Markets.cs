using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PartyTown.World.Work.Markets;

/// <summary>The real Markets a Company can read (ADR 0030).</summary>
public static class MarketKinds
{
    public const string Predictions = "predictions";
    public const string Crypto = "crypto";
    public const string Stocks = "stocks";
    public static readonly string[] All = { Predictions, Crypto, Stocks };
}

/// <summary>One Instrument as its Market quotes it now, in credits per unit.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Markets.Quote")]
public sealed class Quote
{
    [Id(0)] public string Id { get; set; } = string.Empty;
    /// <summary>Cleaned: the only Market text that reaches prompts.</summary>
    [Id(1)] public string Title { get; set; } = string.Empty;
    [Id(2)] public double Bid { get; set; }
    [Id(3)] public double Ask { get; set; }
    /// <summary>Where it stood before today, when the Market says.</summary>
    [Id(4)] public double? Previous { get; set; }
    /// <summary>False while the Market won't deal: shut for the night, or finished.</summary>
    [Id(5)] public bool Open { get; set; }
    /// <summary>What one unit finally pays, once the Market itself says it's over.</summary>
    [Id(6)] public double? Settled { get; set; }

    public double Mid => (Bid + Ask) / 2;
}

/// <summary>Reads one real Market. Never writes to it.</summary>
public interface IMarketFeed
{
    string Kind { get; }

    /// <summary>Instruments for a Mandate's focus, busiest first, all open.</summary>
    Task<List<Quote>> SearchAsync(string? focus, int limit, CancellationToken ct);

    /// <summary>What the Market answered for; a missing id is stale, never settled.</summary>
    Task<List<Quote>> QuotesAsync(IReadOnlyList<string> ids, CancellationToken ct);
}

/// <summary>Finds the feed for a Market kind; null when it isn't configured.</summary>
public sealed class MarketFeeds(IEnumerable<IMarketFeed> feeds)
{
    public IMarketFeed? For(string kind) => feeds.FirstOrDefault(f => f.Kind == kind);
}

/// <summary>Helpers for untrusted Market JSON.</summary>
public static class MarketText
{
    public const int MaxTitle = 120;

    /// <summary>Market text is untrusted (ADR 0030): no control characters, one short line.</summary>
    public static string Clean(string? text, int max = MaxTitle)
        => WorldStream.Clip(new string((text ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim(), max);

    /// <summary>Markets send numbers as numbers or as strings, sometimes both in one payload.</summary>
    public static double? Number(JsonNode? node)
        => node is not null && double.TryParse(node.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;

    /// <summary>"50", "50%" or "50 %": a model asked for a percent writes any of them.</summary>
    public static double? Percent(JsonNode? node)
        => node is not null && double.TryParse(node.ToString().Replace("%", string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : null;

    public static bool Flag(JsonNode? node) => string.Equals(node?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Polymarket nests arrays as JSON inside a string.</summary>
    public static List<string> Strings(JsonNode? node)
    {
        var raw = node?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        try { return (JsonNode.Parse(raw) as JsonArray)?.Select(n => n?.ToString() ?? string.Empty).ToList() ?? new List<string>(); }
        catch (JsonException) { return new List<string>(); }
    }

    /// <summary>The words of a focus line: "BTC, eth" → BTC, ETH.</summary>
    public static List<string> Words(string? focus)
        => (focus ?? string.Empty).Split(new[] { ' ', ',', ';', '/', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToUpperInvariant()).Where(w => w.Length > 0).Distinct().ToList();
}
