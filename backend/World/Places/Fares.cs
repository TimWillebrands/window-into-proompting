using PartyTown.Services.SystemOne;

namespace PartyTown.World.Places;

public static class Fares
{
    public const string Key = "fare";
    public const string None = "none";
    public const string Meal = "meal";
    public const string Light = "light";
    public const string Snack = "snack";
    public const double TakenAt = 0.5;
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    private static readonly IReadOnlyDictionary<string, string?> Options = new Dictionary<string, string?>
    {
        [Meal] = "yes, proper meals",
        [Light] = "yes, coffee and something light to eat",
        [Snack] = "yes, drinks and snacks",
        [None] = "no, nothing is sold to eat or drink (coffee only for the people who work there doesn't count)",
    };

    public static double? Amount(string? fare) => fare switch
    {
        Meal => Self.Bio.Meal,
        Light => Self.Bio.LightMeal,
        Snack => Self.Bio.Snack,
        _ => null,
    };

    public static bool Known(string? fare) => fare is None or Meal or Light or Snack;

    public static string? OfKit(Place place) => place.Kind != PlaceKinds.Public ? null : (place.Archetype ?? (place.Scene == "cafe" ? "cafe" : null)) switch
    {
        null or "place" => null,
        "restaurant" => Meal,
        "cafe" => Light,
        "bar" => Snack,
        _ => None,
    };

    public static bool Judged(Place place) => place is { Kind: PlaceKinds.Public, Private: false } && OfKit(place) is null;

    public static string For(Place place) => $"2\n{place.Name}\n{place.Purpose}";

    public static bool Due(Place place)
        => OfKit(place) is { } kit ? place.Fare != kit || place.FareFor != For(place)
            : Judged(place) && (!Known(place.Fare) || place.FareFor != For(place));

    public static string State(Place place)
    {
        var lines = new List<string> { $"A place in town: {place.Name}{(string.IsNullOrWhiteSpace(place.Purpose) ? string.Empty : $", {place.Purpose.Trim().TrimEnd('.')}")}." };
        if (!string.IsNullOrWhiteSpace(place.Description) && place.Description != place.Name) lines.Add($"What it's like: {place.Description.Trim().TrimEnd('.')}.");
        if ((place.Furni.Count > 0 || place.CustomScene is not null) && ArenaFit.Feel(place) is { } feel) lines.Add(feel);
        return string.Join("\n", lines);
    }

    public static Dictionary<string, SystemOneQuestion> Question(Place place) => new()
    {
        [Key] = new ChoiceQuestion($"Can anyone who comes in buy something to eat or drink at {place.Name}?", Options),
    };

    public static (string Fare, double P)? Read(SystemOneAnswers answers)
    {
        if (!answers.Choices.TryGetValue(Key, out var probs) || probs.Count == 0) return null;
        var no = probs.GetValueOrDefault(None);
        var sold = probs.Where(kv => kv.Key is Meal or Light or Snack).ToList();
        var yes = sold.Sum(kv => kv.Value);
        return sold.Count > 0 && yes >= TakenAt && yes > no ? (sold.MaxBy(kv => kv.Value).Key, yes) : (None, no);
    }
}
