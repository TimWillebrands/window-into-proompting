using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Economy;

/// <summary>
/// One participant's money and wardrobe in a Party (ADR 0024), key = <c>party:participant</c>.
/// Paid once per day by their means; owns the clothes they've bought or been given.
/// </summary>
public sealed class WalletGrain(
    [PersistentState(stateName: "wallet", storageName: "parties")]
    IPersistentState<WalletState> state,
    ISystemOneJudge judge,
    ILogger<WalletGrain> logger)
    : Grain, IWalletGrain
{
    private const int LedgerSize = 40;

    public async Task<WalletView> GetAsync()
    {
        await EnsureAsync();
        return View();
    }

    public async Task<bool> SpendAsync(int amount, string reason)
    {
        await EnsureAsync();
        if (amount < 0 || state.State.Balance < amount) return false;
        await PostAsync(-amount, reason);
        return true;
    }

    public async Task EarnAsync(int amount, string reason)
    {
        await EnsureAsync();
        await PostAsync(amount, reason);
    }

    public async Task SetEmployedAsync(bool employed)
    {
        await EnsureAsync();
        if (state.State.Employed == employed) return;
        state.State.Employed = employed;
        await state.WriteStateAsync();
    }

    public async Task AddToWardrobeAsync(string key, Guid? fromId, string? fromName)
    {
        await EnsureAsync();
        state.State.Wardrobe = state.State.Wardrobe.Select(Catalog.Normalize).OfType<string>().Distinct().ToList();
        if (!state.State.Wardrobe.Contains(key)) state.State.Wardrobe.Add(key);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (fromId is { } from)
            state.State.Gifts.Add(new GiftNote { Key = key, FromId = from, FromName = fromName ?? "someone", At = now });
        state.State.Gifts.RemoveAll(g => now - g.At > 3 * 86_400_000L);
        await state.WriteStateAsync();
    }

    private async Task PostAsync(int amount, string reason)
    {
        state.State.Balance += amount;
        state.State.Ledger.Add(new LedgerEntry { Amount = amount, Reason = reason, At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Balance = state.State.Balance });
        if (state.State.Ledger.Count > LedgerSize) state.State.Ledger.RemoveRange(0, state.State.Ledger.Count - LedgerSize);
        await state.WriteStateAsync();
    }

    /// <summary>First use: means, a starting balance and a wardrobe of what they're wearing. Then payday once a day.</summary>
    private async Task EnsureAsync()
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var dirty = false;
        if (!state.State.Opened)
        {
            var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
            state.State.Means = await MeansAsync(who);
            state.State.Balance = Money.StartingBalance(state.State.Means);
            var body = (await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == participantId);
            var look = body?.Appearance ?? Places.AppearanceRules.Default(participantId);
            state.State.Wardrobe = Catalog.Wearing(look).Where(k => Catalog.Find(k) is { } i && !Catalog.IsService(i)).ToList();
            state.State.Opened = true;
            state.State.PaidDay = Money.DayKey(DateTimeOffset.Now);
            dirty = true;
        }
        var today = Money.DayKey(DateTimeOffset.Now);
        if (state.State.PaidDay != today)
        {
            state.State.PaidDay = today;
            // People with a job are paid per shift (ADR 0026); flat daily pay is for everyone else.
            if (!state.State.Employed)
            {
                await PostAsync(Money.DailyPay(state.State.Means), "payday");
                return;
            }
            dirty = true;
        }
        if (dirty) await state.WriteStateAsync();
    }

    private async Task<string> MeansAsync(CastMember? who)
    {
        if (who is null || who.DefaultDriver != DriverKind.LLM || !judge.Enabled) return Money.GettingBy;
        var sheet = Self.LivedSheets.For(who.Id, who.SystemPrompt) ?? who.Bio;
        if (string.IsNullOrWhiteSpace(sheet)) return Money.GettingBy;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var answers = await judge.AskAsync(sheet, new Dictionary<string, SystemOneQuestion>
            {
                ["means"] = new ChoiceQuestion($"Money-wise, how is {who.Name} doing day to day?", new Dictionary<string, string?>
                {
                    [Money.Tight] = "money is tight, they watch every euro",
                    [Money.GettingBy] = "getting by, nothing fancy",
                    [Money.Comfortable] = "comfortable, they can treat themselves",
                }),
            }, cts.Token);
            var means = Money.MeansFrom(answers.Choices.GetValueOrDefault("means"));
            logger.LogInformation("Wallet for {Name}: means {Means}", who.Name, means);
            return means;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Means read failed for {Name}", who.Name);
            return Money.GettingBy;
        }
    }

    private WalletView View() => new(
        state.State.Balance,
        state.State.Means ?? Money.GettingBy,
        Money.DailyPay(state.State.Means),
        state.State.PaidDay == Money.DayKey(DateTimeOffset.Now),
        state.State.Wardrobe.Select(Catalog.Normalize).OfType<string>().Distinct().ToList(),
        state.State.Ledger.TakeLast(10).Reverse().ToList(),
        state.State.Gifts.Where(g => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - g.At < Money.GiftFreshMs).ToList(),
        state.State.Employed);
}

