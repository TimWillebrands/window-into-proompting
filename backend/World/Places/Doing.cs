using Orleans.Concurrency;
using PartyTown.Grains.Generation;

namespace PartyTown.World.Places;

/// <summary>What the Muse needs to write one Take.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.TakeBrief")]
public sealed record TakeBrief(
    [property: Id(0)] Guid Who, [property: Id(1)] string Name, [property: Id(2)] string? Identity,
    [property: Id(3)] string Place, [property: Id(4)] string PlaceAbout, [property: Id(5)] string Thing,
    [property: Id(6)] string Do, [property: Id(7)] string Gives, [property: Id(8)] bool Leaves,
    [property: Id(9)] string? Why, [property: Id(10)] string When, [property: Id(11)] List<string> Earlier);

/// <summary>What doing something gave: the line for the feed, and the Take on its own.</summary>
[GenerateSerializer, Alias("PartyTown.World.Places.Done")]
public sealed record Done([property: Id(0)] string Summary, [property: Id(1)] string Take, [property: Id(2)] string Thing, [property: Id(3)] string Place);

[Alias("PartyTown.World.Places.IPlaceMuseGrain")]
public interface IPlaceMuseGrain : IGrainWithIntegerKey
{
    [Alias("TakeAsync")] Task<string?> TakeAsync(TakeBrief brief);
}

/// <summary>Writes Takes away from the Places grain (ADR 0049), so a slow model never holds the World up.</summary>
[StatelessWorker]
public sealed class PlaceMuseGrain(IWorldLlm llm, ILogger<PlaceMuseGrain> logger) : Grain, IPlaceMuseGrain
{
    public const string Finds = """
        You are the world around someone, not the person. They just did something at a place; say what they come away with.
        One line, at most 26 words. Concrete and particular: a name, a number, a colour, a small physical fact. Something only this place, at this hour, would give them.
        No feelings, no lesson, no metaphor. Nothing that changes the place or brings in other people. Not one of the earlier ones, and not the same kind of thing as them.
        Write it as what's there, without "you" or their name: "a 1974 Dolly Parton single, sleeve taped at the spine, €3 in pencil".
        Output ONLY JSON: {"take": "..."}
        """;

    public const string Leaves = """
        You're at a place, doing something that stays there for whoever comes next. Write what you leave, in your own words.
        At most 22 words: the thing itself (the wish, the line, the move), not a description of you leaving it. Plain, and about your own life as it is right now.
        No quotation marks, no signature. Not like the earlier ones.
        Output ONLY JSON: {"take": "..."}
        """;

    public async Task<string?> TakeAsync(TakeBrief b)
    {
        var system = b.Leaves && b.Identity is { Length: > 0 } self ? $"{self}\n\n{Leaves}" : Finds;
        var user = $"""
            Place: {b.Place}. {b.PlaceAbout}
            Thing: {b.Thing}
            What {b.Name} does: {b.Do}
            What it gives: {b.Gives}
            {(string.IsNullOrWhiteSpace(b.Why) ? string.Empty : $"Why, in their words: {WorldStream.Clip(b.Why, 240)}")}
            It's {b.When}.
            {(b.Earlier.Count == 0 ? string.Empty : $"Earlier here:\n- {string.Join("\n- ", b.Earlier)}")}
            """;
        try
        {
            var json = await llm.For(b.Who).CompleteJsonAsync(JobComplexity.General, system, user, CancellationToken.None, 0.95, new[] { "take" });
            var take = json?["take"]?.ToString().Trim().Trim('"', '“', '”').Trim();
            return string.IsNullOrWhiteSpace(take) ? null : WorldStream.Clip(take, 220);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "No take for {Thing} at {Place}", b.Thing, b.Place);
            return null;
        }
    }
}

/// <summary>Doing something at a Place (ADR 0049): read, let the Muse write the Take, then apply; never holding the Places grain.</summary>
public static class Doing
{
    /// <param name="own">The doer's own words to leave, when they have them (a Player typing).</param>
    public static async Task<Done?> DoAsync(IGrainFactory grains, Guid partyId, Guid who, string? identity, Guid placeId, string furniId, string? why, string? own, DateTimeOffset now)
    {
        var places = grains.GetGrain<IPlacesGrain>(partyId);
        var snapshot = await places.GetSnapshotAsync();
        var place = snapshot.Places.FirstOrDefault(p => p.Id == placeId);
        var body = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == who);
        if (place?.Furni.FirstOrDefault(f => f.Id == furniId) is not { Do: { } what } furni) return null;
        if (body is null || body.IsWalking || body.PlaceId != placeId) return null;

        var take = string.IsNullOrWhiteSpace(own) ? null : WorldStream.Clip(own.Trim(), 220);
        take ??= await grains.GetGrain<IPlaceMuseGrain>(0).TakeAsync(new TakeBrief(
            who, body.Name.Split(' ')[0], identity, place.Name, place.Description, furni.Name, what,
            furni.Gives ?? "one small, specific thing they notice or find", furni.Leaves, why,
            now.ToLocalTime().ToString("dddd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            furni.Takes.TakeLast(5).Select(t => t.Text).ToList()));
        var summary = await places.DoFeatureAsync(who, placeId, furniId, take ?? string.Empty);
        return summary is null ? null : new Done(summary, take ?? string.Empty, furni.Name, place.Name);
    }
}
