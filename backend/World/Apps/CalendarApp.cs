using System.Globalization;
using System.Text;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Apps;

public sealed class CalendarApp(IGrainFactory grains, IWorldLlm llm) : IWorldApp
{
    private const int MaxUpcoming = 3;
    private static readonly TimeSpan Horizon = TimeSpan.FromHours(12);

    public string Id => "calendar";

    public async Task<AppPerception?> PerceiveAsync(LifeContext ctx, CancellationToken ct)
    {
        var nowMs = ctx.Now.ToUnixTimeMilliseconds();
        var upcoming = (await grains.GetGrain<ICalendarGrain>(ctx.PartyId).GetGatheringsAsync())
            .Where(g => g.Status == GatheringStatus.Planned && g.StartsAt > nowMs && g.StartsAt - nowMs < Horizon.TotalMilliseconds)
            .ToList();

        var sb = new StringBuilder();
        var affordances = new List<Affordance>();
        var (bed, wake) = await SleepWindowAsync(ctx);
        foreach (var g in upcoming)
        {
            var going = g.AttendeeIds.Contains(ctx.Self.Id);
            var who = string.Join(", ", g.AttendeeIds.Where(id => id != ctx.Self.Id).Select(ctx.NameOf).Take(4));
            sb.AppendLine($"- {When(g.StartsAt, ctx.Now)}: \"{g.Title}\" {g.Where}, hosted by {(g.HostId == ctx.Self.Id ? "you" : g.HostName)}"
                + $"{(who.Length > 0 ? $" — {who} going" : "")}{(going ? " (you're going)" : "")}");
            if (!going && !Places.Sleep.IsNight(LocalMinute(DateTimeOffset.FromUnixTimeMilliseconds(g.StartsAt)), bed, wake))
                affordances.Add(new Affordance(Id, $"Say you'll come to \"{g.Title}\" ({When(g.StartsAt, ctx.Now)})", 1, g.Id, "rsvp"));
        }

        var goingSoon = upcoming.Any(g => g.AttendeeIds.Contains(ctx.Self.Id) && g.StartsAt - nowMs < TimeSpan.FromHours(2).TotalMilliseconds);
        if (upcoming.Count < MaxUpcoming && !goingSoon && GatheringRules.MayPlan(LocalMinute(ctx.Now), bed, wake))
        {
            var placesGrain = grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId);
            var snapshot = await placesGrain.GetSnapshotAsync();
            var publicPlaces = snapshot.Places.Where(p => p.Kind == Places.PlaceKinds.Public && !p.Dormant && !p.Private && upcoming.All(g => g.PlaceId != p.Id)).ToList();
            if (publicPlaces.Count > 0)
            {
                var place = publicPlaces[Random.Shared.Next(publicPlaces.Count)];
                affordances.Add(new Affordance(Id, $"Plan to meet people {place.At} {place.Name} later", 0, place.Id, "plan-place"));
            }
            var regular = (await placesGrain.GetVisitDaysAsync(ctx.Self.Id))
                .Where(v => v.Value >= Places.PlacesGrain.RegularDays)
                .Select(v => snapshot.Places.FirstOrDefault(p => p.Id == v.Key))
                .FirstOrDefault(p => p is { Kind: Places.PlaceKinds.Locale } && !Places.PlacesGrain.Unnamed(p.Name));
            if (regular is not null)
                affordances.Add(new Affordance(Id, $"Plan to get people together {regular.At} {regular.Name}, where you often are", 0, regular.Id, "plan-place"));
        }

        if (sb.Length == 0) sb.Append("Nothing planned yet.");
        return new AppPerception(Id, "Calendar (planned get-togethers)", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target) return null;

        if (affordance.Verb == "rsvp")
            return (await RsvpAsync(ctx.PartyId, target, ctx.Self.Id, true))?.Outcome;

        if (affordance.Verb != "plan-place") return null;
        var meetAt = (await grains.GetGrain<Places.IPlacesGrain>(ctx.PartyId).GetSnapshotAsync()).Places.FirstOrDefault(p => p.Id == target);
        if (meetAt is null) return null;
        var whereText = $"in person {meetAt.At} {meetAt.Name}";
        var (bed, wake) = await SleepWindowAsync(ctx);
        var latest = GatheringRules.StartIn(240, LocalMinute(ctx.Now), bed, wake);
        var json = await llm.For(ctx.Self.Id).CompleteJsonAsync(JobComplexity.General,
            $"{WorldLlm.Identity(ctx.Self)}\n\nYou're planning a small get-together {whereText}. It's {ctx.Now.ToLocalTime():HH:mm} now. Give it a short, inviting title in your own voice, and say how many minutes from now it starts (between 15 and {latest}).\nOutput ONLY JSON: {PlanShape}",
            $"What you have in mind: {intent}", ct, 0.9, ["title", "minutesFromNow"]);
        var title = json?["title"]?.ToString();
        if (string.IsNullOrWhiteSpace(title)) return null;
        var minutes = int.TryParse(json?["minutesFromNow"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 60;
        var startsAt = ctx.Now.AddMinutes(GatheringRules.StartIn(minutes, LocalMinute(ctx.Now), bed, wake)).ToUnixTimeMilliseconds();

        return (await PlanAtPlaceAsync(ctx.PartyId, ctx.Self.Id, ctx.Self.Name, title, meetAt, startsAt, ctx.Now)).Outcome;
    }

    public async Task<(Gathering Gathering, ActOutcome Outcome)> PlanAtPlaceAsync(
        Guid partyId, Guid hostId, string hostName, string title, Places.Place place, long startsAt, DateTimeOffset now)
    {
        var planned = await grains.GetGrain<ICalendarGrain>(partyId).PlanAsync(hostId, hostName, title, place.Id, place.Name, startsAt);
        return (planned, new ActOutcome("plan", $"planned \"{planned.Title}\" {place.At} {place.Name} for {When(startsAt, now)}", null));
    }

    public async Task<(Gathering Gathering, ActOutcome Outcome)?> RsvpAsync(Guid partyId, Guid gatheringId, Guid personaId, bool going)
    {
        var g = await grains.GetGrain<ICalendarGrain>(partyId).RsvpAsync(gatheringId, personaId, going);
        if (g is null) return null;
        var summary = going ? $"will come to \"{g.Title}\" {g.Where}" : $"won't make it to \"{g.Title}\" {g.Where}";
        return (g, new ActOutcome("rsvp", summary, null));
    }

    private static int LocalMinute(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        return local.Hour * 60 + local.Minute;
    }

    private async Task<(int Bed, int Wake)> SleepWindowAsync(LifeContext ctx)
    {
        try
        {
            var day = await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(ctx.PartyId, ctx.Self.Id)).GetDayWindowAsync();
            return Places.Sleep.Window(ctx.Self.Id, day.FirstBeatMinute, day.LastBeatMinute);
        }
        catch (Exception)
        {
            return Places.Sleep.Window(ctx.Self.Id);
        }
    }

    private const string PlanShape = """{"title": "<short title>", "minutesFromNow": "<number>"}""";

    private static string When(long unixMs, DateTimeOffset now)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        var mins = (int)Math.Round((at - now).TotalMinutes);
        return mins < 60 ? $"in {Math.Max(mins, 1)} min" : $"at {at.ToLocalTime():HH:mm}";
    }
}
