using System.Text.Json;
using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

/// <summary>An archetype a Locale opens as (ADR 0027): spots, purpose and a description, chosen not designed.</summary>
public sealed record Kit(string Archetype, string Purpose, string Description, string At, string[] Keywords, Spot[] Spots);

/// <summary>The kits in kits.json (shared with the client, whose kit scenes pin these spots; a frontend test checks).</summary>
public static class Kits
{
    private static readonly Lazy<Kit[]> Loaded = new(() =>
    {
        using var s = typeof(Kits).Assembly.GetManifestResourceStream("PartyTown.World.Places.kits.json")!;
        return JsonSerializer.Deserialize<Kit[]>(s, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    });

    /// <summary>Every kit but the plain fallback.</summary>
    public static Kit[] All => Loaded.Value.Where(k => k.Archetype != "place").ToArray();

    public static Kit Place => Loaded.Value.Single(k => k.Archetype == "place");

    public static Kit Of(string? archetype) => All.FirstOrDefault(k => k.Archetype == archetype) ?? Place;

    /// <summary>The archetype a name says plainly ("the climbing gym" → gym); null when it doesn't.</summary>
    public static string? FromName(string name)
    {
        var n = $" {Regex.Replace(name.ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim()} ";
        return All.FirstOrDefault(k => k.Keywords.Any(w => n.Contains($" {w} ", StringComparison.Ordinal)))?.Archetype;
    }

    /// <summary>Fresh spots from a kit, poses filled.</summary>
    public static List<Spot> SpotsOf(Kit kit)
    {
        var spots = kit.Spots.Select(s => new Spot { Id = s.Id, Name = s.Name, Capacity = s.Capacity, At = s.At, Seat = s.Seat, Staff = s.Staff }).ToList();
        Poses.Fill(spots);
        return spots;
    }
}
