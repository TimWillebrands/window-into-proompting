using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PartyTown.World.Skills;

/// <summary>Something a Persona knows how to do (ADR 0054): a name, the how a practised hand has, and what brings it to mind.</summary>
[GenerateSerializer, Alias("PartyTown.World.Skills.Skill")]
public sealed class Skill
{
    [Id(0)] public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>A short gerund phrase: "pulling espresso".</summary>
    [Id(1)] public string Name { get; set; } = string.Empty;
    /// <summary>1–3 sentences of judgement and steps, second person; only shown where it's used.</summary>
    [Id(2)] public string How { get; set; } = string.Empty;
    /// <summary>Lowercase words a moment has to touch for it to come to mind.</summary>
    [Id(3)] public List<string> Cues { get; set; } = new();
    [Id(4)] public SkillSource Source { get; set; }
    /// <summary>Times done; the level comes from this.</summary>
    [Id(5)] public int Practice { get; set; }
    [Id(6)] public long Since { get; set; }
    [Id(7)] public long PractisedAt { get; set; }
    /// <summary>The level its how was written for; crossing the next one rewrites it.</summary>
    [Id(8)] public SkillLevel WrittenFor { get; set; }
}

[GenerateSerializer]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SkillSource>))]
public enum SkillSource
{
    Authored = 0,
    Sheet = 1,
    /// <summary>Shifts worked, Sessions run.</summary>
    Work = 2,
    Feature = 3,
    /// <summary>A want to get better at something, come true.</summary>
    Desire = 4,
}

[GenerateSerializer]
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<SkillLevel>))]
public enum SkillLevel
{
    New = 0,
    Learning = 1,
    Practised = 2,
    SecondNature = 3,
}

/// <summary>One time doing something that teaches: what it is (the key), in plain words, and how many times it counts for.</summary>
[GenerateSerializer, Alias("PartyTown.World.Skills.SkillPractice")]
public sealed record SkillPractice(
    [property: Id(0)] string Key,
    [property: Id(1)] string What,
    [property: Id(2)] SkillSource Source,
    [property: Id(3)] int Times = 1);

/// <summary>A Skill as the API shows it: level worked out, intrinsic or not.</summary>
[GenerateSerializer, Alias("PartyTown.World.Skills.SkillView")]
public sealed record SkillView(
    [property: Id(0)] Guid Id,
    [property: Id(1)] string Name,
    [property: Id(2)] string How,
    [property: Id(3)] List<string> Cues,
    [property: Id(4)] SkillSource Source,
    [property: Id(5)] SkillLevel Level,
    [property: Id(6)] bool Rusty,
    [property: Id(7)] int Practice,
    [property: Id(8)] long PractisedAt,
    [property: Id(9)] bool Intrinsic);

/// <summary>Pure Skill rules (ADR 0054): levels from practice, rust, keys, matching a moment, how it reads.</summary>
public static partial class SkillRules
{
    /// <summary>The time doing the same thing makes the World look for know-how in it.</summary>
    public const int Learns = 3;
    public const int MaxAcquired = 12;
    public const int MaxCues = 8;
    public static readonly TimeSpan RustsAfter = TimeSpan.FromDays(30);
    /// <summary>Before asking again about a key whose look failed.</summary>
    public static readonly TimeSpan AskAgain = TimeSpan.FromHours(1);

    public static SkillLevel Level(int practice) => practice switch
    {
        < Learns => SkillLevel.New,
        < 10 => SkillLevel.Learning,
        < 30 => SkillLevel.Practised,
        _ => SkillLevel.SecondNature,
    };

    /// <summary>Who you are is second nature; what you learned here is as good as you've practised it.</summary>
    public static SkillLevel LevelOf(Skill s, bool intrinsic) => intrinsic ? SkillLevel.SecondNature : Level(s.Practice);

    public static bool Rusty(Skill s, bool intrinsic, DateTimeOffset now)
        => !intrinsic && now.ToUnixTimeMilliseconds() - Math.Max(s.PractisedAt, s.Since) > RustsAfter.TotalMilliseconds;

    public static string Describe(SkillLevel level, bool rusty = false) => rusty ? "rusty" : level switch
    {
        SkillLevel.New => "new to it",
        SkillLevel.Learning => "getting the hang of it",
        SkillLevel.Practised => "practised",
        _ => "second nature",
    };

    /// <summary>What someone at this level actually knows, for whoever writes the how.</summary>
    public static string Depth(SkillLevel level) => level switch
    {
        SkillLevel.New => "they know the first steps and little else; they'd make the beginner's mistakes",
        SkillLevel.Learning => "the basics work now; they still think about each step and get caught out now and then",
        SkillLevel.Practised => "they do it without thinking about the steps, notice when something's off and know the usual fixes",
        _ => "they read the situation at a glance, cut corners safely and know the rare cases",
    };

    public static SkillView View(Skill s, bool intrinsic, DateTimeOffset now)
        => new(s.Id, s.Name, s.How, s.Cues.ToList(), s.Source, LevelOf(s, intrinsic), Rusty(s, intrinsic, now), s.Practice, s.PractisedAt, intrinsic);

