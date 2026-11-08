using System.Security.Claims;
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
    public async Task AnAgent_RedesignsItsPlayersHome()
    {
        var (_, _, _, tools) = await PlayerWorldAsync();
        ScriptedLlm.Json = system => !system.Contains("You design one new place") ? null : JsonNode.Parse("""
            {"name": "the loft", "description": "a loft with a long workbench",
             "spots": [{"id": "sofa", "name": "the sofa", "at": "on", "capacity": 1, "seat": true},
                       {"id": "desk", "name": "the workbench", "at": "at", "capacity": 1},
                       {"id": "kitchen", "name": "the kitchenette", "at": "in", "capacity": 1},
                       {"id": "bed", "name": "the bed", "at": "on", "capacity": 1, "seat": true}],
             "scene": {"map": ["#ww#####", "#......#", "#......#", "D......#", "########"],
                       "spots": {"sofa": {"at": [1, 1]}, "desk": {"at": [5, 1]}, "kitchen": {"at": [5, 3]}, "bed": {"at": [3, 2]}}}}
            """)!.AsObject();
        try
        {
            var home = await tools.RedesignHomeAsync(Agent(), "a loft", "Sparky's loft");

            Assert.Equal(("Sparky's loft", "a loft with a long workbench"), (home.Name, home.Description));
            Assert.Equal("D......#", home.Map[3]);
            Assert.Contains(home.Spots, s => s.Name == "the workbench");
        }
        finally
        {
            ScriptedLlm.Json = null;
        }
    }
}
