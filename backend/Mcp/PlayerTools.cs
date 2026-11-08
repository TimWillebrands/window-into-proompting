using System.ComponentModel;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Economy;
using PartyTown.World.Places;

namespace PartyTown.Mcp;

/// <summary>What an agent may read and change of a Player (ADR 0046): their own, or anyone's for an admin.</summary>
[McpServerToolType]
public sealed class PlayerTools(IGrainFactory grains, Actors actors, IOptions<McpOptions> mcp)
{
    private Guid World => mcp.Value.World;
    private const string Whom = "Participant id; only admins may name someone else. Default: your own Player.";

    private IPlacesGrain Places => grains.GetGrain<IPlacesGrain>(World);

    [McpServerTool(Name = "whoami", ReadOnly = true), Description("Your Player: id, name, Home, and whether you're an admin.")]
    public Task<WhoAmI> WhoAmIAsync(ClaimsPrincipal user) => Guard(async () =>
    {
        var me = await actors.PlayerAsync(user, World);
        var snapshot = await Places.GetSnapshotAsync();
        var home = snapshot.Places.HomeOf(me);
        return new WhoAmI(me, NameOf(snapshot, me), Actors.IsAdmin(user), home?.Id, home?.Name);
    });

    [McpServerTool(Name = "list_participants", ReadOnly = true), Description("Everyone in the World: Personas and Players, with ids for participantId.")]
    public Task<List<ParticipantRow>> ListParticipantsAsync(ClaimsPrincipal user) => Guard(async () =>
    {
        var humans = await actors.HumansAsync(World);
        var cast = await grains.GetGrain<IPartyGrain>(World).GetCastAsync();
        return cast.Where(c => c.Id != Narrator.PersonaId).Select(c => new ParticipantRow(c.Id, c.Name, humans.Contains(c.Id))).ToList();
    });

    [McpServerTool(Name = "get_situation", ReadOnly = true), Description("Where they are and what's around them, in words.")]
    public Task<string> GetSituationAsync(ClaimsPrincipal user, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
        await Places.GetSituationAsync(await ReadAsync(user, participantId)));

