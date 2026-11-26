using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Grains.Generation;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Self;
using PartyTown.World.Work.Cdu;

namespace PartyTown.Bench.Probes;

public static partial class WorkProbes
{
    [Probe("CDU frame loop, the pick: the same desk as Cdu_FrameLoop through the real judge. p(ask), p(hand work) and the rest of the menu as it is, with one remembered line saying what the asks added up to, and with the notes of one look back in the state; 4 samples each, two different notes. Needs OPENROUTER_API_KEY.")]
    public static async Task Cdu_FrameLoopPick(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);
        var llm = new WorldLlm(bench.Grains, bench.LoggerFactory.CreateLogger<WorldLlm>());
        var scene = LoopScene();

        var options = new Dictionary<string, string?>
        {
            ["1"] = "Ask the CDU how something in this world is built, or what happened",
            ["2"] = "Hand the CDU a piece of work: a brief for one change to this world",
            ["3"] = "Say something to Justin Ruysch, who's here",
            ["4"] = "Write a new post on Partyboard",
            ["5"] = "Go to the Café",
            ["0"] = Hooks.RestOnShift,
        };
        var menu = "# Options\n" + string.Join("\n", options.Select(o => $"{o.Key}. {o.Value}"));

        var notes = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var written = await llm.CompleteAsync(JobComplexity.General,
                $"{DeniseIs}\n\n{CduScreen.Calling(Works)}\nYou're at your desk, between two things, and nobody is waiting on you. Before you do anything else, look back over what you and Justin asked the CDU this shift and what came back: all of it together, not one at a time. Think it through for yourself, in your own words. What did it add up to? What do you know now that you didn't this morning? Is the way you've been going about it getting you anywhere, and if it isn't, what would? These are notes to yourself and nobody else reads them.",
                $"{scene.Shift}\n\n# On your desk\n{scene.Rendered}", bench.Cancellation, 0.7);
            if (!string.IsNullOrWhiteSpace(written)) notes.Add(WorldStream.Clip(written.Trim(), 1500));
        }

        var arms = new List<(string Name, string State)>
        {
            ("as it is", $"{DeniseIs}\n\n{scene.World(false)}\n{menu}"),
            ("one remembered line", $"{DeniseIs}\n\n{scene.World(true)}\n{menu}"),
        };
        for (var i = 0; i < notes.Count; i++)
            arms.Add(($"notes of a look back ({i + 1})", $"{DeniseIs}\n\n{scene.World(false)}\n# What you worked out at your desk a moment ago\n{notes[i]}\n\n{menu}"));

        var table = new Dictionary<string, object>();
        foreach (var (name, state) in arms)
        {
            var sum = options.Keys.ToDictionary(k => k, _ => 0.0);
            var answered = 0;
            for (var i = 0; i < 4; i++)
            {
                var answers = await judge.AskAsync(state, new Dictionary<string, SystemOneQuestion>
                {
                    ["next"] = new ChoiceQuestion("Which option would Denise, being who they are, most plausibly choose next?", options),
                }, bench.Cancellation);
                if (!answers.Choices.TryGetValue("next", out var p)) continue;
                answered++;
                foreach (var k in options.Keys) sum[k] += p.GetValueOrDefault(k);
            }
            table[name] = new
            {
                answered,
                ask = Math.Round(sum["1"] / Math.Max(1, answered), 3),
                handWork = Math.Round(sum["2"] / Math.Max(1, answered), 3),
                justin = Math.Round(sum["3"] / Math.Max(1, answered), 3),
                board = Math.Round(sum["4"] / Math.Max(1, answered), 3),
                cafe = Math.Round(sum["5"] / Math.Max(1, answered), 3),
                rest = Math.Round(sum["0"] / Math.Max(1, answered), 3),
            };
        }
        bench.Observe("the notes that were in the state", notes);
        bench.Observe("p(next), mean of 4", table);
    }
}
