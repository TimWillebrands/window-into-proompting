using PartyTown.World.Economy;

namespace PartyTown.World.Desires;

/// <summary>Something a Persona wants (ADR 0026): to have something, or to become someone.</summary>
[GenerateSerializer, Alias("PartyTown.World.Desires.Desire")]
public sealed class Desire
{
    [Id(0)] public Guid Id { get; set; } = Guid.NewGuid();
    [Id(1)] public DesireKind Kind { get; set; }
    [Id(2)] public string Text { get; set; } = string.Empty;
    /// <summary>A catalog item a having-desire names, so the Wardrobe can foreground it.</summary>
    [Id(3)] public string? ItemKey { get; set; }
    /// <summary>Whose attention or tie it's about, when it's about someone.</summary>
    [Id(4)] public Guid? TargetId { get; set; }
    /// <summary>Mimesis: who it was caught from.</summary>
    [Id(5)] public Guid? FromId { get; set; }
    [Id(6)] public DesireSource Source { get; set; }
    [Id(7)] public long Since { get; set; }
    [Id(8)] public long TouchedAt { get; set; }
    [Id(9)] public DesireStatus Status { get; set; }
    /// <summary>The closed Place it's about, so Relations can foreground the key (ADR 0036).</summary>
    [Id(10)] public Guid? PlaceId { get; set; }
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
    /// <summary>Given by a Player to their character on auto (ADR 0037).</summary>
    Mission = 4,
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

/// <summary>Pure desire rules: fading, what's on your mind, how it reads, what the Wardrobe makes of it.</summary>
public static class DesireRules
{
    public const int MaxLines = 2;
    public static readonly TimeSpan FadesAfter = TimeSpan.FromDays(14);
    public static readonly TimeSpan ReviewEvery = TimeSpan.FromHours(12);

    /// <summary>Acquired desires nobody touched in a fortnight fade; intrinsic ones are who you are and stay.</summary>
    public static bool Fades(Desire d, bool intrinsic, DateTimeOffset now)
        => !intrinsic && d.Status == DesireStatus.Active && now.ToUnixTimeMilliseconds() - Math.Max(d.TouchedAt, d.Since) > FadesAfter.TotalMilliseconds;

    /// <summary>What's on their mind: active ones, the most recently touched first, at most two.</summary>
    public static List<Desire> OnMind(IEnumerable<Desire> desires)
        => desires.Where(d => d.Status == DesireStatus.Active)
            .OrderByDescending(d => Math.Max(d.TouchedAt, d.Since))
            .Take(MaxLines)
            .ToList();

    /// <summary>"# What you want lately" as plain lines; empty when nothing is wanted.</summary>
    public static string Render(IEnumerable<Desire> desires)
    {
        var lines = OnMind(desires).Select(d => $"- {Sentence(d.Text)}").ToList();
        return lines.Count == 0 ? string.Empty : $"# What you want lately\n{string.Join("\n", lines)}\n";
    }

    /// <summary>People a want on their mind is about (its target, or a cast name in its text) → that want's text.</summary>
    public static Dictionary<Guid, string> People(IEnumerable<Desire> desires, IEnumerable<(Guid Id, string Name)> cast)
    {
        var people = new Dictionary<Guid, string>();
        var named = cast.ToList();
        foreach (var d in OnMind(desires))
        {
            if (d.TargetId is { } target) people.TryAdd(target, d.Text);
            foreach (var (id, name) in named)
                if (Names(name).Any(n => System.Text.RegularExpressions.Regex.IsMatch(d.Text, $@"\b{System.Text.RegularExpressions.Regex.Escape(n)}\b")))
                    people.TryAdd(id, d.Text);
        }
        return people;
    }

    private static IEnumerable<string> Names(string name)
    {
        yield return name;
        var first = name.Split(' ')[0];
        if (first.Length >= 3 && first != name) yield return first;
    }

    /// <summary>An option as the chooser reads it: one about someone you want something from says so.</summary>
    public static string Toward(string label, string? want)
        => want is null ? label : $"{label} (toward what you want: {WorldStream.Clip(want, 80)})";

    /// <summary>The having-desire the shops can answer: active, naming a catalog item you don't own yet.</summary>
    public static Desire? Shopping(IEnumerable<Desire> desires, IReadOnlyCollection<string> owned)
        => OnMind(desires).FirstOrDefault(d => d.Kind == DesireKind.Having && d.ItemKey is { } key && !owned.Contains(key));

    /// <summary>A distilled desire, or null for "none"/empty. Having-desires get a catalog item when they name one.</summary>
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
