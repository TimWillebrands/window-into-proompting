using PartyTown.World.Places;

namespace PartyTown.World.Economy;

/// <summary>Something wearable you can buy: one palette value in one Appearance slot.</summary>
[GenerateSerializer, Alias("PartyTown.World.Economy.ShopItem")]
public sealed record ShopItem(
    [property: Id(0)] string Key,
    [property: Id(1)] string Slot,
    [property: Id(2)] string Value,
    [property: Id(3)] string Name,
    [property: Id(4)] int Price);

/// <summary>Pure money rules (ADR 0024): means, pay, the catalog and how money reads in a prompt.</summary>
public static class Money
{
    public const string Tight = "tight";
    public const string GettingBy = "getting by";
    public const string Comfortable = "comfortable";
    public static readonly string[] AllMeans = { Tight, GettingBy, Comfortable };

    public static int DailyPay(string? means) => means switch
    {
        Tight => 30,
        Comfortable => 80,
        _ => 50,
    };

    public static int StartingBalance(string? means) => DailyPay(means) * 3;

    public const long GiftFreshMs = 12 * 3_600_000L;

    public static string DayKey(DateTimeOffset now) => now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>One short line for "Right now"; silent when money isn't on your mind.</summary>
    public static string? Cue(int balance, string? means, bool paidToday)
    {
        var pay = DailyPay(means);
        if (balance < pay / 2) return $"Money's tight: {balance} credits until {(paidToday ? "tomorrow's" : "the next")} payday.";
        if (balance >= pay * 6) return $"You've got some money put by ({balance} credits).";
        return null;
    }

    /// <summary>Money and fresh gifts as lines for "Right now"; empty when there's nothing to say.</summary>
    public static string Render(WalletView wallet, DateTimeOffset now)
    {
        var lines = new List<string>();
        if (Cue(wallet.Balance, wallet.Means, wallet.PaidToday) is { } cue) lines.Add(cue);
        foreach (var g in wallet.NewGifts)
            lines.Add($"{g.FromName} gave you {Catalog.Find(g.Key)?.Name ?? "something"} {WorldStream.Ago(g.At, now)}.");
        return lines.Count == 0 ? string.Empty : $"# Money and things\n{string.Join("\n", lines)}\n";
    }

    /// <summary>Most likely means from a System One choice; getting by when unsure.</summary>
    public static string MeansFrom(IReadOnlyDictionary<string, double>? choice)
        => choice is { Count: > 0 } && choice.MaxBy(c => c.Value) is { Value: >= 0.4 } top && AllMeans.Contains(top.Key) ? top.Key : GettingBy;
}

/// <summary>What the shops sell, built from the Appearance palette so new palette values are buyable automatically.</summary>
public static class Catalog
{
    private static readonly Dictionary<string, int> AccessoryPrices = new()
    {
        ["glasses"] = 60, ["hat"] = 45, ["beanie"] = 25, ["scarf"] = 30, ["headphones"] = 80,
    };

    public static readonly IReadOnlyList<ShopItem> All = Build();

    private static List<ShopItem> Build()
    {
        var items = new List<ShopItem>();
        foreach (var v in AppearanceRules.Hairs) items.Add(Item("hair", v, $"{v} hair dye", 30));
        foreach (var v in AppearanceRules.Styles) items.Add(Item("hairStyle", v, v == "bald" ? "a clean shave of the head" : $"a {v} haircut", v == "bald" ? 10 : 25));
        foreach (var v in AppearanceRules.Colours) items.Add(Item("top", v, $"a {v} top", 40));
        foreach (var v in AppearanceRules.Colours) items.Add(Item("bottom", v, $"{v} trousers", 35));
        foreach (var v in AppearanceRules.Accessories.Where(a => a != "none"))
            items.Add(Item("accessory", v, v == "glasses" ? "a pair of glasses" : v == "headphones" ? "headphones" : $"a {v}", AccessoryPrices.GetValueOrDefault(v, 40)));
        return items;
    }

    private static ShopItem Item(string slot, string value, string name, int price) => new($"{slot}:{value}", slot, value, name, price);

    public static ShopItem? Find(string key) => All.FirstOrDefault(i => i.Key == key);

    /// <summary>Hair dye and haircuts are services: you get them done, you don't own them.</summary>
    public static bool IsService(ShopItem item) => item.Slot is "hair" or "hairStyle";

    /// <summary>The keys of what someone is wearing now (skin is never for sale).</summary>
    public static List<string> Wearing(Appearance look) => new()
    {
        $"hair:{look.Hair}", $"hairStyle:{look.HairStyle}", $"top:{look.Top}", $"bottom:{look.Bottom}", $"accessory:{look.Accessory}",
    };

    /// <summary>Today's window display for one person: a stable handful, different per day and per person.</summary>
    public static List<ShopItem> InTheShops(Guid personaId, string dayKey, IReadOnlyCollection<string> owned, int count = 5)
    {
        var seed = HashCode.Combine(personaId, dayKey);
        var rng = new Random(seed);
        return All.Where(i => !owned.Contains(i.Key)).OrderBy(_ => rng.Next()).Take(count).ToList();
    }

    /// <summary>The item an intent names, by word overlap with the item names; null when none is clearly meant.</summary>
    public static ShopItem? Match(string intent, IReadOnlyList<ShopItem> options)
    {
        var words = Words(intent);
        // The thing ("scarf", "top") outweighs the colour, which several slots share.
        var scored = options.Select(o => (Item: o, Score: Words(o.Name).Where(words.Contains).Sum(w => AppearanceRules.Colours.Contains(w) || AppearanceRules.Hairs.Contains(w) ? 1 : 2)))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ToList();
        return scored.Count == 0 || (scored.Count > 1 && scored[0].Score == scored[1].Score) ? null : scored[0].Item;
    }

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal) { "a", "an", "the", "of", "pair", "some", "new" };

    private static HashSet<string> Words(string text)
        => System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).Where(w => !Filler.Contains(w)).ToHashSet();
}
