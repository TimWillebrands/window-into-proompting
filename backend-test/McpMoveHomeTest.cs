using ModelContextProtocol;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>move_home: a Home gets a new address and part of town, keeping its layout.</summary>
public partial class WorldLifeTest
{
    [Fact]
    public async Task AnAgent_MovesItsPlayersHome_KeepingTheLayout()
    {
        var (partyId, player, _, tools) = await PlayerWorldAsync();
        var before = await tools.GetHomeAsync(Agent());

        var said = await tools.MoveHomeAsync(Agent(), "the house on the Johannes Vermeerstraat", "Zuid");

        var snapshot = await PlacesOf(partyId).GetSnapshotAsync();
        var home = snapshot.Places.HomeOf(player)!;
        Assert.Equal("the house on the Johannes Vermeerstraat", home.Name);
        Assert.Equal("Zuid", snapshot.Districts.Single(d => d.Id == home.DistrictId).Name, ignoreCase: true);
        Assert.Equal(before.Map, (await tools.GetHomeAsync(Agent())).Map);
        Assert.Contains("Johannes Vermeerstraat", said);
        await Assert.ThrowsAsync<McpException>(() => tools.MoveHomeAsync(Agent()));
    }
}
