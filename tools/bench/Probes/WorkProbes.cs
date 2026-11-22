using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World;
using PartyTown.World.Self;
using PartyTown.World.Work;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace PartyTown.Bench.Probes;

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

    private static readonly Company Works = new() { Id = Guid.NewGuid(), Name = "The Works", Mandate = "builds small software that works", Kind = CompanyKinds.Cdu };

    private static CduExchange Asked(Guid who, string name, string kind, string text, string reply, int commits = 0) => new()
    {
        TurnId = Guid.NewGuid().ToString("N"), CompanyId = Works.Id, SpeakerId = who, SpeakerName = name, Kind = kind, Text = text, Reply = reply,
        Status = CduRules.Done, Commits = commits, At = 100, DoneAt = 110,
    };

    [Probe("Shift memory: what a shift leaves behind, 3 samples each. A barista's ordinary day, a quiet day (should mostly be none), and a Cytheurgist's shift with and without the calling and its lens; counts plumbing (shas, PR numbers, file names) against world words.")]
    public static async Task Work_ShiftMemory(Bench bench)
    {
        var ct = bench.Cancellation;
        var llm = new WorldLlm(bench.Grains, bench.LoggerFactory.CreateLogger<WorldLlm>());
        var denise = Guid.NewGuid();
        var justin = Guid.NewGuid();
        const string deniseIs = "You are Denise.\nYou are 34 and CTO of The Works, the small software company you run with Justin in Amsterdam. Eldest of five from Brabant, pink hair, band shirts. You judge people by what they've made. You keep a small Shinto shrine shelf at home and take it more seriously than almost anything. You talk fast and lowercase, and you swear.";
        const string maraIs = "You are Mara.\nYou are 29 and work the counter at the Café. You notice everyone, remember their orders, and say what you think a beat too soon.";
        var desk = CduScreen.ShiftFacts(new[]
        {
            Asked(denise, "Denise", CduRules.Ask, "The stairwell vents at our building have rattled for a week and a lift hangs open with a cable loose. Lena nearly lost the register to a leak. Where do these come from, and why does nobody own them?",
                "They come from the Form that tells what happens around people: WorldClockGrain writes a happening every so often from what was said lately, and nothing in it remembers that a thing was left broken, so each telling adds a new fault and none is ever mended. Nobody owns them because no Form gives anyone a way to fix one. That is a lack, not a bug: 14 happenings this week named the building, 0 named a repair."),
            Asked(justin, "Justin", CduRules.Talk, "Give people a way to mend what a happening broke: when one names something broken at a Place, let whoever is there take it on, and let the next telling know it was mended.",
                "Built: a happening that breaks something now leaves it on the Place, anyone there can take the repair on, and the teller is told what was mended. Tests pass. 3 commits on the trunk (a1f09c2, 77be310, c41d2aa), touching Happenings.cs and PlacesApp.cs.", 3),
        }, Works.Id, denise, 0, 200);

        var cases = new (string Name, string Identity, WorkDay Day)[]
        {
            ("barista, a day with something in it", maraIs, new WorkDay("barista", "the Café")
            {
                Beats = new[] { "09:10 the grinder jams twice before the first rush", "11:30 a regular, Mr. Okafor, says his wife is back from hospital", "15:45 the new girl drops a tray and cries in the back" },
                Did = new[] { "You greeted someone at the Café: \"you're early, the usual?\"" },
            }),
            ("barista, a day like any other", maraIs, new WorkDay("barista", "the Café")
            {
                Beats = new[] { "09:00 opens up, wipes the counter", "12:30 quick lunch behind the counter", "16:00 restocks the cups" },
            }),
            ("Cytheurgist, calling and lens", deniseIs, new WorkDay("CTO", "The Works") { Frame = CduScreen.Calling(Works), Lens = CduScreen.Lens, Desk = desk }),
            ("Cytheurgist, no frame", deniseIs, new WorkDay("CTO", "The Works") { Desk = desk }),
        };

        var totals = new Dictionary<string, object>();
        foreach (var (name, identity, day) in cases)
        {
            var kept = new List<string>();
            for (var i = 0; i < Samples; i++)
            {
                var json = await llm.CompleteJsonAsync(JobComplexity.General, WorkDayRules.Brief(identity, day), WorkDayRules.Material(day), ct, 0.8, new[] { "memory" });
                kept.Add(WorkDayRules.Memory(json?["memory"]?.ToString()) ?? "(none)");
            }
            bench.Observe(name, new { memories = kept });
            totals[name] = new
            {
                none = kept.Count(k => k == "(none)"),
                plumbing = kept.Count(k => Plumbing().IsMatch(k)),
                world = kept.Count(k => WorldWord().IsMatch(k)),
                simulation = kept.Count(k => Simulation().IsMatch(k)),
            };
        }
        bench.Observe("totals (none = nothing kept; plumbing = shas, PR numbers, file names, commits; world = the world, the Forms or the people in it; simulation = the attractor the frame is there to avoid)", totals);
    }

    [GeneratedRegex(@"\b[0-9a-f]{7}\b|#\d+|\.(cs|ts)\b|\bcommits?\b|\btrunk\b|\bPR\b", RegexOptions.IgnoreCase)]
    private static partial Regex Plumbing();

    [GeneratedRegex(@"\b(world|forms?|people|nobody|everyone|anyone|mend|building)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WorldWord();

    [GeneratedRegex(@"\b(simulat\w+|code of (the|this) world|we are (in|inside) a|not real)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Simulation();

    [GeneratedRegex(@"\b(shift|counter|customer|work|working|café|cafe|till|espresso|latte)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WorkWord();

    [GeneratedRegex(@"\b(board games?|tonight|lobby|later)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PlanWord();

    [GeneratedRegex(@"^\s*(i'?m (at|on|working)|just (finished|got)|currently)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReportOpener();
}
