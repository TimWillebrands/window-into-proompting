using PartyTown.World.Self;

namespace PartyTown.World.Places;

public static class Bearing
{
    public const string Unwell = "looks unwell";
    public const string Spent = "looks dead on their feet";
    public const string Famished = "looks like they haven't eaten all day";

    public static string? Of(Reserves r)
        => !r.Felt ? null
            : r.Ailing ? Unwell
            : r.Tiredness == Tiredness.Spent ? Spent
            : r.Hunger == Hunger.Famished ? Famished
            : null;

    public static string? Shown(Body body, long now) => body.BearingUntil > now ? body.Bearing : null;

    public static bool Due(Body body, string? bearing, long now)
        => bearing != Shown(body, now) || (bearing is not null && body.BearingUntil - now < Bio.FeltFor.TotalMilliseconds / 2);
}
