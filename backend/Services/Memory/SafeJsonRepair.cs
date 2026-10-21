using JsonRepairSharp;

namespace PartyTown.Services.Memory;

/// <summary>JsonRepairSharp keeps static parser state; concurrent calls corrupt each other, so serialize them.</summary>
internal static class SafeJsonRepair
{
    private static readonly Lock Gate = new();

    public static string Repair(string json)
    {
        lock (Gate) return JsonRepair.RepairJson(json, JsonRepair.InputType.LLM);
    }
}
