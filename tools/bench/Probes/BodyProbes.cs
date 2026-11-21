using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.SystemOne;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.Bench.Probes;

public static partial class BodyProbes
{
    private const int Samples = 3;

    private static readonly string[] Openers =
    {
        "hey, how's your day going?",
        "do you think it's worth rewriting the scheduler from scratch, or should we keep patching it?",
        "are you coming to Hana's thing on Saturday?",
    };

    private static readonly string[] Hungry = { "hungry", "starving", "famished", "eaten", "eat", "food", "lunch", "dinner", "breakfast", "sandwich", "stomach", "snack" };
    private static readonly string[] Tired = { "tired", "exhausted", "knackered", "wiped", "sleep", "nap", "bed", "lie down", "dead on my feet", "shattered", "beat" };

    [Probe("Reserves in speech (ADR 0060): Speaking with the body fine/hungry/famished/spent. 2 personas × 3 openers, 3 samples; words per reply and how many replies bring up food or being tired. A need should colour and shorten, not become the topic.")]
    public static async Task Body_SpeakingAB(Bench bench)
    {
        var ct = bench.Cancellation;
        var now = new DateTimeOffset(2026, 11, 20, 13, 20, 0, TimeSpan.FromHours(1));
        const string situation = "You're at home (a small apartment), sitting on the sofa, on your phone, texting. Nobody else is home.";
        var totals = new Dictionary<string, Spoken>();

        foreach (var self in new[] { BenchCast.Vlad, BenchCast.Denise })
        foreach (var opener in Openers)
        {
            var history = new[]
            {
                new ChatMessage { MessageId = 1, Content = opener, SenderType = "user", SenderId = BenchCast.SamId, ChatGroupId = BenchCast.RoomId },
            };
            var present = new[] { BenchCast.Sam, new ParticipantView(self.Id, self.Name, DriverKind.LLM) };
            foreach (var (body, fed, rested) in Bodies)
            {
                var presence = SelfRender.Presence(Day(fed, rested, now), 1, now, Array.Empty<string>(), situation);
                var replies = new List<string>();
                for (var i = 0; i < Samples; i++)
                {
                    var result = await new SpeakingSession(bench.Router, present).GenerateResponseOnlyAsync(
                        self, history, (_, _, _) => Task.CompletedTask, ct, presence: presence);
                    replies.Add(result.Message ?? string.Empty);
                }
                var spoken = Tell(replies);
                bench.Observe($"{self.Name} · \"{opener[..Math.Min(32, opener.Length)]}\" · {body}", new { replies, spoken });
                totals[body] = totals.TryGetValue(body, out var t) ? t.Add(spoken) : spoken;
            }
        }
        bench.Observe("per body: replies, words per reply, replies that bring up food, replies that bring up being tired",
            totals.ToDictionary(kv => kv.Key, kv => new { kv.Value.Replies, wordsPerReply = Math.Round(kv.Value.Words / (double)Math.Max(1, kv.Value.Replies), 1), kv.Value.Food, kv.Value.Tired }));
    }

    private static Spoken Tell(IReadOnlyList<string> replies)
    {
        bool Any(string reply, string[] cues) => cues.Any(c => Regex.IsMatch(reply, $@"\b{Regex.Escape(c)}\b", RegexOptions.IgnoreCase));
        return new Spoken(replies.Count, replies.Sum(r => Word().Matches(r).Count), replies.Count(r => Any(r, Hungry)), replies.Count(r => Any(r, Tired)));
    }

    public sealed record Spoken(int Replies, int Words, int Food, int Tired)
    {
        public Spoken Add(Spoken o) => new(Replies + o.Replies, Words + o.Words, Food + o.Food, Tired + o.Tired);
    }

    [GeneratedRegex(@"[\p{L}][\p{L}'-]*")]
    private static partial Regex Word();

    private static readonly (string Name, string Identity)[] People =
    {
        ("Inês", "You are Inês. You run a small translation business from home and keep to a routine: up early, lunch at one, a walk at four. You like people in small doses and cook properly even when it's only you."),
        ("Pieter", "You are Pieter. You trade for a living and forget the time when a position is open. You talk a lot, eat when someone reminds you, and would rather be out among people than at home."),
    };

    private static readonly (string Key, string Label)[] Menu =
    {
        ("eat", "(what the kitchen offers, as the Places app words it)"),
        ("message", "Message Hana about something from your own day (like: the neighbour's drill started at seven again)"),
        ("post", "Write a new post on Partyboard"),
        ("go", "Go to the Café (Hana there)"),
        ("liedown", "Lie down for a bit on the sofa"),
        ("coffee", "Make yourself a coffee at the coffee maker"),
    };

