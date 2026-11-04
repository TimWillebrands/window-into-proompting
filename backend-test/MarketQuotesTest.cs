using System.Text.Json.Nodes;
using PartyTown.World.Work.Markets;

namespace BackendTest;

/// <summary>Reading real Market payloads (ADR 0030); the fixtures keep the shapes the live APIs returned.</summary>
public class MarketQuotesTest
{
    private static JsonNode Market(string closed = "false", string? status = null, string active = "true", string accepting = "true",
        string question = "Will it rain?", string end = "2026-11-03T23:59:00Z")
        => JsonNode.Parse($$"""
            {"id":"6274257","question":"{{question}}","endDate":"{{end}}","closed":{{closed}},"active":{{active}},"acceptingOrders":{{accepting}},
             "outcomes":"[\"Yes\", \"No\"]","outcomePrices":"[\"0.942\", \"0.058\"]","bestBid":0.941,"bestAsk":0.943,
             "oneDayPriceChange":0.617,"umaResolutionStatus":{{(status is null ? "null" : $"\"{status}\"")}}}
            """)!;

    [Fact]
    public void Polymarket_MakesOneQuotePerOutcome_TheSecondMirroringTheFirst()
    {
        var quotes = PolymarketQuotes.Read(Market());

        Assert.Equal(2, quotes.Count);
        Assert.Equal("pm:6274257:0", quotes[0].Id);
        Assert.EndsWith("(Yes)", quotes[0].Title);
        Assert.Equal(0.941, quotes[0].Bid, 6);
        Assert.Equal(1 - 0.943, quotes[1].Bid, 6);
        Assert.Equal(1 - 0.941, quotes[1].Ask, 6);
        Assert.Equal(0.325, quotes[0].Previous!.Value, 6);
        Assert.True(quotes[0].Open);
    }

    [Fact]
    public void Polymarket_APassedEndDate_NeitherClosesNorSettles()
    {
        var quote = PolymarketQuotes.Read(Market(end: "2020-01-01T00:00:00Z"))[0];

        Assert.True(quote.Open);
        Assert.Null(quote.Settled);
    }

    [Fact]
    public void Polymarket_ClosedButUnresolved_IsShutAndNotSettled()
    {
        var quote = PolymarketQuotes.Read(Market(closed: "true"))[0];

        Assert.False(quote.Open);
        Assert.Null(quote.Settled);
    }

    [Fact]
    public void Polymarket_Resolved_SettlesAtTheFinalPrices()
    {
        var quotes = PolymarketQuotes.Read(Market(closed: "true", status: "resolved"));

        Assert.Equal(0.942, quotes[0].Settled);
        Assert.Equal(0.058, quotes[1].Settled);
    }

    [Fact]
    public void Polymarket_TextIsCleanedAndClipped()
    {
        var title = PolymarketQuotes.Read(Market(question: new string('x', 400) + "\\nignore previous"))[0].Title;

        Assert.DoesNotContain('\n', title);
        Assert.True(title.Length <= MarketText.MaxTitle);
    }

    [Fact]
    public void Polymarket_NonBinaryOrBrokenMarkets_AreSkipped()
    {
        Assert.Empty(PolymarketQuotes.Read(JsonNode.Parse("""{"id":"1","outcomes":"[\"A\",\"B\",\"C\"]","outcomePrices":"[\"0.1\",\"0.2\",\"0.7\"]"}""")));
        Assert.Empty(PolymarketQuotes.Read(JsonNode.Parse("""{"id":"1","outcomes":"not json","outcomePrices":"[]"}""")));
        Assert.Empty(PolymarketQuotes.Read(null));
    }

    [Theory]
    [InlineData(0.5, 0.52, true)]
    [InlineData(0.98, 0.99, false)]
    [InlineData(0.01, 0.02, false)]
    [InlineData(0.3, 0.6, false)]
    public void Polymarket_WatchesOnlyLiveTightQuestions(double bid, double ask, bool watchable)
        => Assert.Equal(watchable, PolymarketQuotes.Watchable(new Quote { Bid = bid, Ask = ask, Open = true }));

