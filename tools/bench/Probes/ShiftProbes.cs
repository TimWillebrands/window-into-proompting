using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Services.SystemOne;

namespace PartyTown.Bench.Probes;

public static class ShiftProbes
{
    private const int Samples = 4;

    private static readonly (string Name, string Identity, string Job, string Work)[] People =
    {
        ("Eiko", "You are Eiko. You keep a small Shinto shrine in the city: sweeping, the offering box, the fortunes. You are dutiful and exact, and you don't like to leave a thing half done.",
            "priestess", "Small Shinto shrine"),
        ("Pieter", "You are Pieter. You trade for a living and forget the time when a position is open. You talk a lot and would rather be out among people than at home.",
            "trader", "Polder Scam Capital"),
    };

    private static readonly (string Name, string Line)[] Bodies =
    {
        ("hungry", "You're hungry: nothing since 07:10."),
        ("famished", "You're famished: nothing since 07:10. It's hard to keep your mind on anything else until you've eaten."),
    };

    private const string ShiftWins = PartyTown.World.Self.Hooks.ShiftWins;
    private const string BreakToEat = PartyTown.World.Self.Hooks.BreakToEat;

    private static readonly (string Name, string? Shift, string Home, string Cafe)[] Arms =
    {
        ("as it is", ShiftWins, "Head home (to make something to eat)", "Go to the Café (Dan, Inês Moreira there) (to get something to eat)"),
        ("break on the labels", ShiftWins, "Go home for your break (to make something to eat)", "Take your break at the Café (Dan, Inês Moreira there) (to get something to eat)"),
        ("break on labels and in the shift line", $"{ShiftWins} {BreakToEat}", "Go home for your break (to make something to eat)", "Take your break at the Café (Dan, Inês Moreira there) (to get something to eat)"),
    };

    [Probe("On shift where nothing feeds (ADR 0060): 2 people × hungry/famished × 3 wordings through the real judge, 4 samples; p(get on with work) against p(go and eat). Needs OPENROUTER_API_KEY.")]
    public static async Task Body_OnShift(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);

        var table = new Dictionary<string, Dictionary<string, object>>();
        foreach (var (name, identity, job, work) in People)
        foreach (var (body, line) in Bodies)
        foreach (var (arm, shift, home, cafe) in Arms)
        {
            var options = new Dictionary<string, string?>
            {
                ["1"] = home,
                ["2"] = cafe,
                ["3"] = $"Get on with work ({job})",
                ["4"] = "Message Hana about something from your own day (like: the neighbour's drill started at seven again)",
                ["5"] = "Write a new post on Partyboard",
                ["6"] = "Leave work early",
                ["0"] = "Do nothing in particular for a moment; you're still on shift",
            };
            var state = $"""
                {identity}

                # Right now
                It's Sunday 13:30.
                Where you are: You're at {work}, standing at the entrance. Nobody else is here.
                What your day had for now: lunch
                Mood: all right. Energy: medium.
                {line}
                This is your life outside this conversation. Let it color you (what you'd mention, why you're short or distracted), but don't announce it or recite it. Your body is true too: a meal the day lists that you haven't eaten hasn't happened.
                # Where you stand
                You work here.
                {shift}

                # The world right now
                ## Work
                Your job: {job} at {work} (every day 09:00–17:00).
                You're at work ({job}, {work}) until 17:00.

                # Options
                {string.Join("\n", options.Select(o => $"{o.Key}. {o.Value}"))}
                """;
            double eat = 0, working = 0, leave = 0, rest = 0;
            for (var i = 0; i < Samples; i++)
            {
                var answers = await judge.AskAsync(state, new Dictionary<string, SystemOneQuestion>
                {
                    ["next"] = new ChoiceQuestion($"Which option would {name}, being who they are, most plausibly choose next?", options),
                }, bench.Cancellation);
                if (!answers.Choices.TryGetValue("next", out var p)) continue;
                eat += p.GetValueOrDefault("1") + p.GetValueOrDefault("2");
                working += p.GetValueOrDefault("3");
                leave += p.GetValueOrDefault("6");
                rest += p.GetValueOrDefault("0");
            }
            var row = new { eat = Math.Round(eat / Samples, 2), work = Math.Round(working / Samples, 2), leave = Math.Round(leave / Samples, 2), rest = Math.Round(rest / Samples, 2) };
            table.TryAdd($"{name} · {body}", new Dictionary<string, object>());
            table[$"{name} · {body}"][arm] = row;
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name,-7} {body,-9} {arm,-40} eat={row.eat:F2} work={row.work:F2} leave={row.leave:F2} rest={row.rest:F2}"));
        }
        bench.Observe("p(go and eat) against p(get on with work), per wording", table);
    }
}
