using System.Text.Json.Serialization;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.World.Relations;

[GenerateSerializer, Alias("PartyTown.World.Relations.Relation")]
public sealed class Relation
{
    [Id(0)] public Guid Id { get; set; } = Guid.NewGuid();
    [Id(1)] public Guid AId { get; set; }
    [Id(2)] public Guid BId { get; set; }
    [Id(3)] public RelationKind Kind { get; set; }
    [Id(4)] public string? Story { get; set; }
    [Id(5)] public long Since { get; set; }
    [Id(6)] public long? EndedAt { get; set; }
    [Id(7)] public bool LiveTogether { get; set; }
}

[GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter<RelationKind>))]
public enum RelationKind
{
    Together = 0,
    Married = 1,
    Family = 2,
    Friends = 3,
    Other = 4,
}

[GenerateSerializer, Alias("PartyTown.World.Relations.RelationAsk")]
public sealed class RelationAsk
{
    [Id(0)] public Guid Id { get; set; } = Guid.NewGuid();
    [Id(1)] public Guid FromId { get; set; }
    [Id(2)] public Guid ToId { get; set; }
    [Id(3)] public AskKind Kind { get; set; }
    [Id(4)] public long At { get; set; }
    [Id(5)] public bool? Yes { get; set; }
    [Id(6)] public long? AnsweredAt { get; set; }
    [Id(7)] public Guid? PlaceId { get; set; }
    [Id(8)] public string? About { get; set; }
}

[GenerateSerializer]
[JsonConverter(typeof(JsonStringEnumConverter<AskKind>))]
public enum AskKind
{
    GoOut = 0,
    MoveIn = 1,
    Marry = 2,
    Key = 3,
}

[GenerateSerializer, Alias("PartyTown.World.Relations.RelationBook")]
public sealed class RelationBook
{
    [Id(0)] public List<Relation> Relations { get; set; } = new();
    [Id(1)] public List<RelationAsk> Asks { get; set; } = new();
}

public static class RelationRules
{
    public static readonly TimeSpan AskLasts = TimeSpan.FromDays(1);
    public static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(3);
    public static readonly TimeSpan MoveInAfter = TimeSpan.FromDays(3);
    public static readonly TimeSpan MarryAfter = TimeSpan.FromDays(14);
    public const double WarmEnough = 0.2;
    public const double Strained = -0.2;
    public const double KnownAfter = 10;
    private const int MaxLines = 3;

    public static bool Involves(this Relation r, Guid id) => r.AId == id || r.BId == id;
    public static Guid Other(this Relation r, Guid id) => r.AId == id ? r.BId : r.AId;
    public static bool IsCurrent(this Relation r) => r.EndedAt is null;
    public static bool IsRomantic(this Relation r) => r.Kind is RelationKind.Together or RelationKind.Married;

    public static Relation? CurrentBetween(this RelationBook book, Guid a, Guid b)
        => book.Relations.FirstOrDefault(r => r.IsCurrent() && r.Involves(a) && r.Involves(b));

    public static bool Partners(this RelationBook book, Guid a, Guid b)
        => book.CurrentBetween(a, b)?.IsRomantic() == true;

    public static bool HasPartner(this RelationBook book, Guid who)
        => book.Relations.Any(r => r.IsCurrent() && r.IsRomantic() && r.Involves(who));

    public static bool SharesHome(this RelationBook book, Guid who)
        => book.Relations.Any(r => r.IsCurrent() && r.LiveTogether && r.Involves(who));

    public static bool IsOpen(this RelationAsk ask, long now) => ask.Yes is null && now - ask.At < AskLasts.TotalMilliseconds;

    public static IEnumerable<RelationAsk> OpenTo(this RelationBook book, Guid who, long now)
        => book.Asks.Where(a => a.ToId == who && a.IsOpen(now));

    public static IEnumerable<RelationAsk> FreshlyAnswered(this RelationBook book, Guid asker, long now)
        => book.Asks.Where(a => a.FromId == asker && a.AnsweredAt is { } at && now - at < AskLasts.TotalMilliseconds);

    private static bool Pair(RelationAsk ask, Guid a, Guid b)
        => (ask.FromId == a && ask.ToId == b) || (ask.FromId == b && ask.ToId == a);

    public static bool MayAsk(this RelationBook book, Guid from, Guid to, AskKind kind, long now)
        => from != to && !book.Asks.Any(a => Pair(a, from, to) && (a.IsOpen(now) || (a.Kind == kind && now - a.At < AskAgainAfter.TotalMilliseconds)));

