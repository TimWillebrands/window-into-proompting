using System.Globalization;
using PartyTown.World.Work.Markets;

namespace PartyTown.World.Work.Companies;

/// <summary>What a Book holds of one Instrument (ADR 0030).</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Companies.Position")]
public sealed class Position
{
    [Id(0)] public string InstrumentId { get; set; } = string.Empty;
    [Id(1)] public string Title { get; set; } = string.Empty;
    [Id(2)] public double Units { get; set; }
    /// <summary>Everything paid for the units still held, fees included.</summary>
    [Id(3)] public double Cost { get; set; }
    /// <summary>Price it could be sold at now.</summary>
    [Id(4)] public double Mark { get; set; }
    /// <summary>False once a read failed: the Mark is old, nothing more.</summary>
    [Id(5)] public bool Fresh { get; set; } = true;

    public double Value => Units * Mark;
}

[GenerateSerializer]
public enum TradeSide
{
    Buy = 0,
    Sell = 1,
    /// <summary>Paid out by the Market, not chosen.</summary>
    Settled = 2,
}

/// <summary>One buy, sell or Settlement in a Book, with the Thesis behind it.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Companies.Trade")]
public sealed class Trade
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public long At { get; set; }
    /// <summary>Null for a Settlement: nobody chose it.</summary>
    [Id(2)] public Guid? TraderId { get; set; }
    [Id(3)] public string? TraderName { get; set; }
    [Id(4)] public string InstrumentId { get; set; } = string.Empty;
    [Id(5)] public string Title { get; set; } = string.Empty;
    [Id(6)] public TradeSide Side { get; set; }
    [Id(7)] public double Units { get; set; }
    [Id(8)] public double Price { get; set; }
    [Id(9)] public double Fee { get; set; }
    /// <summary>Profit or loss this closed; null for a buy.</summary>
    [Id(10)] public double? Realized { get; set; }
    [Id(11)] public string? Thesis { get; set; }
    /// <summary>On a question: the chance the trader gave it of paying out, 0..1.</summary>
    [Id(12)] public double? Forecast { get; set; }
    /// <summary>Brier score of <see cref="Forecast"/>, once the Market settled it.</summary>
    [Id(13)] public double? Score { get; set; }
}

/// <summary>A Company's ghost money. Real prices, pretend fills; never touches a Wallet.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Companies.Book")]
public sealed class Book
{
    [Id(0)] public double Capital { get; set; }
    [Id(1)] public double Cash { get; set; }
    [Id(2)] public List<Position> Positions { get; set; } = new();
    [Id(3)] public List<Trade> Trades { get; set; } = new();
    /// <summary>Local day the day's opening value belongs to.</summary>
    [Id(4)] public string? Day { get; set; }
    [Id(5)] public double DayOpen { get; set; }
    /// <summary>Direction of the big move already told today: -1, 0 or 1.</summary>
    [Id(6)] public int ToldToday { get; set; }

    public double Value => Cash + Positions.Sum(p => p.Value);
}

/// <summary>What someone wants to do; the fill comes from the quote, not from them.</summary>
/// <param name="Share">Buy: part of the Book's value. Sell: part of the Position. 0..1.</param>
[GenerateSerializer, Alias("PartyTown.World.Work.Companies.TradeOrder")]
public sealed record TradeOrder(
    [property: Id(0)] string InstrumentId,
    [property: Id(1)] TradeSide Side,
    [property: Id(2)] double Share,
    [property: Id(3)] Guid? TraderId,
    [property: Id(4)] string? TraderName,
    [property: Id(5)] string? Thesis,
    [property: Id(6)] double? Forecast);

/// <summary>A Trade, or why there wasn't one.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Companies.TradeResult")]
public sealed record TradeResult([property: Id(0)] Trade? Trade, [property: Id(1)] string? Refusal)
{
    public static TradeResult No(string why) => new(null, why);
}

/// <summary>What a refresh turned up that someone should hear about.</summary>
[GenerateSerializer, Alias("PartyTown.World.Work.Companies.BookNews")]
public sealed record BookNews(
    [property: Id(0)] List<Trade> Settled,
    /// <summary>Today's move as a fraction, the first time it crosses the line; null otherwise.</summary>
    [property: Id(1)] double? BigMove)
{
    public bool Any => Settled.Count > 0 || BigMove is not null;
}

/// <summary>Pure rules of a Book (ADR 0030): how Trades fill, Positions mark and Settlements pay.</summary>
public static class BookRules
{
    public const double FeeRate = 0.001;
    /// <summary>No single Instrument may take more of the Book than this.</summary>
    public const double MaxShare = 0.25;
    public const double BigMoveLine = 0.05;
    public const int MaxTrades = 200;
    private const double MinCredits = 1;

    public static Book Open(double capital) => new() { Capital = capital, Cash = capital };

    /// <summary>Buys fill at the ask, sells at the bid, plus a fee: a round trip always costs.</summary>
    public static TradeResult Apply(Book book, Quote quote, TradeOrder order, long now)
    {
        if (quote.Settled is not null || !quote.Open) return TradeResult.No($"The market for {quote.Title} isn't open.");
        if (order.Share is not > 0 or > 1) return TradeResult.No("That's not a share of anything.");
        return order.Side switch
        {
            TradeSide.Buy => Buy(book, quote, order, now),
            TradeSide.Sell => Sell(book, quote, order, now),
            _ => TradeResult.No("Nobody chooses a Settlement."),
        };
    }