[Alias("PartyTown.World.Economy.IWalletGrain")]
public interface IWalletGrain : IGrainWithStringKey
{
    [Alias("GetAsync")] Task<WalletView> GetAsync();
    /// <summary>False (and nothing changes) when they can't afford it.</summary>
    [Alias("SpendAsync")] Task<bool> SpendAsync(int amount, string reason);
    [Alias("EarnAsync")] Task EarnAsync(int amount, string reason);
    /// <summary>Employed people are paid per shift, not daily (ADR 0026).</summary>
    [Alias("SetEmployedAsync")] Task SetEmployedAsync(bool employed);
    /// <summary>Own a catalog item; with a giver it's also a gift note until seen.</summary>
    [Alias("AddToWardrobeAsync")] Task AddToWardrobeAsync(string key, Guid? fromId, string? fromName);
}

[GenerateSerializer, Alias("PartyTown.World.Economy.WalletView")]
public sealed record WalletView(
    [property: Id(0)] int Balance,
    [property: Id(1)] string Means,
    [property: Id(2)] int DailyPay,
    [property: Id(3)] bool PaidToday,
    [property: Id(4)] List<string> Wardrobe,
    [property: Id(5)] List<LedgerEntry> Ledger,
    /// <summary>Gifts from the last 12 hours.</summary>
    [property: Id(6)] List<GiftNote> NewGifts,
    [property: Id(7)] bool Employed = false);

[GenerateSerializer, Alias("PartyTown.World.Economy.WalletState")]
public sealed class WalletState
{
    [Id(0)] public bool Opened { get; set; }
    [Id(1)] public int Balance { get; set; }
    [Id(2)] public string? Means { get; set; }
    [Id(3)] public string? PaidDay { get; set; }
    [Id(4)] public List<string> Wardrobe { get; set; } = new();
    [Id(5)] public List<LedgerEntry> Ledger { get; set; } = new();
    [Id(6)] public List<GiftNote> Gifts { get; set; } = new();
    [Id(7)] public bool Employed { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Economy.LedgerEntry")]
public sealed class LedgerEntry
{
    [Id(0)] public int Amount { get; set; }
    [Id(1)] public string Reason { get; set; } = string.Empty;
    [Id(2)] public long At { get; set; }
    [Id(3)] public int Balance { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Economy.GiftNote")]
public sealed class GiftNote
{
    [Id(0)] public string Key { get; set; } = string.Empty;
    [Id(1)] public Guid FromId { get; set; }
    [Id(2)] public string FromName { get; set; } = string.Empty;
    [Id(3)] public long At { get; set; }
}