    public static List<AskKind> Askable(RelationBook book, Guid me, Guid other, TieRead mine, TieRead theirs, bool otherIsHuman, bool mutualClosest, long now)
    {
        var asks = new List<AskKind>();
        if (otherIsHuman) return asks;
        var between = book.CurrentBetween(me, other);
        if (between is null or { Kind: RelationKind.Friends or RelationKind.Other })
        {
            if (mutualClosest && Touch.Allows(Poses.HoldHands, mine) && Touch.Allows(Poses.HoldHands, theirs) && !book.HasPartner(me) && !book.HasPartner(other))
                asks.Add(AskKind.GoOut);
        }
        else if (between.IsRomantic() && mine.Warmth >= WarmEnough && theirs.Warmth >= WarmEnough)
        {
            var together = now - between.Since;
            if (!between.LiveTogether && together >= MoveInAfter.TotalMilliseconds && !book.SharesHome(me) && !book.SharesHome(other)) asks.Add(AskKind.MoveIn);
            if (between.Kind == RelationKind.Together && together >= MarryAfter.TotalMilliseconds) asks.Add(AskKind.Marry);
        }
        return asks.Where(k => book.MayAsk(me, other, k, now)).ToList();
    }

    public static bool MayBreakUp(RelationBook book, Guid me, Guid other, TieRead mine)
        => book.Partners(me, other) && mine.Warmth <= Strained;

    public static string Label(AskKind kind, string name) => kind switch
    {
        AskKind.GoOut => $"Ask {name} out",
        AskKind.MoveIn => $"Ask {name} to move in with you",
        _ => $"Ask {name} to marry you",
    };

    private static string Asking(AskKind kind) => kind switch
    {
        AskKind.GoOut => "asked you out",
        AskKind.MoveIn => "asked you to move in with them",
        _ => "asked you to marry them",
    };

    public static string Asked(RelationAsk ask, string name)
        => $"{name} {(ask.Kind == AskKind.Key ? $"asked you for a key to {ask.About}" : Asking(ask.Kind))}, and you haven't answered yet.";

    public static string Answered(RelationAsk ask, string name) => (ask.Kind, ask.Yes) switch
    {
        (AskKind.GoOut, true) => $"{name} said yes when you asked them out.",
        (AskKind.MoveIn, true) => $"{name} said yes to moving in with you.",
        (AskKind.Marry, true) => $"{name} said yes when you asked them to marry you.",
        (AskKind.Key, true) => $"{name} gave you a key to {ask.About}.",
        (AskKind.Key, _) => $"{name} said no when you asked for a key to {ask.About}.",
        (AskKind.GoOut, _) => $"{name} said no when you asked them out.",
        (AskKind.MoveIn, _) => $"{name} said no to moving in with you.",
        _ => $"{name} said no when you asked them to marry you.",
    };

    public static string Done(AskKind kind, string name, string? about = null) => kind switch
    {
        AskKind.GoOut => $"is now with {name}",
        AskKind.Key => $"gives {name} a key to {about}",
        AskKind.MoveIn => $"is moving in with {name}",
        _ => $"is marrying {name}",
    };

    public static string Line(Relation r)
    {
        var line = r.Story ?? (r.Kind, r.IsCurrent()) switch
        {
            (RelationKind.Together, true) => "you're together",
            (RelationKind.Together, false) => "you used to be together",
            (RelationKind.Married, true) => "you're married",
            (RelationKind.Married, false) => "you used to be married",
            (RelationKind.Family, _) => "you're family",
            (RelationKind.Friends, true) => "you're close friends",
            (RelationKind.Friends, false) => "you used to be close friends",
            _ => "you have history",
        };
        return r.LiveTogether ? $"{line}; you live together" : line;
    }

    public static string? Known(Relation r, string name) => !r.IsCurrent() ? null : r.Kind switch
    {
        RelationKind.Together => $"with {name}",
        RelationKind.Married => $"married to {name}",
        RelationKind.Family => $"family of {name}",
        RelationKind.Friends => $"close friends with {name}",
        _ => null,
    };

    public static string? Standing(RelationBook book, Guid viewer, Guid other)
    {
        var lines = book.Relations.Where(r => r.Involves(viewer) && r.Involves(other))
            .OrderByDescending(r => r.IsCurrent()).ThenByDescending(r => r.EndedAt)
            .Select(Line).Distinct().Take(MaxLines).ToList();
        return lines.Count == 0 ? null : string.Join("; ", lines);
    }

    public static Acquaintance Seen(RelationBook book, Guid viewer, Guid other, string name, Tie? tie, Func<Guid, string?> firstNameOf, bool present = true)
    {
        var aware = present || tie?.Exchanges >= KnownAfter;
        var known = book.Relations
            .Where(r => aware && r.Involves(other) && !r.Involves(viewer))
            .Select(r => firstNameOf(r.Other(other)) is { } partner ? Known(r, partner) : null)
            .OfType<string>().ToList();
        return new Acquaintance(name, tie, Standing(book, viewer, other), book.CurrentBetween(viewer, other) is not null,
            known.Count == 0 ? null : string.Join("; ", known));
    }
}
