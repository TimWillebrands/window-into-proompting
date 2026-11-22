namespace PartyTown.World.Self;

public static class Belonging
{
    public const double HalfLifeDays = 10;
    public const double Rate = 0.3;
    public const int Kept = 12;
    public const double Haunt = 0.4;
    public const double PutOff = -0.4;
    public const double RunDownValence = -0.2;
    public const double WellValence = 0.2;
    public const double LowBattery = 0.5;
    public const int ShownHaunts = 3;
    public const string YoursHook = " (one of your places)";
    public const string NewHook = " (you've never been)";
    public const string HauntLine = "This has become one of your places.";
    public const string PutOffLine = "You haven't had good times here lately.";

    public static double Warmth(PlaceTie? tie, DateTimeOffset now)
        => tie is null || tie.At == 0 ? 0
            : tie.Warmth * Math.Pow(0.5, Math.Max(0, (now.ToUnixTimeMilliseconds() - tie.At) / 86_400_000.0) / HalfLifeDays);

    public static double Warmth(IReadOnlyDictionary<Guid, PlaceTie> places, Guid placeId, DateTimeOffset now)
        => Warmth(places.GetValueOrDefault(placeId), now);

    public static double Step(double warmth, double better)
    {
        var step = (Math.Clamp(better, 0, 1) - 0.5) * Rate;
        return Math.Clamp(warmth + step * (step > 0 ? 1 - warmth : 1 + warmth), -1, 1);
    }

    public static void Feel(Dictionary<Guid, PlaceTie> places, Guid placeId, double better, DateTimeOffset now)
    {
        if (Math.Clamp(better, 0, 1) == 0.5) return;
        var tie = places.TryGetValue(placeId, out var t) ? t : places[placeId] = new PlaceTie();
        tie.Warmth = Step(Warmth(tie, now), better);
        tie.At = now.ToUnixTimeMilliseconds();
        while (places.Count > Kept)
            places.Remove(places.MinBy(p => Math.Abs(Warmth(p.Value, now))).Key);
    }

    public static bool IsHaunt(double warmth) => warmth >= Haunt;

    public static bool IsPutOff(double warmth) => warmth <= PutOff;

    public static string? Line(double warmth, bool home)
        => home ? null : IsHaunt(warmth) ? HauntLine : IsPutOff(warmth) ? PutOffLine : null;

    public static bool RunDown(double valence, Reserves body)
        => valence <= RunDownValence || body.Battery < LowBattery || body.Tiredness >= Tiredness.Flagging || body is { Felt: true, Ailing: true };

    public static bool Well(double valence, Reserves body)
        => valence >= WellValence && body.Tiredness <= Tiredness.Fine && body.Hunger <= Hunger.Peckish && !(body is { Felt: true, Ailing: true });

    public static TimeSpan Stay(TimeSpan restless, double warmth) => restless * (1 + Math.Clamp(warmth, 0, 1));

    public static List<Guid> Haunts(IReadOnlyDictionary<Guid, PlaceTie> places, DateTimeOffset now, Func<Guid, bool> home)
        => places.Select(p => (p.Key, Warmth: Warmth(p.Value, now)))
            .Where(p => IsHaunt(p.Warmth) && !home(p.Key))
            .OrderByDescending(p => p.Warmth)
            .Take(ShownHaunts)
            .Select(p => p.Key)
            .ToList();
}

[GenerateSerializer, Alias("PartyTown.World.Self.PlaceTie")]
public sealed class PlaceTie
{
    [Id(0)] public double Warmth { get; set; }
    [Id(1)] public long At { get; set; }
}
