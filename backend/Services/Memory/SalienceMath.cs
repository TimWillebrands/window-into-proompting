namespace PartyTown.Services.Memory;

public static class SalienceMath
{
    public const double DecayHalfLifeDays = 7.0;

    public const double DefaultWeight = 0.5;

    public const double UseBonusHalfLifeDays = 14.0;

    private const double UseBonusPerRecall = 0.15;
    private const double UseBonusCap = 0.5;

    public static double Decay(TimeSpan age)
        => Math.Pow(0.5, Math.Max(0, age.TotalDays) / DecayHalfLifeDays);

    public static double UseBonus(int recallCount, DateTimeOffset? lastRecalled, DateTimeOffset now)
    {
        if (recallCount <= 0)
        {
            return 0;
        }
        var bonus = Math.Min(UseBonusCap, UseBonusPerRecall * Math.Log(1 + recallCount));
        if (lastRecalled is null)
        {
            return bonus;
        }
        return bonus * Math.Pow(0.5, Math.Max(0, (now - lastRecalled.Value).TotalDays) / UseBonusHalfLifeDays);
    }

    public const double RefractoryHours = 4.0;

    public static double Refractory(DateTimeOffset? lastRecalled, DateTimeOffset now)
        => lastRecalled is null
            ? 1
            : 0.25 + 0.75 * Math.Clamp((now - lastRecalled.Value).TotalHours / RefractoryHours, 0, 1);

    public static double Score(
        double weight,
        DateTimeOffset capturedAt,
        int recallCount,
        DateTimeOffset? lastRecalled,
        DateTimeOffset now)
        => (weight * Decay(now - capturedAt) + UseBonus(recallCount, lastRecalled, now)) * Refractory(lastRecalled, now);
}
