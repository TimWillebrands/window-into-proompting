using PartyTown.Model;
using Orleans;

namespace PartyTown.World.Places;

[GenerateSerializer, Alias("PartyTown.World.Places.ShelterSlot")]
public sealed class ShelterSlot
{
    [Id(0)] public Guid Id { get; set; } = Guid.NewGuid();
    [Id(1)] public string Name { get; set; } = string.Empty;
    [Id(2)] public bool Open { get; set; } = true;
    [Id(3)] public Guid? HeldBy { get; set; }
    [Id(4)] public string? HeldByName { get; set; }
    [Id(5)] public long? HeldAt { get; set; }
    [Id(6)] public Guid? FreedBy { get; set; }
    [Id(7)] public string? FreedByName { get; set; }
    [Id(8)] public long? FreedAt { get; set; }
}

public static class ShelterStates
{
    public const string Empty = "empty";
    public const string Held = "held";
    public const string Freed = "freed";
}

public static class ShelterRules
{
    public static string StateOf(ShelterSlot slot)
        => slot.HeldBy is { } ? ShelterStates.Held
        : slot.FreedAt is null ? ShelterStates.Empty
        : ShelterStates.Freed;

    public static bool CanTake(ShelterSlot slot) => slot.Open && slot.HeldBy is null;

    public static bool CanRelease(ShelterSlot slot, Guid? by)
        => slot.HeldBy is { } holder && (by is null || by == holder);

    public static void Take(ShelterSlot slot, Guid who, string name, long now)
    {
        slot.HeldBy = who;
        slot.HeldByName = name;
        slot.HeldAt = now;
        slot.FreedBy = null;
        slot.FreedByName = null;
        slot.FreedAt = null;
    }

    public static void Release(ShelterSlot slot, Guid? by, string? byName, long now)
    {
        slot.FreedBy = by;
        slot.FreedByName = byName;
        slot.FreedAt = now;
        slot.HeldBy = null;
        slot.HeldByName = null;
        slot.HeldAt = null;
    }
}
