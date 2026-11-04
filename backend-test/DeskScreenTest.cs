using System.Text.Json.Nodes;
using PartyTown.World.Work;
using PartyTown.World.Work.Companies;
using PartyTown.World.Work.Markets;
using PartyTown.World.Work.Practices;

namespace BackendTest;

/// <summary>What a trader sees and how an intent becomes an order (ADR 0030); and the job rules around them.</summary>
public class DeskScreenTest
{
    private static readonly Guid Ana = Guid.NewGuid();

    private static Company Polder()
    {
        var company = new Company { Name = "Polder Capital", Mandate = "trades rain", Market = MarketKinds.Predictions, Book = BookRules.Open(10_000) };
        company.Watchlist.Add(new Quote { Id = "a", Title = "Will it rain? (Yes)", Bid = 0.40, Ask = 0.42, Previous = 0.30, Open = true });
        company.Watchlist.Add(new Quote { Id = "b", Title = "Will it snow? (Yes)", Bid = 0.10, Ask = 0.12, Open = true });
        company.Watchlist.Add(new Quote { Id = "c", Title = "Shut for the night", Bid = 0.5, Ask = 0.5, Open = false });
        return company;
    }

    private static JsonObject Decision(string line = "1", string side = "buy", string percent = "10", string chance = "70")
        => new() { ["line"] = line, ["side"] = side, ["percent"] = percent, ["chance"] = chance, ["thesis"] = "it always rains" };

    [Fact]
    public void Lines_PutWhatsHeldFirstAndLeaveOutWhatsShut()
    {
        var company = Polder();
        BookRules.Apply(company.Book, company.Watchlist[1], new TradeOrder("b", TradeSide.Buy, 0.1, Ana, "Ana", null, null), 1);

        var lines = DeskScreen.Lines(company);

        Assert.Equal(new[] { "b", "a" }, lines.Select(l => l.InstrumentId));
        Assert.NotNull(lines[0].Held);
    }

    [Fact]
    public void Render_GivesFactsAndNumbersLinesForTheOrder()
    {
        var company = Polder();

        var text = DeskScreen.Render(company, DeskScreen.Lines(company));

        Assert.Contains("10,000 credits", text);
        Assert.Contains("1. Will it rain? (Yes): 0.41, +36.7% today", text);
        Assert.Contains("2. Will it snow? (Yes)", text);
    }

    [Fact]
    public void Read_TurnsADecisionIntoAnOrder()
    {
        var lines = DeskScreen.Lines(Polder());

        var order = DeskScreen.Read(Decision(line: "2", side: "SELL", percent: "50%", chance: "120"), lines, MarketKinds.Predictions, Ana, "Ana")!;

        Assert.Equal("b", order.InstrumentId);
        Assert.Equal(TradeSide.Sell, order.Side);
        Assert.Null(DeskScreen.Read(Decision(percent: "half"), lines, MarketKinds.Predictions, Ana, "Ana"));
        Assert.Equal(1, DeskScreen.Read(Decision(chance: "120"), lines, MarketKinds.Predictions, Ana, "Ana")!.Forecast);
    }

    [Theory]
    [InlineData("none", "buy", "10")]
    [InlineData("9", "buy", "10")]
    [InlineData("1", "hold", "10")]
    [InlineData("1", "buy", "0")]
    public void Read_NothingOrNonsense_IsNoOrder(string line, string side, string percent)
        => Assert.Null(DeskScreen.Read(Decision(line, side, percent), DeskScreen.Lines(Polder()), MarketKinds.Predictions, Ana, "Ana"));

    [Fact]
    public void Read_OnlyQuestionsCarryAForecast()
        => Assert.Null(DeskScreen.Read(Decision(), DeskScreen.Lines(Polder()), MarketKinds.Crypto, Ana, "Ana")!.Forecast);

    [Fact]
    public void Read_ClipsAndCleansTheThesis()
    {
        var json = Decision();
        json["thesis"] = new string('x', 900);

        Assert.True(DeskScreen.Read(json, DeskScreen.Lines(Polder()), MarketKinds.Predictions, Ana, "Ana")!.Thesis!.Length <= 301);
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(1_000, 0.8)]
    [InlineData(-1_000, 0.2)]
    [InlineData(100, 0.55)]
    public void Better_ScalesWithTheShareOfTheBook(double amount, double better)
        => Assert.Equal(better, DeskRules.Better(amount, 10_000), 6);

    [Fact]
    public void Carry_KeepsWhatTheJobIsWhenHoursChange()
    {
        var company = Guid.NewGuid();
        var next = new Occupation();

        WorkRules.Carry(next, new Occupation { CompanyId = company });

        Assert.Equal(company, next.CompanyId);
    }

    [Fact]
    public void Carry_ACompanyAndAPracticeNeverMix()
    {
        var hired = new Occupation { CompanyId = Guid.NewGuid() };
        WorkRules.Carry(hired, new Occupation { Practice = new Practice() });
        Assert.Null(hired.Practice);

        var settingUp = new Occupation { Practice = new Practice() };
        WorkRules.Carry(settingUp, new Occupation { CompanyId = Guid.NewGuid() });
        Assert.Null(settingUp.CompanyId);
    }

    [Theory]
    [InlineData(WorkApp.AppId, 0)]
    [InlineData(DeskApp.AppId, 0)]
    [InlineData(PracticeApp.AppId, 0)]
    [InlineData("board", 1)]
    public void Damping_SpareTheAppsThatAreTheJob(string app, int damping) => Assert.Equal(damping, Shifts.Damping(app, onShift: true));
}
