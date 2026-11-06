using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World.Desires;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.World.Relations;

/// <summary>Someone dealt with a closed door: where, whose, and how often lately.</summary>
public sealed record Door(Guid PlaceId, string PlaceName, Guid HostId, Guid VisitorId, int Moments);

/// <summary>Pure key rules (ADR 0036): who's close enough for one to come up, and when it's turned sour.</summary>
public static class KeyRules
{
    public const int ToThinkOfIt = 2;
    public const double WantAt = 0.7;
    public const int Wanted = 2;
    public const int Sore = 3;
    public static readonly TimeSpan WantEvery = TimeSpan.FromDays(3);

    public static bool Exes(RelationBook book, Guid a, Guid b)
        => book.CurrentBetween(a, b) is null && book.Relations.Any(r => r.IsRomantic() && r.Involves(a) && r.Involves(b));

    /// <summary>Things went wrong between them, so a key is a question that needs settling.</summary>
    public static bool Sour(RelationBook book, Guid me, Guid other, TieRead mine)
        => mine.Warmth <= RelationRules.Strained || Exes(book, me, other);

    /// <summary>What they are, or how well they know each other both ways; strain rules it out.</summary>
    public static bool Close(RelationBook book, Guid me, Guid other, TieRead mine, TieRead theirs)
        => !Sour(book, me, other, mine) && theirs.Warmth > RelationRules.Strained
           && (book.CurrentBetween(me, other) is not null || (mine.Exchanges >= RelationRules.KnownAfter && theirs.Exchanges >= RelationRules.KnownAfter));

    /// <summary>One live want about a door at a time, and no new one right after the last.</summary>
    public static bool MayWant(IEnumerable<Desire> acquired, long now)
        => !acquired.Any(d => d.PlaceId is not null && (d.Status == DesireStatus.Active || now - d.Since < WantEvery.TotalMilliseconds));

    public static bool Wants(IEnumerable<Desire> acquired, Guid placeId, Guid other)
        => acquired.Any(d => d.Status == DesireStatus.Active && d.PlaceId == placeId && d.TargetId == other);
}

/// <summary>
/// Wanting a key forms at the door (ADR 0036), the way Mimesis forms in talk: System One judges each side in character,
/// a hit is voiced once, and it's capped. Detached and non-fatal; humans are left out.
/// </summary>
public sealed class KeyWants(ISystemOneJudge judge, IGrainFactory grains, IWorldLlm llm, ILogger<KeyWants> logger)
{
    private const string Key = "want";

    public async Task ConsiderAsync(Guid partyId, Door door)
    {
        try
        {
            var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
            if (cast.FirstOrDefault(c => c.Id == door.VisitorId) is not { DefaultDriver: DriverKind.LLM } visitor) return;
            if (cast.FirstOrDefault(c => c.Id == door.HostId) is not { DefaultDriver: DriverKind.LLM } host) return;
            var book = await grains.GetGrain<IRelationsGrain>(partyId).GetAsync();
            var mine = await Ties(partyId, visitor.Id).GetTieAsync(host.Id);
            var theirs = await Ties(partyId, host.Id).GetTieAsync(visitor.Id);
            if (!KeyRules.Close(book, visitor.Id, host.Id, mine, theirs)) return;

            var standing = RelationRules.Standing(book, visitor.Id, host.Id);
            var (v, h) = (First(visitor), First(host));
            await WantAsync(partyId, door, visitor, host, DesireKind.Having, standing,
                $"{v} wishes they didn't have to knock at {door.PlaceName} any more.",
                $"You put in one short line, second person, what {v} wants: to stop being kept outside {door.PlaceName}, {h}'s place to open or not. Their words, not a slogan.");
            await WantAsync(partyId, door, host, visitor, DesireKind.Being, standing,
                $"{h} would rather hand {v} a key to {door.PlaceName} than keep opening the door, being who they are.",
                $"You put in one short line, second person, what {h} wants: to give {v} their own way into {door.PlaceName}, and what letting them in would mean. Their words, not a slogan.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Key want failed at {Place}", door.PlaceName);
        }
    }

    private async Task WantAsync(Guid partyId, Door door, CastMember who, CastMember other, DesireKind kind, string? standing, string question, string voice)
    {
        var desires = grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, who.Id));
        var acquired = (await desires.GetAsync()).Acquired;
        // Another round at the door keeps a want alive; that's what fading is measured against.
        if (acquired.FirstOrDefault(d => d.Status == DesireStatus.Active && d.PlaceId == door.PlaceId) is { } live)
        {
            await desires.TouchAsync(live.Id);
            return;
        }
        var now = DateTimeOffset.UtcNow;
        if (door.Moments < KeyRules.ToThinkOfIt || !KeyRules.MayWant(acquired, now.ToUnixTimeMilliseconds())) return;
        var weigh = judge.For(who.Id);
        if (!weigh.Enabled) return;

        var state = $"""
            {who.Name}. {(LivedSheets.Lived(who.Id, who.SystemPrompt) is { } lived ? LivedSheets.Bio(lived) : null) ?? who.Bio ?? who.SystemPrompt}
            {First(who)} and {First(other)}: {standing ?? "they know each other well"}.
            {door.PlaceName} is closed: it's opened from inside. That door has been in the way {door.Moments} times this week.
            """;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var answers = await weigh.AskAsync(state, new Dictionary<string, SystemOneQuestion> { [Key] = new NoulQuestion(question) }, cts.Token);
        var p = answers.Noul(Key, 0);
        logger.LogInformation("Key want {Who} at {Place}: p={P:F2}", who.Name, door.PlaceName, p);
        if (p < KeyRules.WantAt) return;

        var json = await llm.For(who.Id).CompleteJsonAsync(JobComplexity.General, $$"""{{voice}} Output ONLY JSON: {"want": "<the line>"}""", state, cts.Token, 0.6, ["want"]);
        if (DesireRules.Parse(json?["want"]?.ToString(), kind, DesireSource.Play, now) is not { } desire) return;
        desire.ItemKey = null;
        desire.PlaceId = door.PlaceId;
        desire.TargetId = other.Id;
        await desires.AddAsync(desire);
        logger.LogInformation("Key want: {Who} wants \"{Want}\"", who.Name, desire.Text);
    }

    private IPersonaTiesGrain Ties(Guid partyId, Guid who) => grains.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, who));

    private static string First(CastMember who) => who.Name.Split(' ')[0];
}
