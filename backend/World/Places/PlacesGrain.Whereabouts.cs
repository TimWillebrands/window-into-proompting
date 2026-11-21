using System.Globalization;
using PartyTown.World.Self;

namespace PartyTown.World.Places;

public sealed partial class PlacesGrain
{
    private static readonly TimeSpan MinErrand = TimeSpan.FromHours(2);
    private static readonly TimeSpan PlanFresh = TimeSpan.FromMinutes(30);
    private readonly Dictionary<Guid, (List<Whereabout> Plan, long At)> _plans = new();
    private long _lastPlanCheck;

    private async Task WhereaboutsCycleAsync()
    {
        var now = Now();
        if (now - _lastPlanCheck < SleepCheckEvery.TotalMilliseconds) return;
        _lastPlanCheck = now;
        var local = time.GetLocalNow();
        var minute = local.Hour * 60 + local.Minute;
        var today = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var body in state.State.Bodies.Values.Where(b => !b.IsHuman && !b.Asleep && !b.IsWalking).ToList())
        {
            var stretch = Errands(PlanFor(body.ParticipantId, now)).FirstOrDefault(w => w.Start <= minute && minute < w.End);
            var company = Occupants(body.PlaceId).Any(o => o.ParticipantId != body.ParticipantId && o.SpotId == body.SpotId);
            var (bed, wake) = WindowFor(body.ParticipantId, now);
            var night = Sleep.IsNight(minute, bed, wake) || Sleep.TurnedIn(body.TurnedInUntil, now, minute, bed, wake);
            switch (ErrandStep(body, stretch, today, company, night))
            {
                case ErrandMove.Return:
                    await ReturnAsync(body.ParticipantId);
                    break;
                case ErrandMove.Go when Find(stretch!.LocaleId!.Value) is { Kind: PlaceKinds.Locale } where:
                    body.AwayStretch = $"{today}:{stretch.Start}";
                    await SendAwayAsync(body, where, ErrandAs(where.Name), byDay: true);
                    break;
            }
        }
    }

    internal enum ErrandMove { Stay, Go, Return }

    internal static ErrandMove ErrandStep(Body body, Whereabout? stretch, string today, bool company, bool night)
    {
        if (body.AwayByDay) return stretch?.LocaleId == body.PlaceId ? ErrandMove.Stay : ErrandMove.Return;
        if (stretch?.LocaleId is null || body.AwayAs is not null || company || night) return ErrandMove.Stay;
        return body.AwayStretch == $"{today}:{stretch.Start}" ? ErrandMove.Stay : ErrandMove.Go;
    }

    internal static IEnumerable<Whereabout> Errands(IEnumerable<Whereabout> plan)
        => plan.Where(w => w.LocaleId is not null && w.End - w.Start >= MinErrand.TotalMinutes);

    internal static string ErrandAs(string place)
        => place.Contains("work", StringComparison.OrdinalIgnoreCase) || place.Contains("office", StringComparison.OrdinalIgnoreCase)
            ? "at work"
            : "out";

    private List<Whereabout> PlanFor(Guid id, long now)
    {
        if (_plans.TryGetValue(id, out var p) && now - p.At < PlanFresh.TotalMilliseconds) return p.Plan;
        _ = RefreshPlanAsync(id);
        return p.Plan ?? new List<Whereabout>();
    }

    private async Task RefreshPlanAsync(Guid id)
    {
        try
        {
            var plan = await GrainFactory.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(PartyId, id)).GetWhereaboutsAsync();
            _plans[id] = (plan, Now());
        }
        catch (Exception ex)
        {
            _plans[id] = (new List<Whereabout>(), Now());
            logger.LogDebug(ex, "Whereabouts unavailable for {Persona}", id);
        }
    }
}