    /// <summary>"Look through the telescope." → "feature:look through the telescope": the same thing anywhere is one key.</summary>
    public static string Key(string kind, string what)
        => $"{kind}:{WorldStream.Clip(Spaces().Replace(what.Trim().TrimEnd('.', '!', '?').ToLowerInvariant(), " "), 80)}";

    /// <summary>The Skill a moment touches most, if any: content words against cues and name. No LLM.</summary>
    public static SkillView? Match(IEnumerable<SkillView> skills, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var words = Self.VoiceWear.ContentWords(text).Select(Stem).ToHashSet();
        if (words.Count == 0) return null;
        return skills
            .Select(s => (Skill: s, Hits: Words(s).Count(words.Contains)))
            .Where(x => x.Hits > 0)
            .OrderByDescending(x => x.Hits)
            .ThenBy(x => x.Skill.Rusty)
            .ThenByDescending(x => x.Skill.Level)
            .Select(x => x.Skill)
            .FirstOrDefault();
    }

    /// <summary>Cues and the name's own words, stemmed; generic words never count.</summary>
    private static HashSet<string> Words(SkillView s)
        => s.Cues.SelectMany(c => Self.VoiceWear.ContentWords(c)).Concat(Self.VoiceWear.ContentWords(s.Name)).Select(Stem).ToHashSet();

    /// <summary>"bikes", "biking", "bike" → "bik": enough to match a cue, no dictionary.</summary>
    public static string Stem(string word)
    {
        var w = word.ToLowerInvariant().Trim('\'', '’');
        foreach (var suffix in new[] { "ing", "ed", "es", "s" })
            if (w.EndsWith(suffix, StringComparison.Ordinal) && w.Length - suffix.Length >= 3)
            {
                w = w[..^suffix.Length];
                break;
            }
        return w.Length > 3 && w.EndsWith('e') ? w[..^1] : w;
    }

    /// <summary>An option as the chooser reads it: one that exercises a Skill says so.</summary>
    public static string Toward(string label, SkillView? skill)
        => skill is null ? label : $"{label} (you know how: {skill.Name}{(skill.Rusty ? ", a bit rusty" : string.Empty)})";

    /// <summary>The one Skill in use, as a block; empty when there's none.</summary>
    public static string Render(SkillView? skill)
    {
        if (skill is null || string.IsNullOrWhiteSpace(skill.How)) return string.Empty;
        return $"""
            # What you know how to do
            - {Capital(skill.Name)} ({Describe(skill.Level, skill.Rusty)}): {skill.How.Trim()}
            Know-how, not a topic: it shows in what you notice and do when it's useful here. Never bring it up for its own sake or explain it unasked.

            """;
    }

    /// <summary>A written Skill from JSON, or null when there's nothing usable.</summary>
    public static Skill? Parse(JsonNode? json, SkillSource source, long now, int practice = 0)
    {
        var name = Text(json?["name"]);
        var how = Text(json?["how"]);
        if (name is null || how is null || name.Length < 3 || how.Length < 12 || name.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;
        return new Skill
        {
            Name = WorldStream.Clip(name.TrimEnd('.'), 60), How = WorldStream.Clip(how, 400), Cues = Cues(json?["cues"]),
            Source = source, Practice = practice, Since = now, PractisedAt = now, WrittenFor = Level(practice),
        };
    }

    /// <summary>Cues as an array or a comma list; lowercase single words, deduplicated.</summary>
    public static List<string> Cues(JsonNode? node)
    {
        IEnumerable<string> raw = node switch
        {
            JsonArray a => a.Select(x => x?.ToString() ?? string.Empty),
            JsonValue v => (v.ToString() ?? string.Empty).Split(',', ';'),
            _ => Array.Empty<string>(),
        };
        return raw.SelectMany(c => Self.VoiceWear.ContentWords(c)).Distinct().Take(MaxCues).ToList();
    }

    private static string? Text(JsonNode? node)
        => node is JsonValue v && v.ToString() is { } t && !string.IsNullOrWhiteSpace(t) ? t.Trim().Trim('"') : null;

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

/// <summary>Where doing something teaches (ADR 0054): one call per practice site, never failing what was done.</summary>
public static class Practising
{
    public static async Task NoteAsync(IGrainFactory grains, Guid partyId, Guid who, SkillPractice practice, ILogger? logger = null)
    {
        try { await grains.GetGrain<ISkillsGrain>(WorldKeys.Life(partyId, who)).PractiseAsync(practice); }
        catch (Exception ex) { logger?.LogDebug(ex, "Practice {Key} not counted for {Who}", practice.Key, who); }
    }

    /// <summary>A shift worked or a Session run: the same craft, keyed by the job.</summary>
    public static SkillPractice Work(string title, string what) => new(SkillRules.Key("work", title), what, SkillSource.Work);

    public static SkillPractice Feature(string what, string summary) => new(SkillRules.Key("feature", what), summary, SkillSource.Feature);

    /// <summary>A want to get better at something, come true: it counts as having done it enough to know.</summary>
    public static SkillPractice Desire(Guid desireId, string text) => new($"desire:{desireId:N}", $"got where they wanted: {text}", SkillSource.Desire, SkillRules.Learns);
}
