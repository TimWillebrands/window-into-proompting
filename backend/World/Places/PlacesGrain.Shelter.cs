using PartyTown.Model;
using PartyTown.World;

namespace PartyTown.World.Places;

public sealed partial class PlacesGrain
{
    public async Task<ShelterSlot?> AddShelterSlotAsync(Guid placeId, string name, bool open)
    {
        await SettleAsync();
        if (Find(placeId) is not { } place) return null;
        var slot = new ShelterSlot
        {
            Name = string.IsNullOrWhiteSpace(name) ? "shelter slot" : name.Trim(),
            Open = open,
        };
        place.ShelterSlots.Add(slot);
        await SaveAsync();
        await PublishPlaceAsync(place);
        return slot;
    }

    public Task<List<ShelterSlot>> GetShelterSlotsAsync(Guid placeId)
    {
        var place = Find(placeId);
        return Task.FromResult(place?.ShelterSlots.ToList() ?? new List<ShelterSlot>());
    }

    public async Task<bool> TakeShelterSlotAsync(Guid placeId, Guid slotId, Guid who, string whoName)
    {
        await SettleAsync();
        if (Find(placeId) is not { } place) return false;
        if (state.State.Bodies.GetValueOrDefault(who) is not { } body || body.PlaceId != placeId) return false;
        if (place.ShelterSlots.FirstOrDefault(s => s.Id == slotId) is not { } slot || !ShelterRules.CanTake(slot)) return false;
        var now = Now();
        ShelterRules.Take(slot, who, whoName, now);
        await SaveAsync();
        await PublishPlaceAsync(place);
        _ = ClockSignalsAsync(place, slot, "shelter-claimed",
            $"{whoName} takes the {slot.Name} slot at {place.Name}", who, whoName, now);
        return true;
    }

    public async Task<bool> ReleaseShelterSlotAsync(Guid placeId, Guid slotId, Guid? by, string? byName)
    {
        await SettleAsync();
        if (Find(placeId) is not { } place) return false;
        if (place.ShelterSlots.FirstOrDefault(s => s.Id == slotId) is not { } slot || !ShelterRules.CanRelease(slot, by)) return false;
        var now = Now();
        var who = slot.HeldBy;
        var whoName = slot.HeldByName;
        ShelterRules.Release(slot, by, byName, now);
        await SaveAsync();
        await PublishPlaceAsync(place);
        var byLine = by is null ? "closing time" : $"{byName ?? "someone"} frees";
        _ = ClockSignalsAsync(place, slot, "shelter-freed",
            $"the {slot.Name} slot at {place.Name} is freed ({byLine}; was held by {whoName ?? "someone"})", who, whoName, now);
        return true;
    }

    private async Task ClockSignalsAsync(Place place, ShelterSlot slot, string action, string summary, Guid? who, string? whoName, long now)
    {
        try
        {
            var clock = GrainFactory.GetGrain<IWorldClockGrain>(this.GetPrimaryKey());
            await clock.RecordAsync(new WorldActivity
            {
                Ts = now,
                PersonaId = who ?? Narrator.PersonaId,
                PersonaName = whoName ?? Narrator.DisplayName,
                App = "places",
                Action = action,
                Summary = summary,
                TargetId = place.Id,
            });
        }
        catch (Exception ex) { logger.LogDebug(ex, "Shelter record failed for {Slot}", slot.Id); }
        try
        {
            var desks = place.Staff.Concat(place.Hosts()).Distinct().ToList();
            if (desks.Count > 0)
            {
                var clock = GrainFactory.GetGrain<IWorldClockGrain>(this.GetPrimaryKey());
                await clock.NotifyAsync(desks);
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "Shelter notify failed for {Slot}", slot.Id); }
    }
}
