using System.Text.RegularExpressions;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World;
using PartyTown.World.Self;
using PartyTown.World.Work;

namespace PartyTown.Bench.Probes;

/// <summary>
/// ADR 0026: does "# Where you stand" + "# What you owe" ground replies in work and plans without turning every
/// line into a status report? Same personas, same openers; only the work blocks differ between arms.
/// </summary>
public static partial class WorkProbes
{
    private const int Samples = 3;

    private static readonly string[] Openers =
    {
        "hey, how's your day going?",
        "want to grab a coffee later?",
        "what's something you've been thinking about lately?",
    };

    [Probe("Work A/B: Speaking with vs without the role + owed blocks (on shift at the Café, a Gathering tonight). 2 personas × 3 openers, 3 samples; counts work/plan mentions and status-report openers.")]
    public static async Task Work_OwedAB(Bench bench)
    {
        var ct = bench.Cancellation;
        var now = new DateTimeOffset(2026, 10, 21, 14, 30, 0, TimeSpan.FromHours(1));
        var day = new SelfState
        {
            Mood = "steady", Energy = "medium",
            Beats = SelfMath.ParseBeats("08:00 coffee\n09:00 opened up the Café\n12:30 quick lunch behind the counter"),
        };
        var job = new Occupation { Title = "barista", Workplace = "the Café", PlaceId = Guid.NewGuid(), Start = 9 * 60, End = 17 * 60 };
        var today = new ShiftRecord { Day = Shifts.DayKey(now), Status = ShiftStatus.On };
        var tonight = new Gathering
        {
            HostId = BenchCast.SamId, HostName = "Sam", Title = "Board games", RoomName = "the Lobby", PlaceId = Guid.NewGuid(),
            StartsAt = now.AddHours(5).ToUnixTimeMilliseconds(), AttendeeIds = new() { BenchCast.SamId },
        };
        const string situation = "You're at the Café, standing behind the counter, working. Here: Sam (at the window table).";
        var totals = new Dictionary<string, (int Replies, int Work, int Plan, int Report)>();

        foreach (var self in new[] { BenchCast.Vlad, BenchCast.Denise })
        foreach (var opener in Openers)
        {
            var history = new[] { new ChatMessage { MessageId = 1, Content = opener, SenderType = "user", SenderId = BenchCast.SamId, ChatGroupId = BenchCast.RoomId } };
            var present = new[] { BenchCast.Sam, new ParticipantView(self.Id, self.Name, DriverKind.LLM) };
            tonight.AttendeeIds = new() { BenchCast.SamId, self.Id };
            var work = "# Where you stand\nYou work at the Café.\n"
                + ResponsibilityRules.Render(Shifts.Line(job, today, now, atWork: true), ResponsibilityRules.FromGatherings(new[] { tonight }, self.Id, now));
            var plain = SelfRender.Presence(day, 1, now, Array.Empty<string>(), situation);

            foreach (var (arm, presence) in new[] { ("none", plain), ("work", plain + work) })
            {
                var replies = new List<string>();
                for (var i = 0; i < Samples; i++)
                {
                    var result = await new SpeakingSession(bench.Router, present).GenerateResponseOnlyAsync(
                        self, history, (_, _, _) => Task.CompletedTask, ct, presence: presence);
                    replies.Add(result.Message ?? string.Empty);
                }
                var tally = (replies.Count,
                    replies.Count(r => WorkWord().IsMatch(r)),
                    replies.Count(r => PlanWord().IsMatch(r)),
                    replies.Count(r => ReportOpener().IsMatch(r)));
                bench.Observe($"{self.Name} · \"{opener}\" · {arm}", new { presence, replies, work = tally.Item2, plan = tally.Item3, report = tally.Item4 });
                var t = totals.GetValueOrDefault(arm);
                totals[arm] = (t.Replies + tally.Item1, t.Work + tally.Item2, t.Plan + tally.Item3, t.Report + tally.Item4);
            }
        }
        bench.Observe("totals per arm (work = mentions the shift/counter/customers; plan = mentions tonight's board games; report = opens with a status update)",
            totals.ToDictionary(kv => kv.Key, kv => new { kv.Value.Replies, kv.Value.Work, kv.Value.Plan, kv.Value.Report }));
    }

    [GeneratedRegex(@"\b(shift|counter|customer|work|working|café|cafe|till|espresso|latte)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WorkWord();

    [GeneratedRegex(@"\b(board games?|tonight|lobby|later)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlanWord();

    [GeneratedRegex(@"^\s*(i'?m (at|on|working)|just (finished|got)|currently)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportOpener();
}