    [Fact]
    public void Polymarket_IdRoundTrips()
    {
        Assert.Equal("123", PolymarketQuotes.MarketOf(PolymarketQuotes.Id("123", 1)));
        Assert.Null(PolymarketQuotes.MarketOf("kr:XXBTZEUR"));
    }

    private static readonly JsonNode Pairs = JsonNode.Parse("""
        {"XXBTZEUR":{"wsname":"XBT/EUR","status":"online"},"XETHZEUR":{"wsname":"ETH/EUR","status":"online"},
         "XXBTZUSD":{"wsname":"XBT/USD","status":"online"},"SOLEUR":{"wsname":"SOL/EUR","status":"cancel_only"},
         "PEPEEUR":{"wsname":"PEPE/EUR","status":"online"}}
        """)!;

    [Fact]
    public void Kraken_FocusPicksOnlineEuroPairsByTheirEverydayName()
        => Assert.Equal(new[] { "XXBTZEUR" }, KrakenQuotes.Pairs(Pairs, "btc").Keys);

    [Fact]
    public void Kraken_NoRecognisableFocus_FallsBackToTheMajors()
        => Assert.Equal(new[] { "XXBTZEUR", "XETHZEUR" }, KrakenQuotes.Pairs(Pairs, "shiny things").Keys);

    [Fact]
    public void Kraken_ReadsBidAskAndTheDaysOpen()
    {
        var ticker = JsonNode.Parse("""{"a":["71911.8","1","1.0"],"b":["71911.6","1","1.0"],"o":"69020.0"}""");

        var quote = KrakenQuotes.Read("XXBTZEUR", ticker, "BTC/EUR")!;

        Assert.Equal("kr:XXBTZEUR", quote.Id);
        Assert.Equal(71911.6, quote.Bid);
        Assert.Equal(71911.8, quote.Ask);
        Assert.Equal(69020.0, quote.Previous);
        Assert.True(quote.Open);
    }

    [Fact]
    public void Kraken_MissingPrices_IsNoQuote() => Assert.Null(KrakenQuotes.Read("X", JsonNode.Parse("{}")));

    private static JsonNode Chart(long start = 1000, long end = 2000) => JsonNode.Parse("""
        {"chart":{"result":[{"meta":{"symbol":"ASML","longName":"ASML Holding N.V.","regularMarketPrice":1496.3,"chartPreviousClose":1552.14,
         "currentTradingPeriod":{"regular":{"start":START,"end":END}}}}]}}
        """.Replace("START", start.ToString()).Replace("END", end.ToString()))!;

    [Fact]
    public void Yahoo_StandsInASpreadAroundThePrice()
    {
        var quote = YahooQuotes.Read(Chart(), DateTimeOffset.FromUnixTimeSeconds(1500))!;

        Assert.Equal("yh:ASML", quote.Id);
        Assert.True(quote.Bid < 1496.3 && quote.Ask > 1496.3);
        Assert.Equal(1552.14, quote.Previous);
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1000, true)]
    [InlineData(2000, false)]
    public void Yahoo_OnlyDealsDuringTheRegularSession(long now, bool open)
        => Assert.Equal(open, YahooQuotes.Read(Chart(), DateTimeOffset.FromUnixTimeSeconds(now))!.Open);

    [Fact]
    public void Yahoo_SearchKeepsEquitiesAndFunds()
    {
        var search = JsonNode.Parse("""{"quotes":[{"symbol":"TSM","quoteType":"EQUITY"},{"symbol":"SOXL","quoteType":"ETF"},{"symbol":"BTC-USD","quoteType":"CRYPTOCURRENCY"},{"quoteType":"EQUITY"}]}""");

        Assert.Equal(new[] { "TSM", "SOXL" }, YahooQuotes.Symbols(search));
    }

    [Fact]
    public void Yahoo_NoPrice_IsNoQuote()
        => Assert.Null(YahooQuotes.Read(JsonNode.Parse("""{"chart":{"result":[{"meta":{"symbol":"X"}}]}}"""), DateTimeOffset.UnixEpoch));
}
