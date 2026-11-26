using PartyTown.World;
using PartyTown.World.Places;

namespace BackendTest;

public partial class PlacesTest
{
    [Fact]
    public async Task PowerLoss_RecordsPowerLostThenPowerRestored_AndSignalsStaffAndHosts()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var lobby = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Lobby");
        await places.WorkAtAsync(pieter, lobby.Id);

        Assert.True(await places.LosePowerAsync(lobby.Id));
        var lost = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == lobby.Id);
        Assert.True(lost.PowerLost);
        var lostLine = Assert.Single(await WaitForActivitiesAsync(clock, "power-lost"), a => a.Action == "power-lost");
        Assert.Contains("the Lobby", lostLine.Summary);
        await WaitForNotifiedSinceAsync(clock, pieter, 0);
        var afterLost = (await clock.GetNotifiedAsync())[pieter];
        Assert.False(await places.LosePowerAsync(lobby.Id));

        Assert.True(await places.RestorePowerAsync(lobby.Id));
        var back = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == lobby.Id);
        Assert.False(back.PowerLost);
        var restoredLine = Assert.Single(await WaitForActivitiesAsync(clock, "power-restored"), a => a.Action == "power-restored");
        Assert.Contains("the Lobby", restoredLine.Summary);
        await WaitForNotifiedSinceAsync(clock, pieter, afterLost);

        Assert.False(await places.RestorePowerAsync(lobby.Id));
        Assert.False(await places.LosePowerAsync(Guid.NewGuid()));
    }

    private static async Task WaitForNotifiedSinceAsync(IWorldClockGrain clock, Guid who, long since)
    {
        for (var i = 0; i < 40; i++)
        {
            if ((await clock.GetNotifiedAsync()).TryGetValue(who, out var at) && at > since) return;
            await Task.Delay(50);
        }
        Assert.Fail($"{who} was not notified after {since}");
    }
}