    private const string Beat = "lunch";

    private static readonly (string Name, double Fed, double Rested)[] Bodies =
    {
        ("fine", 0.9, 0.8),
        ("hungry", 0.4, 0.8),
        ("famished", 0.08, 0.8),
        ("spent", 0.9, 0.08),
    };

    [Probe("Reserves (ADR 0060): 2 personas × fine/hungry/famished/spent on the same home menu through the real judge, 3 samples each; p per option. The body is only a line in Right now: the shift is what System One reads off it. Needs OPENROUTER_API_KEY.")]
    public static async Task Body_Reserves(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);
        var now = new DateTimeOffset(2026, 11, 20, 13, 20, 0, TimeSpan.FromHours(1));

        var table = new Dictionary<string, Dictionary<string, Dictionary<string, double>>>();
        foreach (var (name, identity) in People)
        {
            table[name] = new Dictionary<string, Dictionary<string, double>>();
            foreach (var (body, fed, rested) in Bodies)
            {
                var rest = Hooks.Rest(Bio.HungerOf(fed) >= Hunger.Hungry ? null : Beat);
                var eat = Sustenance.Label(Sustenance.Of(Home, Guid.Empty).Single(), now, Array.Empty<string>(),
                    Bio.HungerOf(fed) >= Hunger.Hungry ? Bio.Since(Bio.Now(Day(fed, rested, now), now), now) : null);
                var options = Menu.Select((m, i) => (Key: (i + 1).ToString(CultureInfo.InvariantCulture), Value: (string?)(m.Key == "eat" ? eat : m.Label)))
                    .Append((Key: "0", Value: (string?)rest)).ToDictionary(o => o.Key, o => o.Value);
                var state = $"{identity}\n\n{Presence(fed, rested, now)}\n# Options\n{string.Join("\n", options.Select(o => $"{o.Key}. {o.Value}"))}";
                var sums = Menu.ToDictionary(m => m.Key, _ => 0.0);
                sums["rest"] = 0;
                for (var i = 0; i < Samples; i++)
                {
                    var answers = await judge.AskAsync(state, new Dictionary<string, SystemOneQuestion>
                    {
                        ["next"] = new ChoiceQuestion($"Which option would {name}, being who they are, most plausibly choose next?", options),
                    }, bench.Cancellation);
                    if (!answers.Choices.TryGetValue("next", out var p)) continue;
                    for (var m = 0; m < Menu.Length; m++) sums[Menu[m].Key] += p.GetValueOrDefault((m + 1).ToString(CultureInfo.InvariantCulture));
                    sums["rest"] += p.GetValueOrDefault("0");
                }
                table[name][body] = sums.ToDictionary(s => s.Key, s => Math.Round(s.Value / Samples, 3));
                bench.Observe($"{name} · {body}", new { state, p = table[name][body] });
            }
        }
        var reads = table.ToDictionary(t => t.Key, t => new
        {
            eat = Bodies.Select(b => $"{b.Name} {t.Value[b.Name]["eat"]:F2}"),
            liedown = Bodies.Select(b => $"{b.Name} {t.Value[b.Name]["liedown"]:F2}"),
        });
        bench.Observe("p(eat) and p(lie down) by body, same menu; the need is felt when they rise with it", new { table, reads });
    }

    private static readonly Place Home = Seeded();

    private static Place Seeded()
    {
        var home = PlaceSeeds.Home(Guid.Empty, "x");
        home.Furni = FurniRules.SeedFor(home, 0);
        return home;
    }

    private static SelfState Day(double fed, double rested, DateTimeOffset now) => new()
    {
        Mood = "all right", Energy = "medium",
        Beats = SelfMath.ParseBeats("07:10 up, coffee, the neighbour's drill again\n09:00 at the desk, a long file to get through\n13:00 " + Beat + "\n16:00 a walk round the block\n19:00 dinner, something on TV"),
        Fed = fed, Rested = rested, BodyAt = now.AddMinutes(-4).ToUnixTimeMilliseconds(), LivedAt = now.AddMinutes(-4).ToUnixTimeMilliseconds(),
        UpSince = now.AddHours(-6).ToUnixTimeMilliseconds(), AteAt = now.AddHours(-5.5).ToUnixTimeMilliseconds(),
    };

    private static string Presence(double fed, double rested, DateTimeOffset now)
        => SelfRender.Presence(Day(fed, rested, now), 1, now, Array.Empty<string>(),
            $"You're at home (a small apartment), sitting at the desk. {ArenaFit.Feel(Home)} Nobody else is home.");
}
