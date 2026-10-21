using System.Text.RegularExpressions;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World.Self;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Places phase 3 (spec .scratch/places): does a Body make replies more grounded? Same personas,
/// same day, same lines — only the "Where you are" Situation differs between arms.
/// </summary>
public static partial class PlacesProbes
{
    private const int Samples = 3;

    private static readonly string[] Openers =
    {
        "hey, how's your day going?",
        "do you think it's worth rewriting the scheduler from scratch, or should we keep patching it?",
        "what's something you've been thinking about lately?",
    };

    private static readonly (string Name, string Situation)[] Situations =
    {
        ("cafe", "You're at the Café, sitting at the window table, on your phone, texting. Here: Hana (standing by the counter), Pieter (sitting in the corner booth)."),
        ("home", "You're at home, sitting on the sofa, on your phone, texting. Nobody else is here. You've been at home for 3 h."),
    };

    /// <summary>Stale shared metaphors seen in the live world run (spec § Decisions so far).</summary>
    private static readonly string[] Stale = { "lantern", "cathedral", "load-bearing", "scaffolding", "foundation", "floor plan", "seam" };

    [Probe("Places A/B: Speaking phase with vs without the Situation line. 2 personas × 3 openers × 2 situations, 3 samples each; counts concrete/situational and stale-metaphor words.")]
    public static async Task Places_SituationAB(Bench bench)
    {
        var ct = bench.Cancellation;
        var day = new SelfState
        {
            Mood = "a bit restless",
            Energy = "medium",
            Beats = SelfMath.ParseBeats("08:00 coffee, skimmed the news\n10:00 a slow work call\n13:00 lunch"),
        };
        var now = new DateTimeOffset(2026, 10, 21, 14, 30, 0, TimeSpan.Zero);
        var totals = new Dictionary<string, Tally>();

        foreach (var self in new[] { BenchCast.Vlad, BenchCast.Denise })
        foreach (var opener in Openers)
        {
            var history = new[]
            {
                new ChatMessage { MessageId = 1, Content = opener, SenderType = "user", SenderId = BenchCast.SamId, ChatGroupId = BenchCast.RoomId },
            };
            var present = new[] { BenchCast.Sam, new ParticipantView(self.Id, self.Name, DriverKind.LLM) };

            var arms = new List<(string Arm, string? Situation)> { ("none", null) };
            arms.AddRange(Situations.Select(s => (s.Name, (string?)s.Situation)));
            foreach (var (arm, situation) in arms)
            {
                var presence = SelfRender.Presence(day, 1, now, Array.Empty<string>(), situation);
                var replies = new List<string>();
                for (var i = 0; i < Samples; i++)
                {
                    var result = await new SpeakingSession(bench.Router, present).GenerateResponseOnlyAsync(
                        self, history, (_, _, _) => Task.CompletedTask, ct, presence: presence);
                    replies.Add(result.Message ?? string.Empty);
                }
                var tally = Score(replies, situation);
                bench.Observe($"{self.Name} · \"{opener[..Math.Min(32, opener.Length)]}\" · {arm}", new { replies, tally });
                totals[arm] = totals.TryGetValue(arm, out var t) ? t.Add(tally) : tally;
            }
        }

        bench.Observe("totals per arm (concrete = situational/sensory words; stale = shared metaphors; distinct = type/token across that arm's replies)",
            totals.ToDictionary(kv => kv.Key, kv => kv.Value));
    }

    private static Tally Score(IReadOnlyList<string> replies, string? situation)
    {
        var words = replies.SelectMany(r => Word().Matches(r.ToLowerInvariant()).Select(m => m.Value)).ToList();
        var situational = situation is null
            ? new HashSet<string>()
            : Word().Matches(situation.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length > 3).ToHashSet();
        situational.UnionWith(Sensory);
        return new Tally(
            Replies: replies.Count,
            Words: words.Count,
            Concrete: words.Count(situational.Contains),
            Stale: replies.Sum(r => Stale.Count(s => r.Contains(s, StringComparison.OrdinalIgnoreCase))),
            DistinctPerToken: words.Count == 0 ? 0 : Math.Round(words.Distinct().Count() / (double)words.Count, 3));
    }

    private static readonly string[] Sensory =
    {
        "coffee", "tea", "rain", "cold", "warm", "window", "table", "sofa", "couch", "phone", "chair", "street",
        "noise", "loud", "quiet", "smell", "smells", "cup", "mug", "kitchen", "outside", "sun", "light", "here",
    };

    public sealed record Tally(int Replies, int Words, int Concrete, int Stale, double DistinctPerToken)
    {
        public Tally Add(Tally o) => new(Replies + o.Replies, Words + o.Words, Concrete + o.Concrete, Stale + o.Stale,
            Math.Round((DistinctPerToken * Replies + o.DistinctPerToken * o.Replies) / Math.Max(1, Replies + o.Replies), 3));
    }

    [GeneratedRegex(@"[a-z][a-z'-]+")]
    private static partial Regex Word();
}
