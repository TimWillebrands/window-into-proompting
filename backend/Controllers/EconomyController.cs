using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Economy;

namespace PartyTown.Controllers;

/// <summary>Money and wardrobes (ADR 0024): everyone's wallet, today's shop, and buying/wearing/gifting for the Player.</summary>
[ApiController]
[Route("worlds/{partyId:guid}")]
public sealed class EconomyController(IGrainFactory grains, WardrobeApp wardrobe) : ControllerBase
{
    [HttpGet("wallets")]
    public async Task<ActionResult<List<WalletSummary>>> GetWallets(Guid partyId)
    {
        var cast = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Where(c => c.Id != Narrator.PersonaId).ToList();
        var wallets = await Task.WhenAll(cast.Select(c => Wallet(partyId, c.Id).GetAsync()));
        var looks = await LooksAsync(partyId);
        return Ok(cast.Select((c, i) => Summary(c.Id, c.Name, wallets[i], looks)).ToList());
    }

    /// <summary>What's in the shops today for one participant (their own window display).</summary>
    [HttpGet("wallets/{participantId:guid}/shop")]
    public async Task<ActionResult<List<ShopItem>>> GetShop(Guid partyId, Guid participantId)
    {
        var wallet = await Wallet(partyId, participantId).GetAsync();
        return Ok(Catalog.InTheShops(participantId, Money.DayKey(DateTimeOffset.Now), wallet.Wardrobe, count: 12));
    }

    [HttpPost("wallets/{participantId:guid}/buy")]
    public Task<ActionResult<WalletSummary>> Buy(Guid partyId, Guid participantId, [FromBody] WardrobeRequest request)
        => ActAsync(partyId, participantId, request, item => wardrobe.BuyAsync(partyId, participantId, item), "Can't afford that.");

    [HttpPost("wallets/{participantId:guid}/wear")]
    public Task<ActionResult<WalletSummary>> Wear(Guid partyId, Guid participantId, [FromBody] WardrobeRequest request)
        => ActAsync(partyId, participantId, request, item => wardrobe.WearAsync(partyId, participantId, item), "That's not in the wardrobe.");

    [HttpPost("wallets/{participantId:guid}/gift")]
    public async Task<ActionResult<WalletSummary>> Gift(Guid partyId, Guid participantId, [FromBody] WardrobeRequest request)
    {
        if (request.ToId is not { } to) return BadRequest("Who is it for?");
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var from = cast.FirstOrDefault(c => c.Id == participantId);
        var them = cast.FirstOrDefault(c => c.Id == to);
        if (from is null || them is null) return NotFound();
        return await ActAsync(partyId, participantId, request,
            item => wardrobe.GiftAsync(partyId, participantId, from.Name, to, them.Name, item), "Can't afford that, or it can't be given.");
    }

    private async Task<ActionResult<WalletSummary>> ActAsync(Guid partyId, Guid participantId, WardrobeRequest request, Func<ShopItem, Task<ActOutcome?>> act, string refusal)
    {
        if (Catalog.Find(request.Key) is not { } item) return NotFound("No such item.");
        var who = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is null) return NotFound();
        if (await act(item) is not { } outcome) return BadRequest(refusal);
        await grains.GetGrain<IWorldClockGrain>(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = who.Id, PersonaName = who.Name, App = wardrobe.Id, Action = outcome.Action,
            Summary = outcome.Summary, TargetId = outcome.TargetId, WithPersonaId = outcome.WithPersonaId,
        });
        return Ok(Summary(who.Id, who.Name, await Wallet(partyId, participantId).GetAsync(), await LooksAsync(partyId)));
    }

    private async Task<Dictionary<Guid, List<string>>> LooksAsync(Guid partyId)
        => (await grains.GetGrain<World.Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Bodies
            .Where(b => b.Appearance is not null)
            .ToDictionary(b => b.ParticipantId, b => Catalog.Wearing(b.Appearance!));

    private IWalletGrain Wallet(Guid partyId, Guid who) => grains.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, who));

    private static WalletSummary Summary(Guid id, string name, WalletView w, Dictionary<Guid, List<string>> looks) => new(
        id, name, w.Balance, w.Means, w.DailyPay, w.PaidToday,
        w.Wardrobe.Select(Catalog.Find).OfType<ShopItem>().ToList(),
        w.Ledger,
        w.NewGifts.Select(g => new GiftSummary(g.FromId, g.FromName, Catalog.Find(g.Key)?.Name ?? g.Key, g.At)).ToList(),
        looks.GetValueOrDefault(id) ?? new List<string>());
}

public sealed record WardrobeRequest(string Key, Guid? ToId = null);

public sealed record GiftSummary(Guid FromId, string FromName, string Item, long At);

public sealed record WalletSummary(
    Guid ParticipantId,
    string Name,
    int Balance,
    string Means,
    int DailyPay,
    bool PaidToday,
    List<ShopItem> Wardrobe,
    List<LedgerEntry> Ledger,
    List<GiftSummary> NewGifts,
    /// <summary>Catalog keys of what they have on right now.</summary>
    List<string> Wearing);
