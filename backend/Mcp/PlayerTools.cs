using System.Text;
using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
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
public sealed class PlayerTools(IGrainFactory grains, Actors actors, IOptions<McpOptions> mcp, PlayerAuto? auto = null)
{
    private Guid World => mcp.Value.World;
    private const string Whom = "Participant id; only admins may name someone else. Default: your own Player.";
    private const string Where = "Admins: a Place id from list_places, acted on as its author. Default: their Home.";

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

    [McpServerTool(Name = "list_places", ReadOnly = true), Description("Every Home and public Place: ids for get_place, what it's for, who lives or works there.")]
    public Task<List<PlaceRow>> ListPlacesAsync() => Guard(async () =>
    {
        var snapshot = await Places.GetSnapshotAsync();
        return snapshot.Places.Where(p => p.Kind is PlaceKinds.Home or PlaceKinds.Public)
            .Select(p => new PlaceRow(p.Id, p.Name, p.Kind, p.Purpose, p.Living().Concat(p.Staff).Distinct().Select(id => NameOf(snapshot, id)).ToList()))
            .ToList();
    });

    [McpServerTool(Name = "get_place", ReadOnly = true), Description("A Place by id: spots, things (with ids and tiles for arranging), colours and the tile map.")]
    public Task<PlaceView> GetPlaceAsync([Description("Place id from list_places.")] Guid placeId) => Guard(async () =>
    {
        var snapshot = await Places.GetSnapshotAsync();
        return PlaceView.Of(snapshot.Places.FirstOrDefault(p => p.Id == placeId) ?? throw new McpException("No such Place."), snapshot);
    });

    [McpServerTool(Name = "get_home", ReadOnly = true), Description("Their Home: name, spots, things (with ids and tiles for arranging), colours and the tile map.")]
    public Task<PlaceView> GetHomeAsync(ClaimsPrincipal user, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await ReadAsync(user, participantId);
        var snapshot = await Places.GetSnapshotAsync();
        var home = snapshot.Places.HomeOf(who) ?? throw new McpException("They have no Home.");
        return PlaceView.Of(home, snapshot);
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

    [McpServerTool(Name = "set_appearance"), Description("Set look fields by hand, keys and values from get_look_options. Unknown ones keep the current choice.")]
    public Task<Appearance> SetAppearanceAsync(ClaimsPrincipal user, [Description("Field key to picked value.")] Dictionary<string, string?> picked, [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await actors.ActAsync(user, World, participantId);
        return await Places.SetAppearanceAsync(who, picked) ?? throw new McpException("They have no Body yet.");
    });

    [McpServerTool(Name = "get_design_guide", ReadOnly = true), Description("How to design a place, a look or a thing yourself: the schema, the building blocks, the rules, and for a look the current one. Design, then pass the JSON to design_place, dress or add_thing.")]
    public Task<string> GetDesignGuideAsync(
        ClaimsPrincipal user,
        [Description("place, look or thing.")] string kind,
        [Description("Place only: outdoors, no walls (a camp, a square).")] bool outdoor = false,
        [Description(Whom)] Guid? participantId = null,
        [Description(Where)] Guid? placeId = null) => Guard(async () =>
    {
        var snapshot = await Places.GetSnapshotAsync();
        var who = await ReadAsync(user, participantId);
        switch (kind)
        {
            case "place":
                var place = placeId is { } id ? snapshot.Places.FirstOrDefault(p => p.Id == id) : snapshot.Places.HomeOf(who);
                var rules = place is { Kind: PlaceKinds.Home } ? PlaceDesign.HomeRules(Math.Max(1, place.Living().Count())) : "It's a public place.";
                return $"{PlaceDesign.Prompt(outdoor)}\n{rules}\nPass the whole JSON as design_place's design (or create_place's). check_place tries it without changing anything and shows the map with everything on it.";
            case "look":
                return LookGuide(snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == who)?.Appearance ?? AppearanceRules.Default(who));
            case "thing":
                return $"{ThingModel.Prompt}\nPass the JSON as add_thing's model.";
            default:
                throw new McpException("kind is place, look or thing.");
        }
    });

