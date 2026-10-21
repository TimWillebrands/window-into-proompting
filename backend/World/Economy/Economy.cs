using PartyTown.World.Places;

namespace PartyTown.World.Economy;

/// <summary>Something wearable you can buy: a garment (style + colour) or a service, as Appearance picks.</summary>
[GenerateSerializer, Alias("PartyTown.World.Economy.ShopItem")]
public sealed record ShopItem(
    [property: Id(0)] string Key,
    [property: Id(1)] string Slot,
    [property: Id(2)] string Name,
    [property: Id(3)] int Price,
    /// <summary>What wearing it sets, as <c>SetAppearanceAsync</c> keys: {topStyle: hoodie, top: navy}.</summary>
    [property: Id(4)] Dictionary<string, string> Picks);

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
    private static readonly Dictionary<string, int> TopPrices = new()
    {
        ["tshirt"] = 30, ["tank"] = 25, ["shirt"] = 45, ["sweater"] = 50, ["hoodie"] = 55, ["jacket"] = 90, ["dress"] = 70, ["suit"] = 150,
    };

    private static readonly Dictionary<string, int> BottomPrices = new() { ["trousers"] = 35, ["jeans"] = 45, ["shorts"] = 25, ["skirt"] = 35 };

    private static readonly Dictionary<string, int> AccessoryPrices = new()
    {
        ["glasses"] = 60, ["sunglasses"] = 50, ["hat"] = 45, ["cap"] = 20, ["beanie"] = 25, ["scarf"] = 30, ["headphones"] = 80,
        ["earrings"] = 35, ["flower"] = 5, ["bow"] = 10, ["necklace"] = 60, ["bandana"] = 15,
    };

    private static readonly Dictionary<string, string> Nouns = new()
    {
        ["tshirt"] = "T-shirt", ["tank"] = "tank top", ["glasses"] = "pair of glasses", ["sunglasses"] = "pair of sunglasses",
        ["earrings"] = "pair of earrings", ["flower"] = "flower for the hair", ["bow"] = "hair bow",
    };

    private static readonly Dictionary<string, string> Cuts = new()
    {
        ["bald"] = "a clean shave of the head", ["braids"] = "getting your hair braided", ["buzz"] = "a buzz cut",
        ["bun"] = "a cut long enough for a bun", ["ponytail"] = "a cut long enough for a ponytail", ["mohawk"] = "a mohawk",
        ["afro"] = "an afro", ["long"] = "growing it long (extensions)",
    };

    public static readonly IReadOnlyList<ShopItem> All = Build();
    private static readonly Dictionary<string, ShopItem> ByKey = All.ToDictionary(i => i.Key);

    private static List<ShopItem> Build()
    {
        var items = new List<ShopItem>();
        foreach (var v in AppearanceRules.Hairs) items.Add(new($"hair:{v}", "hair", $"{v} hair dye", 30, new() { ["hair"] = v }));
        foreach (var v in AppearanceRules.Styles)
            items.Add(new($"hairStyle:{v}", "hairStyle", Cuts.GetValueOrDefault(v, $"{An(v)} {v} haircut"), v == "bald" ? 10 : 25, new() { ["hairStyle"] = v }));
        foreach (var style in AppearanceRules.TopStyles)
            foreach (var c in AppearanceRules.Colours)
                items.Add(new($"top:{style}:{c}", "top", $"{An(c)} {c} {Noun(style)}", TopPrices.GetValueOrDefault(style, 40), new() { ["topStyle"] = style, ["top"] = c }));
        foreach (var style in AppearanceRules.BottomStyles)
            foreach (var c in AppearanceRules.Colours)
                items.Add(new($"bottom:{style}:{c}", "bottom", style == "skirt" ? $"{An(c)} {c} skirt" : $"{c} {style}", BottomPrices.GetValueOrDefault(style, 35), new() { ["bottomStyle"] = style, ["bottom"] = c }));
        foreach (var c in AppearanceRules.Colours) items.Add(new($"shoes:{c}", "shoes", $"{c} shoes", 40, new() { ["shoes"] = c }));
        foreach (var v in AppearanceRules.Accessories.Where(a => a != "none"))
            items.Add(new($"accessory:{v}", "accessory", $"{An(Noun(v))} {Noun(v)}", AccessoryPrices.GetValueOrDefault(v, 40), new() { ["accessory"] = v }));
        return items;
    }

    private static string Noun(string value) => Nouns.GetValueOrDefault(value, value);

    private static string An(string word) => "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an" : "a";

    /// <summary>The item for a key; v1 keys ("top:red") read as the plain garment they were (a red T-shirt).</summary>
    public static ShopItem? Find(string key)
    {
        if (ByKey.TryGetValue(key, out var item)) return item;
        var parts = key.Split(':');
        return parts.Length == 2 && parts[0] is "top" or "bottom"
            ? ByKey.GetValueOrDefault(parts[0] == "top" ? $"top:tshirt:{parts[1]}" : $"bottom:trousers:{parts[1]}")
            : null;
    }

    /// <summary>The current key for a stored one (v1 keys migrate), or null when it's gone from the palette.</summary>
    public static string? Normalize(string key) => Find(key)?.Key;

    /// <summary>Hair dye and haircuts are services: you get them done, you don't own them.</summary>
    public static bool IsService(ShopItem item) => item.Slot is "hair" or "hairStyle";

    /// <summary>The keys of what someone is wearing now (body: skin, build, eyes, facial hair are never for sale).</summary>
    public static List<string> Wearing(Appearance look) => new()
    {
        $"hair:{look.Hair}", $"hairStyle:{look.HairStyle}", $"top:{look.TopStyle}:{look.Top}", $"bottom:{look.BottomStyle}:{look.Bottom}",
        $"shoes:{look.Shoes}", $"accessory:{look.Accessory}",
    };

    /// <summary>Today's window display for one person: a stable handful, different per day and per person.</summary>
    public static List<ShopItem> InTheShops(Guid personaId, string dayKey, IReadOnlyCollection<string> owned, int count = 5)
    {
        var rng = new Random(HashCode.Combine(personaId, dayKey));
        return All.Where(i => !owned.Contains(i.Key)).OrderBy(_ => rng.Next()).Take(count).ToList();
    }

    /// <summary>The item an intent names, by word overlap with the item names; null when none is clearly meant.</summary>
    public static ShopItem? Match(string intent, IReadOnlyList<ShopItem> options)
    {
        var words = Words(intent);
        // The thing ("scarf", "hoodie") outweighs the colour, which several slots share.
        var scored = options.Select(o => (Item: o, Score: Words(o.Name).Where(words.Contains).Sum(w => AppearanceRules.Colours.Contains(w) || AppearanceRules.Hairs.Contains(w) ? 1 : 2)))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ToList();
        return scored.Count == 0 || (scored.Count > 1 && scored[0].Score == scored[1].Score) ? null : scored[0].Item;
    }

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal) { "a", "an", "the", "of", "pair", "some", "new", "for", "top" };

    private static HashSet<string> Words(string text)
        => System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).Where(w => !Filler.Contains(w)).ToHashSet();
}
