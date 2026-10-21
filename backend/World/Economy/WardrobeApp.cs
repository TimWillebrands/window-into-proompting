using System.Text;
using PartyTown.World.Apps;
using PartyTown.World.Places;

namespace PartyTown.World.Economy;

/// <summary>
/// Clothes and money in a Life (ADR 0024): change into something you own, buy something new, or get someone a
/// present. Never pushed: buying is a low-priority option that money, mood and payday make more or less likely.
/// </summary>
public sealed class WardrobeApp(IGrainFactory grains, Services.SystemOne.ISystemOneJudge judge, ILogger<WardrobeApp> logger) : IWorldApp
{
    public string Id => "wardrobe";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var wallet = await WalletOf(ctx.PartyId, ctx.Self.Id).GetAsync();
        var look = await LookAsync(ctx.PartyId, ctx.Self.Id);
        var wearing = Catalog.Wearing(look);
        var shops = Catalog.InTheShops(ctx.Self.Id, Money.DayKey(ctx.Now), wallet.Wardrobe);
        var spare = wallet.Wardrobe.Where(k => !wearing.Contains(k)).Select(Catalog.Find).OfType<ShopItem>().ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"You have {wallet.Balance} credits ({(wallet.PaidToday ? "paid today" : "payday is due")}; you earn about {wallet.DailyPay} a day).");
        sb.AppendLine($"Wearing: {Describe(look)}.");
        if (spare.Count > 0) sb.AppendLine($"Also in your wardrobe: {string.Join(", ", spare.Select(i => i.Name))}.");
        foreach (var g in wallet.NewGifts) sb.AppendLine($"New: {Catalog.Find(g.Key)?.Name ?? "something"}, a present from {g.FromName}.");
        if (shops.Count > 0) sb.AppendLine($"In the shops today: {string.Join(", ", shops.Select(i => $"{i.Name} ({i.Price})"))}.");

        var affordances = new List<Affordance>();
        if (spare.Count > 0)
            affordances.Add(new Affordance(Id, "Change into something else from your wardrobe", wallet.NewGifts.Count > 0 ? 1 : 0, null, "wear"));
        var cheapest = shops.Count == 0 ? int.MaxValue : shops.Min(i => i.Price);
        // Spending follows means: someone flush buys more readily than someone counting coins.
        if (wallet.Balance >= cheapest)
            affordances.Add(new Affordance(Id, "Buy something new to wear", wallet.Balance >= wallet.DailyPay * 3 ? 1 : 0, null, "buy"));
        if (wallet.Balance >= cheapest * 2 && ctx.Others.OrderBy(_ => Random.Shared.Next()).FirstOrDefault() is { } friend)
            affordances.Add(new Affordance(Id, $"Get {friend.Name} a little present", 0, friend.Id, "gift"));

        return new AppPerception(Id, "Wardrobe and money", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        var wallet = await WalletOf(ctx.PartyId, ctx.Self.Id).GetAsync();
        var look = await LookAsync(ctx.PartyId, ctx.Self.Id);
        var wearing = Catalog.Wearing(look);
        var shops = Catalog.InTheShops(ctx.Self.Id, Money.DayKey(ctx.Now), wallet.Wardrobe);
        var text = $"{intent} {thought}";
        switch (affordance.Verb)
        {
            case "wear":
            {
                var spare = wallet.Wardrobe.Where(k => !wearing.Contains(k)).Select(Catalog.Find).OfType<ShopItem>().ToList();
                var item = Catalog.Match(text, spare) ?? await PickAsync(text, spare);
                return item is null ? null : await WearAsync(ctx.PartyId, ctx.Self.Id, item);
            }
            case "buy":
            {
                var item = Catalog.Match(text, shops) ?? Catalog.Match(text, Catalog.All.Where(i => !wallet.Wardrobe.Contains(i.Key)).ToList())
                    ?? await PickAsync(text, shops.Where(i => i.Price <= wallet.Balance).ToList());
                return item is null ? null : await BuyAsync(ctx.PartyId, ctx.Self.Id, item);
            }
            case "gift" when affordance.TargetId is { } to:
            {
                var item = Catalog.Match(text, Catalog.All.Where(i => !Catalog.IsService(i)).ToList())
                    ?? await PickAsync($"A present for {ctx.NameOf(to)}. {text}", shops.Where(i => !Catalog.IsService(i) && i.Price <= wallet.Balance).ToList());
                return item is null ? null : await GiftAsync(ctx.PartyId, ctx.Self.Id, ctx.Self.Name, to, ctx.NameOf(to), item);
            }
        }
        return null;
    }

    /// <summary>Put on something you own (free).</summary>
    public async Task<ActOutcome?> WearAsync(Guid partyId, Guid who, ShopItem item)
    {
        var wallet = await WalletOf(partyId, who).GetAsync();
        if (!wallet.Wardrobe.Contains(item.Key)) return null;
        await grains.GetGrain<IPlacesGrain>(partyId).SetAppearanceAsync(who, Picks(item));
        return new ActOutcome("wear", $"changed into {item.Name}", null);
    }

    /// <summary>Pay, own it (unless it's a service like a haircut) and put it on; null when they can't afford it.</summary>
    public async Task<ActOutcome?> BuyAsync(Guid partyId, Guid who, ShopItem item)
    {
        var wallet = WalletOf(partyId, who);
        if (!await wallet.SpendAsync(item.Price, item.Name)) return null;
        if (!Catalog.IsService(item)) await wallet.AddToWardrobeAsync(item.Key, null, null);
        await grains.GetGrain<IPlacesGrain>(partyId).SetAppearanceAsync(who, Picks(item));
        return new ActOutcome("buy", Catalog.IsService(item) ? $"got {item.Name} ({item.Price} credits)" : $"bought {item.Name} ({item.Price} credits) and put it on", null);
    }

    /// <summary>Pay for something and put it in someone else's wardrobe; they'll feel a little warmer toward the giver.</summary>
    public async Task<ActOutcome?> GiftAsync(Guid partyId, Guid from, string fromName, Guid to, string toName, ShopItem item)
    {
        if (Catalog.IsService(item) || !await WalletOf(partyId, from).SpendAsync(item.Price, $"{item.Name} for {toName}")) return null;
        await WalletOf(partyId, to).AddToWardrobeAsync(item.Key, from, fromName);
        try { await grains.GetGrain<Self.IPersonaTiesGrain>(WorldKeys.Life(partyId, to)).ShiftWarmthAsync(from, 0.85); }
        catch (Exception ex) { logger.LogDebug(ex, "Gift warmth failed"); }
        return new ActOutcome("gift", $"bought {toName} {item.Name} as a present", null, to);
    }

    /// <summary>The option an intent that names no item most likely means (System One); null when there's nothing to pick.</summary>
    private async Task<ShopItem?> PickAsync(string intent, IReadOnlyList<ShopItem> options)
    {
        if (options.Count <= 1 || !judge.Enabled) return options.FirstOrDefault();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var labels = options.Select((o, i) => (Key: $"o{i}", o)).ToList();
            var answers = await judge.AskAsync(intent, new Dictionary<string, Services.SystemOne.SystemOneQuestion>
            {
                ["pick"] = new Services.SystemOne.ChoiceQuestion("Which of these fits what they have in mind best?",
                    labels.ToDictionary(l => l.Key, l => (string?)l.o.Name)),
            }, cts.Token);
            var best = answers.Choices.GetValueOrDefault("pick")?.MaxBy(c => c.Value).Key;
            return labels.FirstOrDefault(l => l.Key == best).o ?? options[0];
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Wardrobe pick failed");
            return options[0];
        }
    }

    private IWalletGrain WalletOf(Guid partyId, Guid who) => grains.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, who));

    private async Task<Appearance> LookAsync(Guid partyId, Guid who)
        => (await grains.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == who)?.Appearance
            ?? AppearanceRules.Default(who);

    private static Dictionary<string, string?> Picks(ShopItem item) => item.Picks.ToDictionary(p => p.Key, p => (string?)p.Value);

    public static string Describe(Appearance look)
    {
        var parts = new List<string> { $"{look.Hair} {look.HairStyle} hair" };
        parts.AddRange(Catalog.Wearing(look).Skip(2).Select(Catalog.Find).OfType<ShopItem>().Select(i => i.Name));
        return string.Join(", ", parts);
    }
}
