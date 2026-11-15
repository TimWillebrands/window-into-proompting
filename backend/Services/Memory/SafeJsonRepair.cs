using JsonRepairSharp;

namespace PartyTown.Services.Memory;

internal static class SafeJsonRepair
{
    private static readonly Lock Gate = new();

    public static string Repair(string json)
    {
        lock (Gate) return JsonRepair.RepairJson(json, JsonRepair.InputType.LLM);
    }
}
