using System.Globalization;
using System.Text;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Apps;

/// <summary>Calendar: plan Gatherings in Rooms and say you'll come. Anticipation as an Affordance.</summary>
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
        foreach (var g in upcoming)
        {
            var going = g.AttendeeIds.Contains(ctx.Self.Id);
            var who = string.Join(", ", g.AttendeeIds.Where(id => id != ctx.Self.Id).Select(ctx.NameOf).Take(4));
            sb.AppendLine($"- {When(g.StartsAt, ctx.Now)}: \"{g.Title}\" in #{g.RoomName}, hosted by {(g.HostId == ctx.Self.Id ? "you" : g.HostName)}"
                + $"{(who.Length > 0 ? $" — {who} going" : "")}{(going ? " (you're going)" : "")}");
            if (!going)
                affordances.Add(new Affordance(Id, $"Say you'll come to \"{g.Title}\" ({When(g.StartsAt, ctx.Now)})", 1, g.Id, "rsvp"));
        }

        if (upcoming.Count < MaxUpcoming)
        {
            var myRooms = new List<ChatGroupInfo>();
            foreach (var room in ctx.Threads.Where(t => t.Kind == ChatGroupKinds.Room))
                if ((await grains.GetGrain<IChatGroupGrain>(room.Id).GetParticipantIdsAsync()).Count(id => id != Narrator.PersonaId) >= 2)
                    myRooms.Add(room);
            foreach (var room in myRooms.OrderBy(_ => Random.Shared.Next()).Take(1))
                affordances.Add(new Affordance(Id, $"Plan a get-together in #{room.Name} for later", 0, room.Id, "plan"));
        }

        if (sb.Length == 0) sb.Append("Nothing planned yet.");
        return new AppPerception(Id, "Calendar (planned get-togethers)", sb.ToString().TrimEnd(), affordances);
    }

    public async Task<ActOutcome?> ActAsync(LifeContext ctx, Affordance affordance, string intent, string? thought, CancellationToken ct)
    {
        if (affordance.TargetId is not { } target) return null;

        if (affordance.Verb == "rsvp")
            return (await RsvpAsync(ctx.PartyId, target, ctx.Self.Id, true))?.Outcome;

        var room = ctx.Threads.FirstOrDefault(t => t.Id == target);
        if (room is null) return null;
        var json = await llm.CompleteJsonAsync(JobComplexity.General,
            $"{WorldLlm.Identity(ctx.Self)}\n\nYou're planning a small get-together in the chat room #{room.Name}. Give it a short, inviting title in your own voice, and say how many minutes from now it starts (between 15 and 240).\nOutput ONLY JSON: {PlanShape}",
            $"What you have in mind: {intent}", ct, 0.9, ["title", "minutesFromNow"]);
        var title = json?["title"]?.ToString();
        if (string.IsNullOrWhiteSpace(title)) return null;
        var minutes = int.TryParse(json?["minutesFromNow"]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m) ? m : 60;
        var startsAt = ctx.Now.AddMinutes(Math.Clamp(minutes, 15, 240)).ToUnixTimeMilliseconds();

        return (await PlanAsync(ctx.PartyId, ctx.Self.Id, ctx.Self.Name, title, room, startsAt, ctx.Now)).Outcome;
    }

    /// <summary>Commit a Gathering — Persona-titled or Player-typed alike.</summary>
    public async Task<(Gathering Gathering, ActOutcome Outcome)> PlanAsync(
        Guid partyId, Guid hostId, string hostName, string title, ChatGroupInfo room, long startsAt, DateTimeOffset now)
    {
        var planned = await grains.GetGrain<ICalendarGrain>(partyId).PlanAsync(hostId, hostName, title, room.Id, room.Name, startsAt);
        return (planned, new ActOutcome("plan", $"planned \"{planned.Title}\" in #{room.Name} for {When(startsAt, now)}", room.Id));
    }

    /// <summary>Commit an RSVP; null when the Gathering is gone.</summary>
    public async Task<(Gathering Gathering, ActOutcome Outcome)?> RsvpAsync(Guid partyId, Guid gatheringId, Guid personaId, bool going)
    {
        var g = await grains.GetGrain<ICalendarGrain>(partyId).RsvpAsync(gatheringId, personaId, going);
        if (g is null) return null;
        var summary = going ? $"will come to \"{g.Title}\" in #{g.RoomName}" : $"won't make it to \"{g.Title}\" in #{g.RoomName}";
        return (g, new ActOutcome("rsvp", summary, g.RoomId));
    }

    private const string PlanShape = """{"title": "<short title>", "minutesFromNow": "<number>"}""";

    private static string When(long unixMs, DateTimeOffset now)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        var mins = (int)Math.Round((at - now).TotalMinutes);
        return mins < 60 ? $"in {Math.Max(mins, 1)} min" : $"at {at.ToLocalTime():HH:mm}";
    }
}
