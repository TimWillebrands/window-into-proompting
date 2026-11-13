using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Services.SystemOne;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.Bench.Probes;

/// <summary>
/// ADR 0057: does the fit come out of the agent–arena relation, not the room alone? Opposite tastes,
/// the same three rooms; the judge's p(it lifts them) should rank the rooms differently per persona.
/// </summary>
public static class ArenaFitProbes
{
    private const int Samples = 3;

    private static readonly (string Name, string Identity)[] People =
    {
        ("Justin", "You are Justin. You care about craft in any field and judge a thing by whether its structure serves its purpose. You have strong views on music and on how a room should be arranged. You play guitar and synths and record anything with a microphone."),
        ("Brother Anselm", "You are Brother Anselm. You left a monastery after twenty years and still own almost nothing. Clutter makes you anxious; an empty room with one chair is where you think best. You distrust comfort that has to be bought."),
    };

    private static readonly (string Name, string Situation)[] Rooms =
    {
        ("bare", "You're at the flat, sitting on the floor. Cold light. Bare, apart from a mattress and a chair. Nobody else is here."),
        ("furnished", "You're at the flat, sitting on the sofa. Warm light, dust hanging in the air. In it: record crates, a synth on a stand, a big plant, a rug and shelves of books, and more. Nobody else is here."),
        ("neglected", "You're at the flat, sitting on the sofa. Dim light. In it: a sofa, a desk and a lamp, and more. Nobody else is here. Around you: the plant on the windowsill is wilting; dirty dishes are piling up."),
    };

    [Probe("Arena fit (ADR 0057): 2 personas with opposite tastes × bare/furnished/neglected rooms through the real judge, 3 samples each; p(it lifts them). Needs OPENROUTER_API_KEY.")]
    public static async Task Places_ArenaFit(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);

        var table = new Dictionary<string, Dictionary<string, double>>();
        foreach (var (name, identity) in People)
        {
            table[name] = new Dictionary<string, double>();
            foreach (var (room, situation) in Rooms)
            {
                var ps = new List<double>();
                for (var i = 0; i < Samples; i++)
                {
                    var answers = await judge.AskAsync($"{identity}\n\n{situation}",
                        new Dictionary<string, SystemOneQuestion> { [ArenaFit.Key] = ArenaFit.Question(name) }, bench.Cancellation);
                    ps.Add(Appraisal.Better(answers.Choices.GetValueOrDefault(ArenaFit.Key)));
                }
                table[name][room] = Math.Round(ps.Average(), 3);
                bench.Observe($"{name} · {room}", new { p = ps, mean = table[name][room] });
            }
        }
        var order = table.ToDictionary(kv => kv.Key, kv => string.Join(" > ", kv.Value.OrderByDescending(r => r.Value).Select(r => r.Key)));
        bench.Observe("p(it lifts them) per persona; the fit is relational when the orders differ", new { table, order, relational = order.Values.Distinct().Count() > 1 });
    }
}