    [McpServerTool(Name = "get_home", ReadOnly = true), Description("Their Home: name, spots, things (with ids and tiles for arranging), colours and the tile map.")]
    public Task<HomeView> GetHomeAsync(ClaimsPrincipal user, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await ReadAsync(user, participantId);
        var snapshot = await Places.GetSnapshotAsync();
        var home = snapshot.Places.HomeOf(who) ?? throw new McpException("They have no Home.");
        return HomeView.Of(home, snapshot);
    });

    [McpServerTool(Name = "get_look", ReadOnly = true), Description("Their look: the appearance fields as picked now.")]
    public Task<Appearance?> GetLookAsync(ClaimsPrincipal user, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await ReadAsync(user, participantId);
        return (await Places.GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == who)?.Appearance;
    });

    [McpServerTool(Name = "get_look_options", ReadOnly = true), Description("The palette set_appearance picks from, field by field.")]
    public AppearanceField[] GetLookOptions() => AppearanceRules.Fields;

    [McpServerTool(Name = "get_wallet", ReadOnly = true), Description("Their money: balance, daily pay, what they own, recent ledger.")]
    public Task<WalletRead> GetWalletAsync(ClaimsPrincipal user, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await ReadAsync(user, participantId);
        var w = await grains.GetGrain<IWalletGrain>(WorldKeys.Life(World, who)).GetAsync();
        return new WalletRead(w.Balance, w.Means, w.DailyPay, w.PaidToday,
            w.Wardrobe.Select(Catalog.Find).OfType<ShopItem>().Select(i => i.Name).ToList(), w.Ledger.TakeLast(10).ToList());
    });

    [McpServerTool(Name = "restyle"), Description("Change their look from words, e.g. 'dye my hair pink, add a raven staff'. Takes a few seconds.")]
    public Task<RestyleResult> RestyleAsync(ClaimsPrincipal user, [Description("What to change, under 300 characters.")] string text, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 300) throw new McpException("Say what to change, under 300 characters.");
        var who = await actors.ActAsync(user, World, participantId);
        return await Places.RestyleAsync(who, text) ?? throw new McpException("Couldn't restyle right now.");
    });

    [McpServerTool(Name = "set_appearance"), Description("Set look fields by hand, keys and values from get_look_options. Unknown ones keep the current choice.")]
    public Task<Appearance> SetAppearanceAsync(ClaimsPrincipal user, [Description("Field key to picked value.")] Dictionary<string, string?> picked, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await actors.ActAsync(user, World, participantId);
        return await Places.SetAppearanceAsync(who, picked) ?? throw new McpException("They have no Body yet.");
    });

    [McpServerTool(Name = "redesign_home"), Description("Design their Home anew from a description: layout, furniture, colours. Same people, keys and things. Takes up to a minute.")]
    public Task<HomeView> RedesignHomeAsync(
        ClaimsPrincipal user,
        [Description("What it should be like, under 1500 characters.")] string description,
        [Description("A new name, e.g. 'the herenhuis on the Herengracht'. Default: keep it.")] string? name = null,
        [Description("An outdoor camp: no walls, open sky.")] bool outdoor = false,
        [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 1500) throw new McpException("Describe it in under 1500 characters.");
        var home = await OwnHomeAsync(user, participantId);
        _ = await PlaceMaking.RedesignHomeAsync(grains, World, home.Id, description, name, outdoor) ?? throw new McpException("Couldn't design that one; try again or describe it differently.");
        var snapshot = await Places.GetSnapshotAsync();
        return HomeView.Of(snapshot.Places.Single(p => p.Id == home.Id), snapshot);
    });

    [McpServerTool(Name = "add_thing"), Description("Put a new thing in their Home, e.g. 'a brass orrery'. Its 3D model is made shortly after.")]
    public Task<FurniView> AddThingAsync(ClaimsPrincipal user, [Description("The thing, under 200 characters.")] string description, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 200) throw new McpException("Describe it in under 200 characters.");
        var who = await actors.ActAsync(user, World, participantId);
        var home = (await Places.GetSnapshotAsync()).Places.HomeOf(who) ?? throw new McpException("They have no Home.");
        return await Places.AddThingAsync(home.Id, who, description) ?? throw new McpException("Couldn't add that.");
    });

    [McpServerTool(Name = "arrange_thing"), Description("Move or turn a thing in their Home (ids and tiles from get_home). No tile hands it back to the scene.")]
    public Task<string> ArrangeThingAsync(
        ClaimsPrincipal user,
        [Description("Thing id from get_home.")] string furniId,
        [Description("Tile column.")] int? x = null,
        [Description("Tile row.")] int? z = null,
        [Description("Quarter turns, 0-3.")] int rot = 0,
        [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var (who, home) = await ActInHomeAsync(user, participantId);
        return await Places.ArrangeAsync(who, home.Id, furniId, x, z, rot) ? "Moved." : throw new McpException("Not theirs to move, or no such thing.");
    });

    [McpServerTool(Name = "remove_thing", Destructive = true), Description("Take a thing out of their Home.")]
    public Task<string> RemoveThingAsync(ClaimsPrincipal user, [Description("Thing id from get_home.")] string furniId, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var (who, home) = await ActInHomeAsync(user, participantId);
        return await Places.RemoveThingAsync(who, home.Id, furniId) ? "Removed." : throw new McpException("Not theirs to take down, or no such thing.");
    });

    [McpServerTool(Name = "move_home"), Description("Move their Home: a new name (the address) and/or part of town, e.g. 'Zuid'. Layout and things stay.")]
    public Task<string> MoveHomeAsync(
        ClaimsPrincipal user,
        [Description("A new name, e.g. 'the house on the Johannes Vermeerstraat'. Default: keep it.")] string? name = null,
        [Description("Part of town, e.g. 'Zuid' or 'the centre'; made if new. Default: stay.")] string? district = null,
        [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(district)) throw new McpException("Give a name, a district or both.");
        var (_, home) = await ActInHomeAsync(user, participantId);
        var moved = await Places.MoveHomeAsync(home.Id, name, district) ?? throw new McpException("Couldn't move it.");
        var where = (await Places.GetSnapshotAsync()).Districts.FirstOrDefault(d => d.Id == moved.DistrictId)?.Name ?? "the centre";
        return $"Now {moved.Name}, in {where}.";
    });

    [McpServerTool(Name = "set_home_colours"), Description("Wall and floor colours of their Home, as #rrggbb; leave one out to give it back to the design.")]
    public Task<string> SetHomeColoursAsync(ClaimsPrincipal user, string? wall = null, string? floor = null, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var (who, home) = await ActInHomeAsync(user, participantId);
        return await Places.SetPlaceStyleAsync(who, home.Id, wall, floor) ? "Painted." : throw new McpException("Not theirs to restyle.");
    });

    /// <summary>Reading is public, as on the site: anyone may look at anyone.</summary>
    private async Task<Guid> ReadAsync(ClaimsPrincipal user, Guid? participantId)
        => participantId ?? await actors.PlayerAsync(user, World);

    private async Task<(Guid Who, Place Home)> ActInHomeAsync(ClaimsPrincipal user, Guid? participantId)
    {
        var who = await actors.ActAsync(user, World, participantId);
        return (who, (await Places.GetSnapshotAsync()).Places.HomeOf(who) ?? throw new McpException("They have no Home."));
    }

    private async Task<Place> OwnHomeAsync(ClaimsPrincipal user, Guid? participantId) => (await ActInHomeAsync(user, participantId)).Home;

    private static string NameOf(PlacesSnapshot snapshot, Guid who) => snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == who)?.Name ?? "?";

    /// <summary>Identity refusals as tool errors the agent can read, not opaque failures.</summary>
    private static async Task<T> Guard<T>(Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception ex) when (ex is ForbiddenActorException or NoPlayerException or OnAutoException)
        {
            throw new McpException(ex.Message);
        }
    }
}

public sealed record WhoAmI(Guid PlayerId, string Name, bool IsAdmin, Guid? HomeId, string? HomeName);

public sealed record ParticipantRow(Guid Id, string Name, bool Human);

public sealed record WalletRead(int Balance, string Means, int DailyPay, bool PaidToday, List<string> Owns, List<LedgerEntry> RecentLedger);

public sealed record HomeThing(string Id, string Kind, string Name, string SpotId, int? TileX, int? TileZ, int Rot);

public sealed record HomeView(Guid Id, string Name, string Description, List<string> LivingHere, List<Spot> Spots, List<HomeThing> Things, string? WallColour, string? FloorColour, List<string> Map)
{
    public static HomeView Of(Place home, PlacesSnapshot snapshot) => new(
        home.Id, home.Name, home.Description,
        home.Living().Select(id => snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == id)?.Name ?? "?").ToList(),
        home.Spots,
        snapshot.Furni.Where(f => f.PlaceId == home.Id).Select(f => new HomeThing(f.FurniId, f.Kind, f.Name, f.SpotId, f.TileX, f.TileZ, f.Rot)).ToList(),
        home.WallColour, home.FloorColour,
        PlaceDesign.MapRows(home.CustomScene));
}
