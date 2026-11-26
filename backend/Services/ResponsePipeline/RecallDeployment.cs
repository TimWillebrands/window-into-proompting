using PartyTown.Services.Memory;

namespace PartyTown.Services.ResponsePipeline;

public static class RecallDeployment
{
    public const double AutoDeploySalienceFloor = 0.7;

    public static (RecalledMemory? Memory, bool AutoDeployed) ResolvePick(
        Gravity.Weighed weighed,
        IReadOnlyList<RecalledMemory> recalled)
    {
        if (weighed.Memory is int pick && pick >= 1 && pick <= recalled.Count)
            return (recalled[pick - 1], false);

        if (weighed.RecallWeighed)
            return (null, false);

        var auto = recalled.FirstOrDefault(m =>
            m.Arm == RecallArm.Relevant &&
            m.Salience >= AutoDeploySalienceFloor &&
            m.RecallCount == 0);

        return (auto, auto is not null);
    }
}
