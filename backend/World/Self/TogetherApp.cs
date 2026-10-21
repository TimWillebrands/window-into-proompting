using System.Text;
using PartyTown.World.Apps;
using PartyTown.World.Places;

namespace PartyTown.World.Self;

/// <summary>
/// Touch between people at the same spot (ADR 0021): a high-five, a hug, holding hands, a slow dance. Offered only
/// when the Tie allows it *both ways*, so closeness is earned and mutual. Solo poses stay in Places' own spot intent.
/// </summary>
public sealed class TogetherApp(IGrainFactory grains) : IWorldApp
{
    public string Id => "together";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var places = grains.GetGrain<IPlacesGrain>(ctx.PartyId);
        var snapshot = await places.GetSnapshotAsync();
        var me = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == ctx.Self.Id);
        if (me is null || me.IsWalking || me.Asleep) return null;
        var spot = snapshot.Places.FirstOrDefault(p => p.Id == me.PlaceId)?.Spots.FirstOrDefault(s => s.Id == me.SpotId);
        if (spot is null) return null;
        var company = snapshot.Bodies
            .Where(b => b.ParticipantId != ctx.Self.Id && b.PlaceId == me.PlaceId && b.SpotId == me.SpotId && !b.IsWalking && !b.Asleep)
            .ToList();
        if (company.Count == 0) return null;

        var allowed = Poses.Allowed(spot, company: true).Where(Poses.Pair.Contains).ToList();
        var sb = new StringBuilder();
        var affordances = new List<Affordance>();
        if (me.Pose is { } pose && Poses.Pair.Contains(pose) && me.PoseWith is { } with)
        {
            sb.AppendLine($"You're {Poses.Describe(pose, spot, ctx.NameOf(with))}.");
            affordances.Add(new Affordance(Id, $"Let go of {ctx.NameOf(with)}", 0, with, "end"));
        }
        foreach (var other in company.Take(3))
        {
            var myTies = grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id));
            var theirTies = grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(ctx.PartyId, other.ParticipantId));
            var mine = await myTies.GetTieAsync(other.ParticipantId);
            var theirs = await theirTies.GetTieAsync(ctx.Self.Id);
            var mutual = await myTies.GetClosestAsync() == other.ParticipantId && await theirTies.GetClosestAsync() == ctx.Self.Id;
            var name = ctx.NameOf(other.ParticipantId);
            foreach (var p in Touch.Offered(allowed, mine, theirs, ctx.IsHuman(other.ParticipantId), mutual))
                affordances.Add(new Affordance(Id, Touch.Label(p, name), 0, other.ParticipantId, p));
        }
        if (affordances.Count == 0) return null;
        if (sb.Length == 0) sb.Append($"Here with you {spot.At} {spot.Name}: {string.Join(", ", company.Take(3).Select(b => ctx.NameOf(b.ParticipantId)))}.");
        return new AppPerception(Id, "Together (being close to someone here)", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } other || affordance.Verb is not { } verb) return null;
        var places = grains.GetGrain<IPlacesGrain>(ctx.PartyId);
        if (verb == "end")
        {
            await places.SetPoseAsync(ctx.Self.Id, null, null);
            return new ActOutcome("pose", $"lets go of {ctx.NameOf(other)}", null, other);
        }
        var done = await places.SetPoseAsync(ctx.Self.Id, verb, other);
        return done is null ? null : new ActOutcome("pose", $"is {done}", null, other);
    }
}

/// <summary>Pure: which pair poses a Tie allows, both ways.</summary>
public static class Touch
{
    /// <summary>Pair poses both Ties allow; intimate ones only between mutual closest people, never initiated toward a human.</summary>
    public static List<string> Offered(IReadOnlyCollection<string> allowed, TieRead mine, TieRead theirs, bool otherIsHuman, bool mutualClosest)
        => allowed.Where(p => Allows(p, mine) && Allows(p, theirs) && !(Intimate(p) && (otherIsHuman || !mutualClosest))).ToList();

    public static bool Intimate(string pose) => pose is Poses.HoldHands or Poses.SlowDance;

    public static bool Allows(string pose, TieRead tie) => pose switch
    {
        Poses.HighFive => tie.Exchanges >= 2 && tie.Warmth > -0.2,
        Poses.Hug => tie.Exchanges >= 10 && tie.Warmth >= 0.1 || tie.Exchanges >= 40 && tie.Warmth >= 0,
        Poses.HoldHands or Poses.SlowDance => tie.Exchanges >= 10 && tie.Warmth >= 0.4,
        _ => false,
    };

    public static string Label(string pose, string name) => pose switch
    {
        Poses.HighFive => $"High-five {name}",
        Poses.Hug => $"Hug {name}",
        Poses.HoldHands => $"Hold {name}'s hand",
        Poses.SlowDance => $"Slow-dance with {name}",
        _ => $"{pose} with {name}",
    };
}
