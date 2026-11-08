using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using PartyTown.Mcp;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>The Player tools an agent gets (ADR 0046): their own Player, anyone's for an admin.</summary>
public partial class WorldLifeTest
{
    private async Task<(Guid PartyId, Guid Player, Guid Denise, PlayerTools Tools)> PlayerWorldAsync()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var player = (await _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId).ClaimPlayerAsync("user_a", "Sparky"))!.Value;
        await PlacesOf(partyId).GetSnapshotAsync();
        var tools = new PlayerTools(_fx.GrainFactory, new Actors(_fx.GrainFactory), Options.Create(new McpOptions { World = partyId }));
        return (partyId, player, denise, tools);
    }

    private IPlacesGrain PlacesOf(Guid partyId) => _fx.GrainFactory.GetGrain<IPlacesGrain>(partyId);

    private static ClaimsPrincipal Agent(string clerkId = "user_a", bool admin = false)
    {
        var claims = new List<Claim> { new(ClerkAuth.UserIdClaim, clerkId) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, ClerkAuth.AdminRole));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    [Fact]
    public async Task AnAgent_SeesItsPlayerAndTheirHome()
    {
        var (_, player, _, tools) = await PlayerWorldAsync();

        var me = await tools.WhoAmIAsync(Agent());
        var home = await tools.GetHomeAsync(Agent());

        Assert.Equal((player, "Sparky", false), (me.PlayerId, me.Name, me.IsAdmin));
        Assert.Equal(me.HomeId, home.Id);
        Assert.Contains("Sparky", home.LivingHere);
        Assert.Contains(home.Things, t => t.Kind == FurniKinds.Bed);
        Assert.Contains((await tools.ListParticipantsAsync(Agent())), p => p.Id == player && p.Human);
    }

    [Fact]
    public async Task AnAgent_ChangesItsOwnPlayer_ButNoOneElses()
    {
        var (partyId, player, denise, tools) = await PlayerWorldAsync();

        Assert.Equal("Painted.", await tools.SetHomeColoursAsync(Agent(), wall: "#112233"));
        var refused = await Assert.ThrowsAsync<McpException>(() => tools.SetHomeColoursAsync(Agent(), wall: "#112233", participantId: denise));
        Assert.Contains("not your Player", refused.Message);
        await Assert.ThrowsAsync<McpException>(() => tools.WhoAmIAsync(Agent("user_nobody")));

        var snapshot = await PlacesOf(partyId).GetSnapshotAsync();
        Assert.Equal("#112233", snapshot.Places.HomeOf(player)!.WallColour);
        Assert.Null(snapshot.Places.HomeOf(denise)!.WallColour);
    }

    [Fact]
    public async Task AnAdminsAgent_ChangesAnyone()
    {
        var (partyId, _, denise, tools) = await PlayerWorldAsync();
        var plant = (await tools.GetHomeAsync(Agent(), denise)).Things.Single(t => t.Kind == FurniKinds.Plant);

        Assert.Equal("Painted.", await tools.SetHomeColoursAsync(Agent(admin: true), floor: "#445566", participantId: denise));
        Assert.Equal("Moved.", await tools.ArrangeThingAsync(Agent(admin: true), plant.Id, 2, 2, 1, denise));

        var home = await tools.GetHomeAsync(Agent(), denise);
        Assert.Equal("#445566", home.FloorColour);
        Assert.Equal((2, 2, 1), (home.Things.Single(t => t.Id == plant.Id).TileX, home.Things.Single(t => t.Id == plant.Id).TileZ, home.Things.Single(t => t.Id == plant.Id).Rot));
    }

    [Fact]
    public async Task OnlyAnAdminsAgent_FurnishesAPublicPlace()
    {
        var (_, _, _, tools) = await PlayerWorldAsync();
        var works = (await tools.ListPlacesAsync()).First(p => p.Kind == PlaceKinds.Public);

        await Assert.ThrowsAsync<McpException>(() => tools.AddThingAsync(Agent(), "a brass orrery", Model, placeId: works.Id));
        await Assert.ThrowsAsync<McpException>(() => tools.DesignPlaceAsync(Agent(), Loft(), placeId: works.Id));

        var orrery = await tools.AddThingAsync(Agent(admin: true), "a brass orrery", Model, placeId: works.Id);
        Assert.Equal("Moved.", await tools.ArrangeThingAsync(Agent(admin: true), orrery.FurniId, 2, 2, 1, placeId: works.Id));
        Assert.Equal("Painted.", await tools.SetHomeColoursAsync(Agent(admin: true), wall: "#223344", placeId: works.Id));
        var place = await tools.GetPlaceAsync(works.Id);
        Assert.Equal("#223344", place.WallColour);
        Assert.Contains(place.Things, t => t.Id == orrery.FurniId && t.TileX == 2);
        Assert.Equal("Removed.", await tools.RemoveThingAsync(Agent(admin: true), orrery.FurniId, placeId: works.Id));
    }

    [Fact]
    public async Task OnlyAnAdminsAgent_OpensANewPlace_FromItsOwnDesign()
    {
        var (_, _, _, tools) = await PlayerWorldAsync();

        await Assert.ThrowsAsync<McpException>(() => tools.CreatePlaceAsync(Agent(), Loft()));
        var wrong = await Assert.ThrowsAsync<McpException>(() => tools.CreatePlaceAsync(Agent(admin: true), Loft(door: "#")));
        Assert.Contains("no way in", wrong.Message);

        var made = await tools.CreatePlaceAsync(Agent(admin: true), Loft());
        Assert.Equal((PlaceKinds.Public, "a loft with a long workbench"), (made.Kind, made.Description));
        Assert.Contains(await tools.ListPlacesAsync(), p => p.Id == made.Id);
    }

    [Fact]
    public async Task ARedesignedCafe_KeepsItsStaffSpot()
    {
        var (_, _, _, tools) = await PlayerWorldAsync();
        var cafe = (await tools.ListPlacesAsync()).Single(p => p.Name == "the Café");
        var design = JsonDocument.Parse("""
            {"name": "the Café", "description": "a café with a long counter",
             "spots": [{"id": "counter", "name": "the counter", "at": "at", "capacity": 3},
                       {"id": "nook", "name": "the nook", "at": "in", "capacity": 2, "seat": true}],
             "scene": {"map": ["#ww#####", "#......#", "#......#", "D......#", "########"],
                       "spots": {"counter": {"at": [2, 1]}, "nook": {"at": [5, 2]}}}}
            """).RootElement;

        var place = await tools.DesignPlaceAsync(Agent(admin: true), design, placeId: cafe.Id);

        Assert.True(place.Spots.Single(s => s.Id == "counter").Staff);
        Assert.False(place.Spots.Single(s => s.Id == "nook").Staff);
    }

    private static readonly JsonElement Model = JsonDocument.Parse("""
        {"wall": false, "parts": [{"cyl": [0.2, 0.3], "at": [0, 0, 0], "c": "#b08d57"}, {"ball": 0.12, "at": [0, 0.45, 0], "c": "#e8c547"}]}
        """).RootElement;

    private static JsonElement Loft(string door = "D") => JsonDocument.Parse("""
        {"name": "the loft", "description": "a loft with a long workbench",
         "spots": [{"id": "sofa", "name": "the sofa", "at": "on", "capacity": 1, "seat": true},
                   {"id": "desk", "name": "the workbench", "at": "at", "capacity": 1},
                   {"id": "kitchen", "name": "the kitchenette", "at": "in", "capacity": 1},
                   {"id": "bed", "name": "the bed", "at": "on", "capacity": 1, "seat": true}],
         "scene": {"map": ["#ww#####", "#......#", "#......#", "DOOR......#", "########"],
                   "spots": {"sofa": {"at": [1, 1]}, "desk": {"at": [5, 1]}, "kitchen": {"at": [5, 3]}, "bed": {"at": [3, 2]}}}}
        """.Replace("DOOR", door)).RootElement;

    [Fact]
    public async Task AnAgent_DesignsItsPlayersHome_AndHearsWhatsWrong()
    {
        var (_, _, _, tools) = await PlayerWorldAsync();
        var guide = await tools.GetDesignGuideAsync(Agent(), "place");
        Assert.Contains("Spots must include these ids", guide);

        var wrong = await Assert.ThrowsAsync<McpException>(() => tools.DesignPlaceAsync(Agent(), Loft(door: "#")));
        Assert.Contains("no way in", wrong.Message);

        var home = await tools.DesignPlaceAsync(Agent(), Loft(), "Sparky's loft");
        Assert.Equal(("Sparky's loft", "a loft with a long workbench"), (home.Name, home.Description));
        Assert.Equal("D......#", home.Map[3]);
        Assert.Contains(home.Spots, s => s.Name == "the workbench");
    }

    [Fact]
    public async Task AnAgent_DressesItsPlayer_AndFurnishesWithItsOwnModels()
    {
        var (partyId, player, _, tools) = await PlayerWorldAsync();
        Assert.Contains("Their look now", await tools.GetDesignGuideAsync(Agent(), "look"));

        var worn = await tools.DressAsync(Agent(), JsonDocument.Parse("""{"picks": {"hair": "pink"}, "summary": "pink hair, all business"}""").RootElement);
        var orrery = await tools.AddThingAsync(Agent(), "a brass orrery", Model);
        await Assert.ThrowsAsync<McpException>(() => tools.AddThingAsync(Agent(), "nothing", JsonDocument.Parse("""{"parts": []}""").RootElement));

        Assert.Equal("pink", worn.Look.Hair);
        var snapshot = await PlacesOf(partyId).GetSnapshotAsync();
        Assert.Equal("pink", snapshot.Bodies.Single(b => b.ParticipantId == player).Appearance!.Hair);
        Assert.NotNull(snapshot.Furni.Single(f => f.FurniId == orrery.FurniId).Model);
    }
}
