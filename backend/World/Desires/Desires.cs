using PartyTown.World.Economy;

namespace PartyTown.World.Desires;

[GenerateSerializer, Alias("PartyTown.World.Desires.Desire")]
public sealed class Desire
{
    [Id(0)] public Guid Id { get; set; } = Guid.NewGuid();
    [Id(1)] public DesireKind Kind { get; set; }
    [Id(2)] public string Text { get; set; } = string.Empty;
    [Id(3)] public string? ItemKey { get; set; }
    [Id(4)] public Guid? TargetId { get; set; }
    [Id(5)] public Guid? FromId { get; set; }
    [Id(6)] public DesireSource Source { get; set; }
    [Id(7)] public long Since { get; set; }
    [Id(8)] public long TouchedAt { get; set; }
    [Id(9)] public DesireStatus Status { get; set; }
    [Id(10)] public Guid? PlaceId { get; set; }
    [Id(11)] public long? DueAt { get; set; }
    [Id(12)] public long? Until { get; set; }
    [Id(13)] public bool Coming { get; set; }
    [Id(14)] public int Again { get; set; }
    [Id(15)] public long? SettlesAt { get; set; }
}

[GenerateSerializer]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DesireKind>))]
public enum DesireKind
{
    Having = 0,
    Being = 1,
}

[GenerateSerializer]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DesireSource>))]
public enum DesireSource
{
    Authored = 0,
    Sheet = 1,
    Play = 2,
    Mimesis = 3,
    Mission = 4,
    Promise = 5,
}

[GenerateSerializer]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DesireStatus>))]
public enum DesireStatus
{
    Active = 0,
    Fulfilled = 1,
    Faded = 2,
    Abandoned = 3,
}

public static class DesireRules
{
    public const int MaxLines = 2;
    public static readonly TimeSpan FadesAfter = TimeSpan.FromDays(14);
    public static readonly TimeSpan ReviewEvery = TimeSpan.FromHours(12);

    public static bool Fades(Desire d, bool intrinsic, DateTimeOffset now)
        => d.Status == DesireStatus.Active && (Lapsed(d, now.ToUnixTimeMilliseconds())
            || !intrinsic && now.ToUnixTimeMilliseconds() - Math.Max(d.TouchedAt, d.Since) > FadesAfter.TotalMilliseconds);

    public static bool Lapsed(Desire d, long now) => d.Until is { } until && now >= until;

    public static bool IsPromise(Desire d) => d.Source == DesireSource.Promise;

    public static bool Pressing(Desire d, long now)
        => d.Status == DesireStatus.Active && d.DueAt is { } due && now >= due && !Lapsed(d, now);

    public static List<Desire> OnMind(IEnumerable<Desire> desires)
        => desires.Where(d => d.Status == DesireStatus.Active && !IsPromise(d))
            .OrderByDescending(d => Math.Max(d.TouchedAt, d.Since))
            .Take(MaxLines)
            .ToList();

    public static string Render(IEnumerable<Desire> desires)
    {
        var lines = OnMind(desires).Select(d => $"- {Sentence(d.Text)}").ToList();
        return lines.Count == 0 ? string.Empty : $"# What you want lately\n{string.Join("\n", lines)}\n";
    }

    public static Dictionary<Guid, Desire> People(IEnumerable<Desire> desires, IEnumerable<(Guid Id, string Name)> cast,
        long? now = null, Func<Guid, Guid?>? chatWith = null, Func<Guid, Guid?>? placeOf = null)
    {
        var people = new Dictionary<Guid, Desire>();
        var all = desires.ToList();
        var at = now ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var d in all.Where(d => IsPromise(d) && Pressing(d, at) && d.TargetId is not null))
        {
            var target = d.TargetId!.Value;
            people.TryAdd(target, d);
            if (chatWith?.Invoke(target) is { } chat) people.TryAdd(chat, d);
            if (d.Coming && placeOf?.Invoke(target) is { } place) people.TryAdd(place, d);
        }
        var named = cast.ToList();
        foreach (var d in OnMind(all))
        {
            if (d.TargetId is { } target) people.TryAdd(target, d);
            foreach (var (id, name) in named)
                if (Names(name).Any(n => System.Text.RegularExpressions.Regex.IsMatch(d.Text, $@"\b{System.Text.RegularExpressions.Regex.Escape(n)}\b")))
                    people.TryAdd(id, d);
        }
        return people;
    }

    private static IEnumerable<string> Names(string name)
    {
        yield return name;
        var first = name.Split(' ')[0];
        if (first.Length >= 3 && first != name) yield return first;
    }

    public static string Toward(string label, Desire? about) => about switch
    {
        null => label,
        { Source: DesireSource.Promise } => $"{label} (you said you would: {WorldStream.Clip(about.Text, 80)})",
        _ => $"{label} (toward what you want: {WorldStream.Clip(about.Text, 80)})",
    };

    public static Desire? Shopping(IEnumerable<Desire> desires, IReadOnlyCollection<string> owned)
        => OnMind(desires).FirstOrDefault(d => d.Kind == DesireKind.Having && d.ItemKey is { } key && !owned.Contains(key));

    public static Desire? Parse(string? text, DesireKind kind, DesireSource source, DateTimeOffset now)
    {
        text = text?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase) || text.Length < 4) return null;
        return new Desire
        {
            Kind = kind, Text = WorldStream.Clip(text, 160), Source = source, Since = now.ToUnixTimeMilliseconds(),
            ItemKey = kind == DesireKind.Having ? Catalog.Match(text, Catalog.All)?.Key : null,
        };
    }

    private static string Sentence(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return t;
        t = char.ToUpperInvariant(t[0]) + t[1..];
        return t.EndsWith('.') || t.EndsWith('!') || t.EndsWith('?') ? t : $"{t}.";
    }
}
