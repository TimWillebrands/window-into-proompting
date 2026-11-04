using PartyTown.World.Work.Companies;
using PartyTown.World.Work.Markets;

namespace BackendTest;

/// <summary>Ghost fills on real quotes (ADR 0030): what a Trade costs, what a Mark shows, what only the Market can do.</summary>
public class BookTest
{
    private const long Now = 1_000;
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 12, 0, 0, TimeSpan.FromHours(1));

    private static Quote Yes(double bid = 0.40, double ask = 0.42, double? settled = null, bool open = true) => new()
    {
        Id = "pm:1:0", Title = "Will it? (Yes)", Bid = bid, Ask = ask, Open = open, Settled = settled,
    };

    private static TradeOrder Order(TradeSide side, double share, double? forecast = null, string id = "pm:1:0")
        => new(id, side, share, Guid.Empty, "Ana", "because", forecast);

    [Fact]
    public void Buy_FillsAtTheAskAndChargesAFee()
    {
        var book = BookRules.Open(10_000);

        var trade = BookRules.Apply(book, Yes(), Order(TradeSide.Buy, 0.1), Now).Trade!;

        Assert.Equal(0.42, trade.Price);
        Assert.Equal(1_000 / 0.42, trade.Units, 6);
        Assert.Equal(1, trade.Fee, 6);
        Assert.Equal(8_999, book.Cash, 6);
    }

    [Fact]
    public void RoundTrip_AtTheSamePrices_LosesMoney()
    {
        var book = BookRules.Open(10_000);

        BookRules.Apply(book, Yes(), Order(TradeSide.Buy, 0.1), Now);
        var sold = BookRules.Apply(book, Yes(), Order(TradeSide.Sell, 1), Now).Trade!;

        Assert.True(book.Value < 10_000);
        Assert.True(sold.Realized < 0);
        Assert.Empty(book.Positions);
    }

    [Fact]
    public void PartialSell_KeepsTheRestAtItsCost()
    {
        var book = BookRules.Open(10_000);
        BookRules.Apply(book, Yes(), Order(TradeSide.Buy, 0.1), Now);
        var (units, cost) = (book.Positions[0].Units, book.Positions[0].Cost);

        BookRules.Apply(book, Yes(bid: 0.50, ask: 0.52), Order(TradeSide.Sell, 0.5), Now);

        Assert.Equal(units / 2, book.Positions[0].Units, 6);
        Assert.Equal(cost / 2, book.Positions[0].Cost, 6);
    }

    [Fact]
    public void Buy_IsCappedAtAQuarterOfTheBook()
    {
        var book = BookRules.Open(10_000);

        var trade = BookRules.Apply(book, Yes(bid: 0.42, ask: 0.42), Order(TradeSide.Buy, 1), Now).Trade!;

        Assert.Equal(2_500, trade.Units * trade.Price, 6);
        var again = BookRules.Apply(book, Yes(bid: 0.42, ask: 0.42), Order(TradeSide.Buy, 0.1), Now);
        Assert.Null(again.Trade);
        Assert.Contains("already full", again.Refusal);
    }

    [Fact]
    public void Trade_IsRefusedWhenTheMarketIsShut()
    {
        var book = BookRules.Open(10_000);

        Assert.Null(BookRules.Apply(book, Yes(open: false), Order(TradeSide.Buy, 0.1), Now).Trade);
        Assert.Null(BookRules.Apply(book, Yes(settled: 1), Order(TradeSide.Buy, 0.1), Now).Trade);
        Assert.Equal(10_000, book.Cash);
    }

    [Fact]
    public void Sell_OfNothing_IsRefused()
        => Assert.Contains("holds no", BookRules.Apply(BookRules.Open(10_000), Yes(), Order(TradeSide.Sell, 1), Now).Refusal);

    [Fact]
    public void Mark_ValuesPositionsAtTheBid()
    {
        var book = BookRules.Open(10_000);
        BookRules.Apply(book, Yes(), Order(TradeSide.Buy, 0.1), Now);

        BookRules.Mark(book, new List<Quote> { Yes(bid: 0.60, ask: 0.62) }, Monday, Now);

        Assert.Equal(book.Positions[0].Units * 0.60, book.Positions[0].Value, 6);
    }

    [Fact]
    public void Mark_LeavesAnUnansweredPositionStaleAndUnsettled()
    {
        var book = BookRules.Open(10_000);
        BookRules.Apply(book, Yes(), Order(TradeSide.Buy, 0.1), Now);

        var news = BookRules.Mark(book, new List<Quote>(), Monday, Now);

        Assert.Empty(news.Settled);
        Assert.False(book.Positions[0].Fresh);
        Assert.Equal(0.40, book.Positions[0].Mark);
    }

    [Fact]
    public void Settlement_PaysOutClosesAndScoresTheForecast()
    {
        var book = BookRules.Open(10_000);
        BookRules.Apply(book, Yes(), Order(TradeSide.Buy, 0.1, forecast: 0.7), Now);
        var units = book.Positions[0].Units;
        var cash = book.Cash;

        var news = BookRules.Mark(book, new List<Quote> { Yes(settled: 1, open: false) }, Monday, Now);

        var paid = Assert.Single(news.Settled);
        Assert.Equal(TradeSide.Settled, paid.Side);
        Assert.Equal(units - 1_001, paid.Realized!.Value, 6);
        Assert.Equal(cash + units, book.Cash, 6);
        Assert.Empty(book.Positions);
        Assert.Equal(0.09, BookRules.ScoreOf(book, Guid.Empty)!.Value, 6);
    }

    [Fact]
    public void BigMove_IsToldOncePerDirectionPerDay()
    {
        var book = BookRules.Open(10_000);
        BookRules.Mark(book, new List<Quote>(), Monday, Now);
        BookRules.Apply(book, Yes(bid: 0.40, ask: 0.40), Order(TradeSide.Buy, 0.25), Now);

        var up = BookRules.Mark(book, new List<Quote> { Yes(bid: 0.60, ask: 0.60) }, Monday, Now);
        var still = BookRules.Mark(book, new List<Quote> { Yes(bid: 0.70, ask: 0.70) }, Monday, Now);
        var down = BookRules.Mark(book, new List<Quote> { Yes(bid: 0.10, ask: 0.10) }, Monday, Now);
        var tomorrow = BookRules.Mark(book, new List<Quote> { Yes(bid: 0.10, ask: 0.10) }, Monday.AddDays(1), Now);

        Assert.True(up.BigMove > 0);
        Assert.Null(still.BigMove);
        Assert.True(down.BigMove < 0);
        Assert.Null(tomorrow.BigMove);
    }
}