    private static TradeResult Buy(Book book, Quote quote, TradeOrder order, long now)
    {
        if (quote.Ask <= 0) return TradeResult.No($"Nobody is offering {quote.Title}.");
        var value = book.Value;
        var held = book.Positions.FirstOrDefault(p => p.InstrumentId == quote.Id);
        var room = value * MaxShare - (held?.Value ?? 0);
        var spend = Math.Min(Math.Min(value * order.Share, room), book.Cash / (1 + FeeRate));
        if (spend < MinCredits) return TradeResult.No(room < MinCredits ? $"The Book is already full of {quote.Title}." : "There's no cash left in the Book.");

        var units = spend / quote.Ask;
        var fee = spend * FeeRate;
        book.Cash -= spend + fee;
        if (held is null)
        {
            held = new Position { InstrumentId = quote.Id, Title = quote.Title };
            book.Positions.Add(held);
        }
        held.Units += units;
        held.Cost += spend + fee;
        held.Mark = quote.Bid;
        held.Fresh = true;
        return new TradeResult(Record(book, new Trade
        {
            Id = Guid.NewGuid(), At = now, TraderId = order.TraderId, TraderName = order.TraderName, InstrumentId = quote.Id, Title = quote.Title,
            Side = TradeSide.Buy, Units = units, Price = quote.Ask, Fee = fee, Thesis = order.Thesis, Forecast = order.Forecast,
        }), null);
    }

    private static TradeResult Sell(Book book, Quote quote, TradeOrder order, long now)
    {
        if (book.Positions.FirstOrDefault(p => p.InstrumentId == quote.Id) is not { } held) return TradeResult.No($"The Book holds no {quote.Title}.");
        var units = held.Units * order.Share;
        var gross = units * quote.Bid;
        var fee = gross * FeeRate;
        var cost = held.Cost * order.Share;
        book.Cash += gross - fee;
        held.Units -= units;
        held.Cost -= cost;
        held.Mark = quote.Bid;
        if (order.Share >= 1) book.Positions.Remove(held);
        return new TradeResult(Record(book, new Trade
        {
            Id = Guid.NewGuid(), At = now, TraderId = order.TraderId, TraderName = order.TraderName, InstrumentId = quote.Id, Title = quote.Title,
            Side = TradeSide.Sell, Units = units, Price = quote.Bid, Fee = fee, Realized = gross - fee - cost, Thesis = order.Thesis, Forecast = order.Forecast,
        }), null);
    }

    /// <summary>Takes in a read: marks what answered, pays out what settled, and leaves the rest stale.</summary>
    public static BookNews Mark(Book book, IReadOnlyList<Quote> quotes, DateTimeOffset local, long now)
    {
        var day = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (book.Day != day)
        {
            book.Day = day;
            book.DayOpen = book.Value;
            book.ToldToday = 0;
        }

        var settled = new List<Trade>();
        var answered = quotes.ToDictionary(q => q.Id);
        foreach (var position in book.Positions.ToList())
        {
            if (!answered.TryGetValue(position.InstrumentId, out var quote))
            {
                position.Fresh = false;
                continue;
            }
            position.Fresh = true;
            if (quote.Settled is { } payout) settled.Add(Settle(book, position, payout, now));
            else position.Mark = quote.Bid;
        }
        return new BookNews(settled, TellBigMove(book));
    }

    private static Trade Settle(Book book, Position position, double payout, long now)
    {
        var proceeds = position.Units * payout;
        book.Cash += proceeds;
        book.Positions.Remove(position);
        // Hold every forecast on this Instrument against what the Market said.
        foreach (var trade in book.Trades.Where(t => t.InstrumentId == position.InstrumentId && t.Forecast is not null && t.Score is null))
            trade.Score = Brier(trade.Forecast!.Value, payout);
        return Record(book, new Trade
        {
            Id = Guid.NewGuid(), At = now, InstrumentId = position.InstrumentId, Title = position.Title,
            Side = TradeSide.Settled, Units = position.Units, Price = payout, Realized = proceeds - position.Cost,
        });
    }

    private static double? TellBigMove(Book book)
    {
        if (book.DayOpen <= 0) return null;
        var move = book.Value / book.DayOpen - 1;
        var direction = Math.Abs(move) < BigMoveLine ? 0 : Math.Sign(move);
        if (direction == 0 || direction == book.ToldToday) return null;
        book.ToldToday = direction;
        return move;
    }

    public static double Brier(double forecast, double outcome) => (forecast - outcome) * (forecast - outcome);

    /// <summary>Mean Brier of one trader's settled forecasts (lower is better); null before any have settled.</summary>
    public static double? ScoreOf(Book book, Guid? traderId)
    {
        var scores = book.Trades.Where(t => t.Score is not null && (traderId is null || t.TraderId == traderId)).Select(t => t.Score!.Value).ToList();
        return scores.Count == 0 ? null : scores.Average();
    }

    private static Trade Record(Book book, Trade trade)
    {
        book.Trades.Add(trade);
        if (book.Trades.Count > MaxTrades) book.Trades.RemoveRange(0, book.Trades.Count - MaxTrades);
        return trade;
    }
}
