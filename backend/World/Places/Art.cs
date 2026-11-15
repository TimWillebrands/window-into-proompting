using System.Text.RegularExpressions;

namespace PartyTown.World.Places;

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

    public static bool CanMake(Place? place, Guid who, long now, long localMidnight)
        => EaselIn(place) is not null && Decor.PlacedToday(place!.Furni.Where(f => f.OwnerId == who), now, localMidnight) < PerDay;

    public static string Title(string intent)
    {
        var sentence = intent.Trim().Split(new[] { '.', '!', '?', ';', ':', '—', '\n' }, 2)[0];
        return Decor.Label(LeadIn.Replace(sentence, string.Empty));
    }
}