    [McpServerTool(Name = "design_place"), Description("Give their Home (or, for admins, a public Place) a design you made, see get_design_guide place. Same people, keys and things; it's checked, and what's wrong comes back to fix.")]
    public Task<PlaceView> DesignPlaceAsync(
        ClaimsPrincipal user,
        [Description("The design JSON: name, description, spots, scene (and things, for a Home).")] JsonElement design,
        [Description("A new name. Default: keep it.")] string? name = null,
        [Description("Outdoors: no walls, open sky.")] bool outdoor = false,
        [Description(Whom)] Guid? participantId = null,
        [Description(Where)] Guid? placeId = null) => Guard(async () =>
    {
        var (_, place) = await ActInAsync(user, participantId, placeId);
        if (place.Kind is not (PlaceKinds.Home or PlaceKinds.Public)) throw new McpException("Only Homes and public Places have designs.");
        Func<IReadOnlyList<Spot>, string?>? rule = place.Kind == PlaceKinds.Home ? PlaceDesign.MissingHomeSpots : null;
        var check = PlaceDesign.Inspect(JsonNode.Parse(design.GetRawText()) as JsonObject, outdoor, rule);
        var draft = check.Draft;
        if (draft is null) throw new McpException(Wrong(check));
        _ = await Places.RedesignPlaceAsync(place.Id, draft, name) ?? throw new McpException("Couldn't apply it.");
        var snapshot = await Places.GetSnapshotAsync();
        return PlaceView.Of(snapshot.Places.Single(p => p.Id == place.Id), snapshot);
    });

    [McpServerTool(Name = "create_place"), Description("Admins: open a new public Place in the centre from a design you made (see get_design_guide place). It's checked, and what's wrong comes back to fix.")]
    public Task<PlaceView> CreatePlaceAsync(
        ClaimsPrincipal user,
        [Description("The design JSON: name, at, description, purpose, spots, scene.")] JsonElement design,
        [Description("Outdoors: no walls, open sky.")] bool outdoor = false) => Guard(async () =>
    {
        RequireAdmin(user);
        var check = PlaceDesign.Inspect(JsonNode.Parse(design.GetRawText()) as JsonObject, outdoor, null);
        if (check.Draft is not { } draft) throw new McpException(Wrong(check));
        var made = await Places.CreatePlaceAsync(null, draft.Doc["description"]?.ToString() ?? "a new place", draft)
            ?? throw new McpException($"Couldn't open it; there may be {PlacesGrain.MaxMadePlaces} made Places already.");
        return PlaceView.Of(made, await Places.GetSnapshotAsync());
    });

    [McpServerTool(Name = "check_place", ReadOnly = true), Description("Try a place design without changing anything: what's wrong, what was mended, the things to do it would have, and the map with props lettered and spots numbered.")]
    public Task<string> CheckPlaceAsync(
        [Description("The design JSON, as for design_place or create_place.")] JsonElement design,
        [Description("Outdoors: no walls, open sky.")] bool outdoor = false,
        [Description("It's for a Home: needs the sofa, desk, kitchen and bed spots.")] bool home = false) => Guard(() =>
    {
        var check = PlaceDesign.Inspect(JsonNode.Parse(design.GetRawText()) as JsonObject, outdoor, home ? PlaceDesign.MissingHomeSpots : null);
        if (check.Draft is not { } draft) return Task.FromResult(Wrong(check));
        var sb = new StringBuilder("It checks out.\n");
        if (check.Notes.Count > 0) sb.AppendLine($"Mended:\n- {string.Join("\n- ", check.Notes)}");
        sb.AppendLine(draft.Features.Count == 0
            ? "Nothing to do here yet: place something marked ★ or give a prop a \"do\" and \"gives\"."
            : $"To do here:\n- {string.Join("\n- ", draft.Features.Select(f => $"{f.Do} ({f.Name}, {f.SpotId}){(f.Leaves ? ", stays for the next visitor" : string.Empty)}: {f.Gives}"))}");
        return Task.FromResult(sb.Append(check.Preview).ToString());
    });

