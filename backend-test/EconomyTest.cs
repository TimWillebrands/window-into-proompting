using PartyTown.World.Economy;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>Money, the catalog and how wanting a thing becomes buying it (ADR 0024).</summary>
public class EconomyTest
{
    [Theory]
    [InlineData(Money.Tight, 30)]
    [InlineData(Money.GettingBy, 50)]
    [InlineData(Money.Comfortable, 80)]
    [InlineData(null, 50)]
    public void DailyPay_FollowsMeans(string? means, int pay) => Assert.Equal(pay, Money.DailyPay(means));

    [Fact]
    public void Cue_OnlySpeaksWhenMoneyIsOnYourMind()
    {
        Assert.Contains("tight", Money.Cue(10, Money.GettingBy, paidToday: true));
        Assert.Contains("tomorrow's", Money.Cue(10, Money.GettingBy, paidToday: true));
        Assert.Null(Money.Cue(120, Money.GettingBy, paidToday: true));
        Assert.Contains("put by", Money.Cue(500, Money.GettingBy, paidToday: true));
    }

    [Fact]
    public void MeansFrom_TakesAClearWinnerElseGettingBy()
    {
        Assert.Equal(Money.Tight, Money.MeansFrom(new Dictionary<string, double> { [Money.Tight] = 0.7, [Money.GettingBy] = 0.2, [Money.Comfortable] = 0.1 }));
        Assert.Equal(Money.GettingBy, Money.MeansFrom(new Dictionary<string, double> { [Money.Tight] = 0.35, [Money.GettingBy] = 0.33, [Money.Comfortable] = 0.32 }));
        Assert.Equal(Money.GettingBy, Money.MeansFrom(null));
    }

    [Fact]
    public void Catalog_CoversThePaletteButNeverSkin()
    {
        Assert.Contains(Catalog.All, i => i.Key == "accessory:scarf" && i.Price == 30);
        Assert.Contains(Catalog.All, i => i.Key == "top:green");
        Assert.DoesNotContain(Catalog.All, i => i.Slot == "skin" || i.Value == "none");
        Assert.Equal(Catalog.All.Count, Catalog.All.Select(i => i.Key).Distinct().Count());
    }

    [Theory]
    [InlineData("treat myself to that green scarf", "accessory:scarf")]
    [InlineData("a red top for the weekend", "top:red")]
    [InlineData("dye my hair pink, why not", "hair:pink")]
    public void Match_FindsTheMeantItem(string intent, string key)
        => Assert.Equal(key, Catalog.Match(intent, Catalog.All)?.Key);

    [Fact]
    public void Match_IsNullWhenNothingIsMeant()
        => Assert.Null(Catalog.Match("just browsing really", Catalog.All));

    [Fact]
    public void InTheShops_IsStableForTheDayAndSkipsWhatYouOwn()
    {
        var id = Guid.NewGuid();
        var owned = new[] { "accessory:scarf" };
        var a = Catalog.InTheShops(id, "2026-10-21", owned);
        Assert.Equal(a, Catalog.InTheShops(id, "2026-10-21", owned));
        Assert.Equal(5, a.Count);
        Assert.DoesNotContain(a, i => owned.Contains(i.Key));
        Assert.NotEqual(a, Catalog.InTheShops(id, "2026-10-22", owned));
    }

    [Fact]
    public void Wearing_ListsTheLookAsKeys()
    {
        var look = new Appearance { Hair = "red", HairStyle = "bun", Top = "teal", Bottom = "black", Accessory = "glasses" };
        Assert.Equal(new[] { "hair:red", "hairStyle:bun", "top:teal", "bottom:black", "accessory:glasses" }, Catalog.Wearing(look));
    }

    [Fact]
    public void Render_ShowsFreshGiftsAndTightMoney()
    {
        var now = DateTimeOffset.UtcNow;
        var wallet = new WalletView(5, Money.GettingBy, 50, true, new List<string>(), new List<LedgerEntry>(),
            new List<GiftNote> { new() { Key = "accessory:scarf", FromName = "Denise", At = now.AddHours(-2).ToUnixTimeMilliseconds() } });
        var text = Money.Render(wallet, now);
        Assert.Contains("# Money and things", text);
        Assert.Contains("Money's tight", text);
        Assert.Contains("Denise gave you a scarf", text);
        Assert.Equal(string.Empty, Money.Render(wallet with { Balance = 100, NewGifts = new List<GiftNote>() }, now));
    }
}
