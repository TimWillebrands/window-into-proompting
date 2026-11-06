using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

/// <summary>Work that leaves something behind: a Piece, finished at an easel, hangs in the Studio in its maker's words.</summary>
public static class Art
{
    public const string Studio = "studio";
    public const string Wall = "wall";
    public const int PerDay = 2;
    public const int MaxOnShow = 12;

    private static readonly Regex LeadIn = new(
        @"^(i('ll| will|'m going to| am going to| want to| think i'll)\s+|let me\s+|time to\s+|going to\s+)?(finally\s+|just\s+|try to\s+)*(paint|draw|sketch|sculpt|carve|make|finish|start on|start|work on|get on with|continue)\s+",
        RegexOptions.IgnoreCase);

    public static Furni? EaselIn(Place? place) => place?.Furni.FirstOrDefault(f => f.Kind == FurniKinds.Easel);

    /// <summary>There's an easel here and they haven't used up today's allowance.</summary>
    public static bool CanMake(Place? place, Guid who, long now, long localMidnight)
        => EaselIn(place) is not null && Decor.PlacedToday(place!.Furni.Where(f => f.OwnerId == who), now, localMidnight) < PerDay;

    /// <summary>"I'll paint a charcoal study of the canal. It's for Lena." → "a charcoal study of the canal".</summary>
    public static string Title(string intent)
    {
        var sentence = intent.Trim().Split(new[] { '.', '!', '?', ';', ':', '—', '\n' }, 2)[0];
        return Decor.Label(LeadIn.Replace(sentence, string.Empty));
    }
}