    [McpServerTool(Name = "do_thing"), Description("Do what a thing where they are offers (thing ids and what they offer from get_place). Gives back what they came away with; for things that keep what's left, text is their own words to leave.")]
    public Task<string> DoThingAsync(
        ClaimsPrincipal user,
        [Description("Thing id from get_place.")] string furniId,
        [Description("Their own words to leave (a wish, a line); default: the world writes what they find.")] string? text = null,
        [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        var who = await actors.ActAsync(user, World, participantId);
        var body = (await Places.GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == who) ?? throw new McpException("They have no Body yet.");
        var done = await Doing.DoAsync(grains, World, who, null, body.PlaceId, furniId, null, text, DateTimeOffset.UtcNow)
            ?? throw new McpException("Nothing to do with that here (not in this Place, still walking, or no such thing).");
        return done.Summary;
    });

    [McpServerTool(Name = "dress"), Description("Put a look you designed on them (see get_design_guide look): palette picks, a summary, and the design slots that change.")]
    public Task<RestyleResult> DressAsync(
        ClaimsPrincipal user,
        [Description("{\"picks\": {...}, \"summary\": \"...\", \"design\": {...}}")] JsonElement look,
        [Description(Whom)] Guid? participantId = null) => Guard(async () =>
    {
        if (look.ValueKind != JsonValueKind.Object) throw new McpException("The look is a JSON object: picks, summary, design.");
        if (look.TryGetProperty("design", out var design) && design.ValueKind == JsonValueKind.Object
            && FigureDesign.Clean(JsonNode.Parse(design.GetRawText())) is null)
            throw new McpException("Nothing in design survived cleaning; check it against the schema in get_design_guide look.");
        var who = await actors.ActAsync(user, World, participantId);
        return await Places.DressAsync(who, look.GetRawText()) ?? throw new McpException("They have no Body yet.");
    });

