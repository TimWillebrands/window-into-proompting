using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Services.SystemOne;
using PartyTown.World.Places;

namespace PartyTown.Bench.Probes;

public static class FareProbes
{
    private static readonly (string Name, string Purpose, string Expected)[] Places =
    {
        ("the Café", "where people get coffee and linger", Fares.Light),
        ("the Lobby", "where people come in, wait for someone and check the noticeboard", Fares.None),
        ("the city centre", "where people pass through, sit by the water and run into each other", Fares.None),
        ("the Tuin", "where people go for a drink outside and unwind", Fares.Snack),
        ("the Sterrenwacht", "where people come up to look at the sky and write down what they saw", Fares.None),
        ("The Works", "where people work at desks and grab coffee between meetings", Fares.None),
        ("Small Shinto shrine", "where people walk, sit in the grass and get some air", Fares.None),
        ("her studio in an old shipyard building in Noord", "where art gets made", Fares.None),
        ("the reading room", "where people read quietly; no food or drinks", Fares.None),
        ("the lawn", "where office workers eat their lunch on the grass", Fares.None),
        ("the allotments", "where people grow their own food", Fares.None),
        ("the Bar Association", "where lawyers meet", Fares.None),
        ("Tante Sjaan", "a ramen counter under the tracks", Fares.Meal),
        ("het Wafelhuis", "where people queue for stroopwafels", $"{Fares.Light}/{Fares.Snack}"),
    };

    [Probe("What a Place sells (ADR 0062): the fare question on 14 name/purpose pairs through the real judge; the judged fare and its probabilities. Needs OPENROUTER_API_KEY.")]
    public static async Task Places_Fare(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);

        var options = new[] { Fares.Meal, Fares.Light, Fares.Snack, Fares.None };
        Console.WriteLine($"| place | expected | judged | {string.Join(" | ", options)} |");
        Console.WriteLine($"| --- | --- | --- |{string.Concat(options.Select(_ => " --- |"))}");
        var right = 0;
        foreach (var (name, purpose, expected) in Places)
        {
            var place = new Place { Id = Guid.NewGuid(), Name = name, Kind = PlaceKinds.Public, Purpose = purpose };
            var answers = await judge.AskAsync(Fares.State(place), Fares.Question(place), bench.Cancellation);
            var probs = answers.Choices.GetValueOrDefault(Fares.Key) ?? new Dictionary<string, double>();
            var judged = Fares.Read(answers)?.Fare ?? "(no view)";
            var ok = expected.Split('/').Contains(judged);
            if (ok) right++;
            Console.WriteLine($"| {name} | {expected} | {judged}{(ok ? string.Empty : " ✗")} | {string.Join(" | ", options.Select(o => probs.GetValueOrDefault(o).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))} |");
            bench.Observe(name, new { purpose, expected, judged, probs });
        }
        Console.WriteLine($"{right}/{Places.Length} as expected");
        bench.Observe("fare", new { right, of = Places.Length });
    }
}
