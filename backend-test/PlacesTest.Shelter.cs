using PartyTown.World;
using PartyTown.World.Places;

namespace BackendTest;

public partial class PlacesTest
{
    [Fact]
    public async Task ShelterSlot_GoesEmpty_Held_Freed_WithASignalEachChange()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        var lobby = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Lobby");

        var added = await places.AddShelterSlotAsync(lobby.Id, "walk-in", true);
        Assert.NotNull(added);
        var slot = added;
        Assert.Equal("empty", ShelterRules.StateOf(Assert.Single(await places.GetShelterSlotsAsync(lobby.Id))));

        await places.WorkAtAsync(pieter, lobby.Id);

        Assert.False(await places.TakeShelterSlotAsync(lobby.Id, slot.Id, denise, "Denise"));
        Assert.Equal("empty", ShelterRules.StateOf(Assert.Single(await places.GetShelterSlotsAsync(lobby.Id))));

        await places.MoveAsync(denise, lobby.Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await places.GetSnapshotAsync();

        Assert.True(await places.TakeShelterSlotAsync(lobby.Id, slot.Id, denise, "Denise"));
        var held = Assert.Single(await places.GetShelterSlotsAsync(lobby.Id));
        Assert.Equal(("held", denise, "Denise"), (ShelterRules.StateOf(held), held.HeldBy, held.HeldByName));
        var claimed = Assert.Single(await WaitForActivitiesAsync(clock, "shelter-claimed"), a => a.Action == "shelter-claimed");
        Assert.Equal(("places", denise, lobby.Id), (claimed.App, claimed.PersonaId, claimed.TargetId));
        await WaitForNotifiedAsync(clock, pieter);

        await places.MoveAsync(pieter, lobby.Id, "sofas");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await places.GetSnapshotAsync();
        Assert.False(await places.TakeShelterSlotAsync(lobby.Id, slot.Id, pieter, "Pieter"));
        Assert.False(await places.ReleaseShelterSlotAsync(lobby.Id, slot.Id, pieter, "Pieter"));

        Assert.True(await places.ReleaseShelterSlotAsync(lobby.Id, slot.Id, denise, "Denise"));
        var freed = Assert.Single(await places.GetShelterSlotsAsync(lobby.Id));
        Assert.Equal(("freed", denise, (Guid?)null), (ShelterRules.StateOf(freed), freed.FreedBy, freed.HeldBy));
        var freedLine = Assert.Single(await WaitForActivitiesAsync(clock, "shelter-freed"), a => a.Action == "shelter-freed");
        Assert.Contains("freed", freedLine.Summary);
        await WaitForNotifiedAsync(clock, pieter);

        Assert.True(await places.TakeShelterSlotAsync(lobby.Id, slot.Id, pieter, "Pieter"));
        Assert.True(await places.ReleaseShelterSlotAsync(lobby.Id, slot.Id, null, null));
        var closed = Assert.Single(await places.GetShelterSlotsAsync(lobby.Id));
        Assert.Equal(ShelterStates.Freed, ShelterRules.StateOf(closed));
        Assert.Null(closed.FreedBy);
        var closingLine = Assert.Single(await WaitForActivitiesAsync(clock, "shelter-freed", "closing"), a => a.Action == "shelter-freed" && a.Summary.Contains("closing"));
        Assert.Contains("closing time", closingLine.Summary);
    }

    private static async Task WaitForNotifiedAsync(IWorldClockGrain clock, Guid who)
    {
        for (var i = 0; i < 40; i++)
        {
            if ((await clock.GetNotifiedAsync()).ContainsKey(who)) return;
            await Task.Delay(50);
        }
    }

    private static async Task<List<WorldActivity>> WaitForActivitiesAsync(IWorldClockGrain clock, string action, string? summaryHas = null)
    {
        for (var i = 0; i < 40; i++)
        {
            var found = (await clock.GetActivitiesAsync(50, null))
                .Where(a => a.Action == action && (summaryHas is null || a.Summary.Contains(summaryHas)))
                .ToList();
            if (found.Count > 0) return found;
            await Task.Delay(50);
        }
        return (await clock.GetActivitiesAsync(50, null)).Where(a => a.Action == action).ToList();
    }

    [Fact]
    public void ShelterRules_SlotGates_AndReleaseIsHoldersOrClosing()
    {
        var slot = new ShelterSlot { Name = "joop", Open = true };
        Assert.Equal(ShelterStates.Empty, ShelterRules.StateOf(slot));
        Assert.True(ShelterRules.CanTake(slot));
        Assert.False(ShelterRules.CanRelease(slot, null));

        ShelterRules.Take(slot, Guid.NewGuid(), "Eiko", 100);
        Assert.Equal(ShelterStates.Held, ShelterRules.StateOf(slot));
        Assert.False(ShelterRules.CanTake(slot));
        Assert.False(ShelterRules.CanRelease(slot, Guid.NewGuid()));
        Assert.True(ShelterRules.CanRelease(slot, slot.HeldBy));
        Assert.True(ShelterRules.CanRelease(slot, null));

        ShelterRules.Release(slot, null, null, 200);
        Assert.Equal(ShelterStates.Freed, ShelterRules.StateOf(slot));
        Assert.True(ShelterRules.CanTake(slot));

        var shut = new ShelterSlot { Open = false };
        Assert.False(ShelterRules.CanTake(shut));
    }
}