    [McpServerTool(Name = "add_thing"), Description("Put a thing you modelled in their Home (see get_design_guide thing); place it with arrange_thing.")]
    public Task<FurniView> AddThingAsync(
        ClaimsPrincipal user,
        [Description("What it is, e.g. 'a brass orrery'; under 200 characters.")] string description,
        [Description("The model JSON: {\"wall\": false, \"parts\": [...]}.")] JsonElement model,
        [Description(Whom)] Guid? participantId = null,
        [Description(Where)] Guid? placeId = null,
        [Description("Something to do with it, starting with a verb: 'Wind up the music box'. Default: it's just there.")] string? doWhat = null,
        [Description("With doWhat: what someone comes away with, a concrete find: 'the tune it plays and where it sticks'.")] string? gives = null,
        [Description("With doWhat: what they do stays for the next visitor to find.")] bool leaves = false) => Guard(async () =>
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > 200) throw new McpException("Describe it in under 200 characters.");
        var made = ThingModel.Clean(JsonNode.Parse(model.GetRawText())) ?? throw new McpException("No parts survived cleaning; check the model against get_design_guide thing.");
        var (who, place) = await ActInAsync(user, participantId, placeId);
        var offer = string.IsNullOrWhiteSpace(doWhat) ? null : Library.OfferOf(new JsonObject { ["do"] = doWhat, ["gives"] = gives, ["leaves"] = leaves });
        return await Places.AddMadeThingAsync(place.Id, who == Guid.Empty ? null : who, description, made, offer) ?? throw new McpException("Couldn't add that.");
    });

    [McpServerTool(Name = "arrange_thing"), Description("Move or turn a thing in their Home (ids and tiles from get_home). No tile hands it back to the scene.")]
    public Task<string> ArrangeThingAsync(
        ClaimsPrincipal user,
        [Description("Thing id from get_home.")] string furniId,
        [Description("Tile column.")] int? x = null,
        [Description("Tile row.")] int? z = null,
        [Description("Quarter turns, 0-3.")] int rot = 0,
        [Description(Whom)] Guid? participantId = null,
        [Description(Where)] Guid? placeId = null) => Guard(async () =>
    {
        var (who, home) = await ActInAsync(user, participantId, placeId);
        return await Places.ArrangeAsync(who, home.Id, furniId, x, z, rot) ? "Moved." : throw new McpException("Not theirs to move, or no such thing.");
    });

    [McpServerTool(Name = "remove_thing", Destructive = true), Description("Take a thing out of their Home.")]
    public Task<string> RemoveThingAsync(ClaimsPrincipal user, [Description("Thing id from get_home.")] string furniId, [Description(Whom)] Guid? participantId = null, [Description(Where)] Guid? placeId = null) => Guard(async () =>
    {
        var (who, home) = await ActInAsync(user, participantId, placeId);
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

    [McpServerTool(Name = "set_auto"), Description("Hand your own character to the agent stack (on), or take them back (off) so say, go and DM act for you again. A mission is what they go after while on auto.")]
    public Task<string> SetAutoAsync(ClaimsPrincipal user, bool on, [Description("What they go after while on auto; only with on.")] string? mission = null) => Guard(async () =>
    {
        var me = await actors.PlayerAsync(user, World);
        await (auto ?? throw new McpException("Auto isn't available here.")).SetAsync(World, me, on, on ? mission : null);
        return on ? "On auto: they live on their own now." : "Yours again.";
    });

    [McpServerTool(Name = "set_home_colours"), Description("Wall and floor colours of their Home, as #rrggbb; leave one out to give it back to the design.")]
    public Task<string> SetHomeColoursAsync(ClaimsPrincipal user, string? wall = null, string? floor = null, [Description(Whom)] Guid? participantId = null, [Description(Where)] Guid? placeId = null) => Guard(async () =>
    {
        var (who, home) = await ActInAsync(user, participantId, placeId);
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

    private static string LookGuide(Appearance now) => $"""
        {FigureDesign.RestylePrompt}

        Their look now: {AppearanceRules.Describe(now)}
        Drawn as: {now.Design ?? "(palette only)"}

        Prints: put the drawing inline as "svg" on the print instead of a brief; a print without svg is left off.
        {FigureDesign.PrintPrompt}
        Pass the JSON as dress's look.
        """;

    /// <summary>Their Home, or for an admin naming a Place, that Place with the author (<see cref="Guid.Empty"/>) acting.</summary>
    private async Task<(Guid Who, Place Place)> ActInAsync(ClaimsPrincipal user, Guid? participantId, Guid? placeId)
    {
        if (placeId is null) return await ActInHomeAsync(user, participantId);
        RequireAdmin(user);
        var place = (await Places.GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == placeId) ?? throw new McpException("No such Place.");
        return (Guid.Empty, place);
    }

    private static string Wrong(DesignCheck check)
        => $"The design doesn't work yet:\n- {string.Join("\n- ", check.Errors.Take(12))}"
            + (check.Notes.Count > 0 ? $"\nMended already:\n- {string.Join("\n- ", check.Notes.Take(8))}" : string.Empty)
            + (check.Preview.Length > 0 ? $"\nAs it stands (props lettered, spots numbered):\n{check.Preview}" : string.Empty);

    private static void RequireAdmin(ClaimsPrincipal user)
    {
        if (!Actors.IsAdmin(user)) throw new McpException("Only admins may act on Places other than a Home.");
    }

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

public sealed record HomeThing(string Id, string Kind, string Name, string SpotId, int? TileX, int? TileZ, int Rot, string? Do = null, List<string>? Left = null);

public sealed record PlaceRow(Guid Id, string Name, string Kind, string? Purpose, List<string> People);

public sealed record PlaceView(Guid Id, string Name, string Kind, string Description, string? Purpose, List<string> LivingHere, List<string> Staff, List<Spot> Spots, List<HomeThing> Things, string? WallColour, string? FloorColour, List<string> Map)
{
    public static PlaceView Of(Place home, PlacesSnapshot snapshot) => new(
        home.Id, home.Name, home.Kind, home.Description, home.Purpose,
        home.Living().Select(id => snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == id)?.Name ?? "?").ToList(),
        home.Staff.Select(id => snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == id)?.Name ?? "?").ToList(),
        home.Spots,
        snapshot.Furni.Where(f => f.PlaceId == home.Id).Select(f => new HomeThing(f.FurniId, f.Kind, f.Name, f.SpotId, f.TileX, f.TileZ, f.Rot,
            f.Gives is not null ? f.UseLabel : null,
            f.Leaves && f.Takes.Count > 0 ? f.Takes.TakeLast(4).Select(t => $"{t.ByName}: {t.Text}").ToList() : null)).ToList(),
        home.WallColour, home.FloorColour,
        PlaceDesign.MapRows(home.CustomScene));
}
