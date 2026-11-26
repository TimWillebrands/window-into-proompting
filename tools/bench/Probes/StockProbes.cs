using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Self;
using PartyTown.World.Work.Cdu;

namespace PartyTown.Bench.Probes;

public static partial class StockProbes
{
    private const int Samples = 4;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StockedAgo = TimeSpan.FromMinutes(5);
    private const string MessengerTitle = "Messenger (private chats: one-to-one and in groups)";
    private const string BoardTitle = "Partyboard (social feed)";
    private const string PlacesTitle = "Places (where your body is)";

    private sealed class Tally
    {
        public int Total;
        public int Lost;
    }

    private sealed record StockScene(
        string Key, bool Stuck, CastMember Self, DateTimeOffset Now, List<WorldActivity> Deeds,
        Func<SelfState?, IReadOnlyList<WorldActivity>, string> World, Dictionary<string, string?> Options,
        string? LoopLabel = null, string? LoopWhere = null, Regex? Loops = null, string? LoopMeans = null,
        SelfState? Day = null, string? Situation = null, string? Friend = null, WorldActivity? Next = null);

    private sealed record Taken(string? Text, TakingStock.Stock? Stock);

    private sealed record Picked(SelfState After, TakingStock.Stock Stock, bool Extra);

    private static readonly IReadOnlyList<WorldActivity> Nothing = Array.Empty<WorldActivity>();

    private static async Task<T?> Within<T>(Bench bench, Tally tally, string what, Func<CancellationToken, Task<T?>> call) where T : class
    {
        Interlocked.Increment(ref tally.Total);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(bench.Cancellation);
        cts.CancelAfter(CallBudget);
        try
        {
            var result = await call(cts.Token);
            if (result is null) Interlocked.Increment(ref tally.Lost);
            return result;
        }
        catch (Exception ex) when (!bench.Cancellation.IsCancellationRequested)
        {
            Interlocked.Increment(ref tally.Lost);
            bench.Logger("StockProbes").LogWarning("A call was lost ({What}): {Why}", what, ex.Message.Split('\n')[0]);
            return null;
        }
    }

    private static HttpSystemOneJudge? Judge(HttpClient http)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        return string.IsNullOrWhiteSpace(key)
            ? null
            : new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);
    }

    private static string State(StockScene s, SelfState? after, IReadOnlyList<WorldActivity> later)
        => $"{WorldLlm.Identity(s.Self)}\n\n{s.World(after, later)}# Options\n{string.Join("\n", s.Options.Where(o => o.Key != "0").Select(o => $"{o.Key}. {o.Value}"))}\n0. {s.Options["0"]}\n";

    private static Dictionary<string, SystemOneQuestion> Questions(StockScene s, bool grip)
    {
        var questions = new Dictionary<string, SystemOneQuestion>
        {
            ["next"] = new ChoiceQuestion($"Which option would {s.Self.Name}, being who they are, most plausibly choose next?", s.Options),
            [Lack.Key] = Lack.Question(s.Self.Name),
        };
        if (grip) questions[Grip.Key] = Grip.Question(s.Self.Name);
        return questions;
    }

    private static double Ripeness(StockScene s) => TakingStock.Ripeness(new SelfState(), TakingStock.Since(s.Deeds, 0, s.Now), s.Now);

    private static string Material(StockScene s)
        => TakingStock.Material(s.World(null, Nothing), s.Deeds, s.Options.Where(o => o.Key != "0").Select(o => o.Value!), s.Options["0"]!, s.Now);

    [Probe("Taking stock, the judge (ADR 0069): five staged Life states (stuck at the CDU desk, work going well, an ordinary good day, a loop in DM talk, waiting on someone), the real next + lack + Grip questions as Life asks them, 4 samples each; p(stuck), grip and TakingStock.Chance at ripe 1. Needs OPENROUTER_API_KEY.")]
    public static async Task Stock_Grip(Bench bench)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        if (Judge(http) is not { } judge)
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        var tally = new Tally();
        var scenes = Scenes();
        var table = new Dictionary<string, object>();
        var gate = new object();

        await Task.WhenAll(scenes.Select(async s =>
        {
            var state = State(s, null, Nothing);
            var stuck = new List<double>();
            var lacks = new List<double>();
            var next = s.Options.Keys.ToDictionary(k => k, _ => 0.0);
            var answered = 0;
            for (var i = 0; i < Samples; i++)
            {
                var answers = await Within(bench, tally, $"{s.Key} grip", async c => (SystemOneAnswers?)await judge.AskAsync(state, Questions(s, true), c));
                if (answers is null) continue;
                if (answers.Nouls.TryGetValue(Grip.Key, out var g)) stuck.Add(g);
                else Interlocked.Increment(ref tally.Lost);
                if (answers.Nouls.TryGetValue(Lack.Key, out var l)) lacks.Add(l);
                if (answers.Choices.TryGetValue("next", out var p))
                {
                    answered++;
                    foreach (var k in s.Options.Keys) next[k] += p.GetValueOrDefault(k);
                }
            }
            double? mean = stuck.Count == 0 ? null : Math.Round(stuck.Average(), 3);
            var row = new
            {
                stuck = s.Stuck,
                pStuck = mean,
                samples = stuck.Select(g => Math.Round(g, 3)).ToList(),
                grip = mean is { } m ? Math.Round(Grip.FromLoss(m), 3) : (double?)null,
                chanceAtRipe1 = mean is { } n ? Math.Round(TakingStock.Chance(Grip.FromLoss(n), 1), 3) : (double?)null,
                ripeness = Math.Round(Ripeness(s), 3),
                lack = lacks.Count == 0 ? (double?)null : Math.Round(lacks.Average(), 3),
            };
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{s.Key,-44} p(stuck)={mean:F3} chance={row.chanceAtRipe1:F3}"));
            lock (gate)
            {
                table[s.Key] = row;
                bench.Observe($"A · {s.Key}", new { row, question = ((NoulQuestion)Grip.Question(s.Self.Name)).Instructions, state,
                    next = next.ToDictionary(kv => $"{kv.Key}. {s.Options[kv.Key]}", kv => Math.Round(kv.Value / Math.Max(1, answered), 3)) });
            }
        }));
        bench.Observe("A · p(stuck), grip = 1 - p, chance = (1 - grip)^2 at ripe 1", table);
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }

    private static async Task<Taken> TakeStockAsync(Bench bench, WorldLlm llm, Tally tally, StockScene s)
    {
        var text = await Within(bench, tally, $"{s.Key} stock", c => llm.For(s.Self.Id).CompleteAsync(JobComplexity.CharacterThoughts,
            TakingStock.Brief(WorldLlm.Identity(s.Self)), Material(s), c, TakingStock.Temperature));
        return new Taken(text, text is null ? null : TakingStock.Read(text));
    }

    private static async Task<Picked?> PickAsync(Bench bench, WorldLlm llm, Tally tally, StockScene s, IReadOnlyList<Taken> runs)
    {
        var found = runs.FirstOrDefault(r => r.Stock is { Nothing: false, Upshot: not null })?.Stock;
        var extra = false;
        for (var i = 0; found is null && i < Samples; i++)
        {
            extra = true;
            found = (await TakeStockAsync(bench, llm, tally, s)).Stock is { Nothing: false, Upshot: not null } more ? more : null;
        }
        if (found is null) return null;
        var after = Copy(s.Day);
        TakingStock.Record(after, found.Notes, found.Upshot, (s.Now - StockedAgo).ToUnixTimeMilliseconds());
        return new Picked(after, found, extra);
    }

    [Probe("Taking stock, the look back and what it changes (ADR 0069), on the CharacterThoughts / General tiers: B the real Brief over the real material (world, deeds, what they could do from here) for five staged scenes, 4 samples; F the same-upshot rule pairwise on B's upshots; C the Life voicing call for the option that continues the loop with and without one stock-take rendered the real way (scenes 1, 4, 5); E p(next) for scene 1 through the judge as it is, right after a stock-take, and after one lived deed. Needs OPENROUTER_API_KEY for E.")]
    public static async Task Stock_Take(Bench bench)
    {
        var llm = new WorldLlm(bench.Grains, bench.LoggerFactory.CreateLogger<WorldLlm>());
        var tally = new Tally();
        var scenes = Scenes();
        var gate = new object();

        var stocks = new Dictionary<string, List<Taken>>();
        await Task.WhenAll(scenes.Select(async s =>
        {
            var runs = new List<Taken>();
            for (var i = 0; i < Samples; i++) runs.Add(await TakeStockAsync(bench, llm, tally, s));
            lock (gate)
            {
                stocks[s.Key] = runs;
                bench.Observe($"B · {s.Key}", new
                {
                    stuck = s.Stuck,
                    material = Material(s),
                    cameToNothing = runs.Count(r => r.Stock?.Nothing == true),
                    noSoLine = runs.Count(r => r.Stock is { Nothing: false, Upshot: null }),
                    lost = runs.Count(r => r.Stock is null),
                    runs = runs.Select(r => new { text = r.Text, nothing = r.Stock?.Nothing, upshot = r.Stock?.Upshot }).ToList(),
                });
            }
        }));
        bench.Observe("B · came to nothing, of 4", scenes.ToDictionary(s => s.Key, s => new
        {
            stuck = s.Stuck,
            nothing = stocks[s.Key].Count(r => r.Stock?.Nothing == true),
            upshots = stocks[s.Key].Where(r => r.Stock?.Upshot is not null).Select(r => r.Stock!.Upshot).ToList(),
        }));
        bench.Observe($"F · same-upshot rule pairwise within a scene (TakingStock.Overlap: share of the smaller word set, bar {TakingStock.SameAt}; jaccard for comparison)", scenes.ToDictionary(s => s.Key, s =>
        {
            var ups = stocks[s.Key].Select(r => r.Stock?.Upshot).Where(u => u is not null).Select(u => u!).ToList();
            var pairs = new List<object>();
            for (var i = 0; i < ups.Count; i++)
                for (var j = i + 1; j < ups.Count; j++)
                    pairs.Add(new { a = i + 1, b = j + 1, overlap = Math.Round(TakingStock.Overlap(ups[i], ups[j]), 3), jaccard = Math.Round(Jaccard(ups[i], ups[j]), 3), same = TakingStock.Same(ups[i], ups[j]) });
            return (object)new { upshots = ups.Count, pairsSame = pairs.Count(p => ((dynamic)p).same), pairCount = pairs.Count, pairs };
        }));

        var picked = new Dictionary<string, Picked?>();
        foreach (var s in scenes.Where(s => s.Loops is not null))
            picked[s.Key] = await PickAsync(bench, llm, tally, s, stocks[s.Key]);
        bench.Observe("the stock-take used for C and E (first of B with notes and an upshot; extra = B had none and more were drawn)",
            picked.ToDictionary(kv => kv.Key, kv => kv.Value is { } p ? new { p.Stock.Upshot, p.Stock.Notes, p.Extra } : null));

        async Task Voicing(StockScene s)
        {
            var arms = new Dictionary<string, object>();
            var after = picked[s.Key]?.After;
            foreach (var (arm, state, later) in new[] { ("without", (SelfState?)null, Nothing), ("with", after, after is null ? Nothing : new[] { Stocked(s) }) })
            {
                if (arm == "with" && state is null) continue;
                var world = s.World(state, later);
                var runs = new List<object>();
                int continued = 0, declined = 0, lost = 0;
                for (var i = 0; i < Samples; i++)
                {
                    var voiced = await Within(bench, tally, $"{s.Key} voice", c => llm.For(s.Self.Id).CompleteJsonAsync(JobComplexity.General,
                        $"{WorldLlm.Identity(s.Self)}\n\nYou've decided what to do next. Say what's on your mind and what exactly you'll do: only this one thing, in {s.LoopWhere}, nothing from elsewhere.\n{LifeChoice.DeclineCue}\nOutput ONLY JSON: {WorkProbes.LoopIntentShape}",
                        $"{world}\n# What you've decided\n{s.LoopLabel} ({s.LoopWhere})", c, 0.8, new[] { "thought", "intent" }, new[] { "decline" }, LifeChoice.Refuse));
                    if (voiced is null) { lost++; continue; }
                    var thought = voiced["thought"]?.ToString();
                    var intent = voiced["intent"]?.ToString() ?? string.Empty;
                    var no = LifeChoice.Declined(voiced);
                    var loops = !no && s.Loops!.IsMatch(intent);
                    if (no) declined++;
                    if (loops) continued++;
                    runs.Add(new { thought, intent, decline = no, continuesTheLoop = loops });
                }
                arms[arm] = new { continued, declined, lost, runs };
            }
            lock (gate) bench.Observe($"C · {s.Key} · {s.LoopLabel}", new { loopRegex = s.LoopMeans, arms, withWorld = after is null ? null : s.World(after, new[] { Stocked(s) }) });
        }

        async Task Pick1(StockScene s)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            if (Judge(http) is not { } judge || picked[s.Key] is not { } p)
            {
                bench.Observe("E · skipped", new { judge = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") is { Length: > 0 }, stock = picked[s.Key] is not null });
                return;
            }
            var table = new Dictionary<string, object>();
            var states = new (string Name, SelfState? After, IReadOnlyList<WorldActivity> Later)[]
            {
                ("as it is", null, Nothing),
                ("right after a stock-take (notes, upshot line, stock deed)", p.After, new[] { Stocked(s) }),
                ("after one lived deed (upshot line, the deed, no notes)", p.After, new[] { Stocked(s), s.Next! }),
            };
            foreach (var (arm, after, later) in states)
            {
                var state = State(s, after, later);
                var sum = s.Options.Keys.ToDictionary(k => k, _ => 0.0);
                var answered = 0;
                for (var i = 0; i < Samples; i++)
                {
                    var answers = await Within(bench, tally, "E pick", async c => (SystemOneAnswers?)await judge.AskAsync(state, Questions(s, false), c));
                    if (answers is null || !answers.Choices.TryGetValue("next", out var next)) continue;
                    answered++;
                    foreach (var k in s.Options.Keys) sum[k] += next.GetValueOrDefault(k);
                }
                table[arm] = new { answered, notesInState = state.Contains("# What you worked out", StringComparison.Ordinal), next = sum.ToDictionary(kv => $"{kv.Key}. {s.Options[kv.Key]}", kv => Math.Round(kv.Value / Math.Max(1, answered), 3)), state };
            }
            lock (gate) bench.Observe("E · scene 1 p(next), mean of 4", table);
        }

        var byKey = scenes.ToDictionary(s => s.Key[..1]);
        await Task.WhenAll(Voicing(byKey["1"]), Voicing(byKey["4"]), Voicing(byKey["5"]), Pick1(byKey["1"]));
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }

    [Probe("Taking stock in talk (ADR 0069): D, a friend's 'hey, hoe gaat het met je?' DM to Mara (an ordinary day) and Justin (a loop in talk), the Speaking phase with and without the upshot of one real stock-take in presence, 4 samples; word counts and a flag for 6+ consecutive words of the upshot.")]
    public static async Task Stock_Talk(Bench bench)
    {
        var llm = new WorldLlm(bench.Grains, bench.LoggerFactory.CreateLogger<WorldLlm>());
        var tally = new Tally();
        foreach (var s in new[] { Mara(), Justin() })
        {
            var runs = new List<Taken>();
            for (var i = 0; i < Samples; i++) runs.Add(await TakeStockAsync(bench, llm, tally, s));
            var picked = await PickAsync(bench, llm, tally, s, runs);
            var friend = new ParticipantView(Guid.NewGuid(), s.Friend!, DriverKind.User);
            var self = s.Self.ToSelfView(DriverKind.LLM);
            var history = new[] { new ChatMessage { MessageId = 1, Content = "hey, hoe gaat het met je?", SenderType = "user", SenderId = friend.Id, ChatGroupId = Guid.NewGuid() } };
            var present = new[] { friend, new ParticipantView(self.Id, self.Name, DriverKind.LLM) };
            var upshot = picked?.Stock.Upshot;
            var arms = new Dictionary<string, object>();
            foreach (var (arm, day) in new[] { ("without", s.Day), ("with", picked?.After) })
            {
                if (day is null) continue;
                var presence = SelfRender.Presence(day, 1, s.Now, Array.Empty<string>(), s.Situation);
                var replies = new List<object>();
                for (var i = 0; i < Samples; i++)
                {
                    var result = await Within(bench, tally, $"{s.Key} talk", async c => (SpeakingResult?)await new SpeakingSession(bench.Router, present)
                        .GenerateResponseOnlyAsync(self, history, (_, _, _) => Task.CompletedTask, c, presence: presence));
                    var reply = result?.Message;
                    if (reply is null) continue;
                    replies.Add(new { reply, words = Words(reply).Count, recital = upshot is not null && Recites(reply, upshot) });
                }
                arms[arm] = new { presence, replies };
            }
            bench.Observe($"D · {s.Key} · {s.Friend}: hey, hoe gaat het met je?", new { upshot, extra = picked?.Extra, arms });
        }
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }

    private static double Jaccard(string a, string b)
    {
        var x = Words(a).ToHashSet();
        var y = Words(b).ToHashSet();
        return x.Count == 0 || y.Count == 0 ? 0 : (double)x.Intersect(y).Count() / x.Union(y).Count();
    }

    private static SelfState Copy(SelfState? day) => day is null
        ? new SelfState()
        : new SelfState { Mood = day.Mood, Energy = day.Energy, Beats = day.Beats.ToList() };

    private static List<string> Words(string s) => WordPattern().Matches(s.ToLowerInvariant()).Select(m => m.Value).ToList();

    private static bool Recites(string reply, string upshot)
    {
        var said = Words(reply);
        var up = Words(upshot);
        if (up.Count < 6 || said.Count < 6) return false;
        var grams = Enumerable.Range(0, said.Count - 5).Select(i => string.Join(' ', said.Skip(i).Take(6))).ToHashSet();
        return Enumerable.Range(0, up.Count - 5).Any(i => grams.Contains(string.Join(' ', up.Skip(i).Take(6))));
    }

    private static WorldActivity Did(Guid who, string name, DateTimeOffset at, string app, string action, string summary, string? thought = null, string? affordance = null)
        => new() { PersonaId = who, PersonaName = name, App = app, Action = action, Summary = summary, Thought = thought, Affordance = affordance, Ts = at.ToUnixTimeMilliseconds() };

    private static WorldActivity Stocked(StockScene s) => Did(s.Self.Id, s.Self.Name, s.Now - StockedAgo, "life", TakingStock.Action, TakingStock.Summary);

    private static string Recent(IEnumerable<WorldActivity> deeds, DateTimeOffset now)
    {
        var own = deeds.OrderByDescending(a => a.Ts).Take(12)
            .Where(a => a.Action is not ("fizzle" or "error" or Lack.Action or OwnHappenings.Action)).Take(8).ToList();
        return own.Count == 0 ? "(nothing yet today)" : string.Join("\n", own.Take(5).Select(a => $"- {WorldStream.Ago(a.Ts, now)}: {a.Action} {a.Summary}"));
    }

    private static string LifeWorld(string presence, IReadOnlyList<string> onMind, string? notes, string recent, IReadOnlyList<(string Title, string Block)> apps)
    {
        var sb = new StringBuilder();
        sb.AppendLine(presence.Trim());
        sb.AppendLine();
        sb.AppendLine("# On your mind");
        sb.AppendLine(onMind.Count == 0 ? "(nothing in particular)" : string.Join("\n", onMind.Select(m => $"- {m}")));
        if (notes is not null) sb.AppendLine($"\n{notes}");
        sb.AppendLine("\n# What you did recently");
        sb.AppendLine(recent);
        sb.AppendLine("\n# The world right now");
        foreach (var (title, block) in apps) sb.AppendLine($"## {title}\n{block}\n");
        return sb.ToString();
    }

    private static string AtTheDesk(string world, IEnumerable<WorldActivity> deeds, SelfState? after, IReadOnlyList<WorldActivity> later, DateTimeOffset now)
    {
        world += "\n";
        if (after is null) return world;
        var nl = world.IndexOf('\n');
        if (TakingStock.Line(after, now) is { } line) world = world.Insert(nl + 1, $"{line}\n");
        if (TakingStock.Notes(after, deeds.Concat(later), now) is { } notes)
        {
            var at = world.IndexOf("\n# Where you stand", StringComparison.Ordinal);
            world = world.Insert(at, $"\n{notes}\n");
        }
        const string recent = "# What you did recently\n";
        var r = world.IndexOf(recent, StringComparison.Ordinal) + recent.Length;
        return world.Insert(r, string.Concat(later.OrderByDescending(a => a.Ts).Select(a => $"- {WorldStream.Ago(a.Ts, now)}: {a.Action} {a.Summary}\n")));
    }

    private static string Presence(StockScene s, SelfState? after, bool onShift)
        => SelfRender.Presence(after ?? s.Day, 1, s.Now, Array.Empty<string>(), s.Situation) + (onShift ? "\n" + Hooks.ShiftWins : string.Empty);

    private static List<WorldActivity> With(StockScene s, IReadOnlyList<WorldActivity> later) => s.Deeds.Concat(later).ToList();

    private static CastMember Denise { get; } = new()
    {
        Id = BenchCast.DeniseId, Name = "Denise", DefaultDriver = DriverKind.LLM,
        SystemPrompt = WorkProbes.DeniseIs["You are Denise.\n".Length..],
    };

    private static readonly Dictionary<string, string?> DeskMenu = new()
    {
        ["1"] = "Ask the CDU how something in this world is built, or what happened",
        ["2"] = "Hand the CDU a piece of work: a brief for one change to this world",
        ["3"] = "Say something to Justin Ruysch, who's here",
        ["4"] = "Write a new post on Partyboard",
        ["5"] = "Go to the Café",
        ["0"] = Hooks.RestOnShift,
    };

    private static List<StockScene> Scenes() => new() { Stuck(), Going(), Mara(), Justin(), Pieter() };

    private static StockScene Stuck()
    {
        var loop = WorkProbes.LoopScene();
        var now = DateTimeOffset.UtcNow;
        var me = Denise.Id;
        WorldActivity Ask(int minutesAgo, string thought) => Did(me, "Denise", now.AddMinutes(-minutesAgo), "cdu", CduRules.Ask, "asked the CDU at The Works a question", thought);
        var deeds = new List<WorldActivity>
        {
            Did(me, "Denise", now.AddMinutes(-175), "work", "work", "is busy at work (CTO, The Works)", "desk first, then the cdu, before justin starts asking what i found"),
            Ask(160, "the hum and the crack in the marble have to come from somewhere in the forms"),
            Ask(120, "fine, not the hum. the coffee cart is Broken:true, something set that"),
            Ask(100, "the sandwich tray was set out by someone, the forms must know who"),
            Did(me, "Denise", now.AddMinutes(-90), "life", "rest", "thought better of it (Say something to Justin Ruysch, who's here)", "nothing to tell him yet", "places|say"),
            Ask(60, "the brass key has to open something, that's a real thread"),
            Ask(40, "the water main shutoff is in someone's day, so something has to track water"),
            Ask(20, "who else on the street loses water, that'd tell me where it's written down"),
        };
        return new StockScene("1 stuck at work (Denise, CDU desk)", true, Denise, now, deeds,
            (after, later) => AtTheDesk(loop.World(false), deeds, after, later, now), DeskMenu,
            "Ask the CDU how something in this world is built, or what happened", WorkProbes.LoopWhere, Lift(), "intent is about the lift (the new Happening: lift|elevator|dashes|lunch order)",
            Next: Did(me, "Denise", now.AddMinutes(-2), "places", "say", "out loud at The Works: \"justin, the key, the parcel, the water main: dead ends, nothing behind any of them. i'm dropping them.\"", "better he hears it from me than guesses"));
    }

    private static StockScene Going()
    {
        var now = DateTimeOffset.UtcNow;
        var me = Denise.Id;
        var justin = Guid.NewGuid();
        CduExchange Turn(Guid who, string name, string kind, string text, string reply, int minutesAgo, int commits = 0) => new()
        {
            TurnId = Guid.NewGuid().ToString("N"), CompanyId = WorkProbes.Works.Id, SpeakerId = who, SpeakerName = name, Kind = kind, Text = text, Reply = reply,
            Status = CduRules.Done, Model = "beam-free", Commits = commits,
            At = now.AddMinutes(-minutesAgo).ToUnixTimeMilliseconds(), DoneAt = now.AddMinutes(-minutesAgo + 1).ToUnixTimeMilliseconds(),
        };
        var exchanges = new List<CduExchange>
        {
            Turn(me, "Denise", CduRules.Ask, "denise here, question only, no changes. the odd things in the building keep coming: the hum, the marble crack, the broken coffee cart, the parcel. skip what each one is. what writes them? which form tells the happenings, and does anything remember what it already broke?",
                "Denise — one Form tells them all: the World clock writes a Happening every so often from what was said lately, and the Places carry it as a Lately line. Nothing in it remembers what it broke: it never reads back what it told, so each telling adds a new fault and none is ever mended. Nobody can fix one because no Form offers a way to. 14 happenings this week named the building, 0 named a repair.", 150),
            Turn(justin, "Justin Ruysch", CduRules.Ask, "Premise first: I am not asking for a change. When a Happening names something broken, is there any record of who was affected, or only the line itself?",
                "Justin — only the line. A Happening is text on the Place; no Take, no Furni state and no person record follows from it, except the coffee cart's Broken flag, which a tend clears.", 125),
            Turn(me, "Denise", CduRules.Talk, "brief: when a happening breaks something at a place, leave it on the place as a broken thing, let whoever is there take the repair on, and let the next telling know it was mended. done when a happening that breaks a thing shows up as broken furni and a repair clears it and the teller hears about it.",
                "Built: a Happening that breaks something now leaves it broken on the Place, anyone there can take the repair on, and the teller hears what was mended. Tests pass. 2 commits on the trunk.", 110, 2),
            Turn(me, "Denise", CduRules.Ask, "denise, question only. did the repair one land? what's on the trunk now, and is anything of it in front of people yet?",
                "Denise — yes: 2 commits ahead of master on the trunk, both from your brief (a broken thing stays on the Place; a repair clears it and tells the teller). They go out with the next PR; nothing is in front of people until then.", 60),
        };
        var status = new CduStatus(false, null, 0, "beam-free",
            new CduTrunk("e442f47", new List<CduCommit> { new("a1f09c2", "happenings: a broken thing stays on the Place", "cdu"), new("77be310", "places: take a repair on, the teller hears it", "cdu") }),
            null, new CduAllowance(0.4, 1), 0, 0, null,
            new List<CduReset> { new(131, "MERGED", now.AddDays(-1).ToUnixTimeMilliseconds()) },
            new List<CduModel> { new("beam-free", "free preview", "free", 0, 0), new("mimo-v2.6-flash", "quick, cheapest", "small", 0.14, 0.28), new("kimi-k2.7-code", "the best coder", "capable", 0.95, 4) });
        var health = new WorldHealth(412, 9, 61, new List<(string, int)> { ("tried to walk to the Café: no way through", 5), ("meant to: Book a session with Lena", 3) })
        {
            Uses = new List<(string, int)> { ("the Places: walking about, talking face to face", 171), ("Messenger: DMs and group chats", 88), ("the Board", 34), ("diaries", 21), ("shifts", 19), ("the CDU", 17), ("practices: doctors, studios", 2) },
            Lacks = new List<(string, string)>
            {
                ("Pieter Voss", "have Dan's notes in hand now to check the settlement figures before thursday"),
                ("Inês Moreira", "get someone in to fix the tap that keeps running and the damp cupboard under the sink"),
                ("Mara", "fix the coffee machine at work herself instead of posting about it again"),
            },
        };
        var rendered = CduScreen.Render(new CduDesk(WorkProbes.Works, new CduState { Exchanges = exchanges }, status, health, me)
        {
            Colleagues = new List<(string, bool)> { ("Justin Ruysch", true) }, Now = now,
        });
        var deeds = new List<WorldActivity>
        {
            Did(me, "Denise", now.AddMinutes(-175), "work", "work", "is busy at work (CTO, The Works)", "desk first, then the cdu, before justin starts asking what i found"),
            Did(me, "Denise", now.AddMinutes(-150), "cdu", CduRules.Ask, "asked the CDU at The Works a question", "these happenings keep landing in the building. i want to know what writes them, not what each one is"),
            Did(me, "Denise", now.AddMinutes(-135), "places", "say", "out loud at The Works: \"justin, it's one form writing all of them and nothing ever gets mended. that's the bug.\"", "he said he'd rather know than guess"),
            Did(me, "Denise", now.AddMinutes(-110), "cdu", CduRules.Talk, "handed the CDU at The Works a piece of work", "small brief: a broken thing stays broken on the place till someone mends it"),
            Did(me, "Denise", now.AddMinutes(-60), "cdu", CduRules.Ask, "asked the CDU at The Works a question", "check it actually built what i asked before i tell anyone"),
            Did(me, "Denise", now.AddMinutes(-30), "places", "say", "out loud at The Works: \"two commits on the trunk. a broken thing stays broken till someone mends it now.\"", "finally something that holds"),
        };
        StockScene scene = null!;
        string World(SelfState? after, IReadOnlyList<WorldActivity> later) => AtTheDesk($"""
            It is Wednesday 14:10. You're at The Works, at your desk, on shift until 17:00. You feel steady, a little restless.

            # Lately in the world
            - 6 min ago, where you are: {WorkProbes.LoopHappening}

            # On your mind
            - Your sister Fleur wants to know by tonight whether you're coming on Sunday.
            - Justin said over lunch he'd rather know than guess.

            # Where you stand
            - Cytheurgy: {CduScreen.Secret(WorkProbes.Works)[1].Reasoning}

            # What you did recently
            {Recent(scene.Deeds, now)}

            # The world right now
            ## {WorkProbes.LoopWhere}
            {CduScreen.Frame(WorkProbes.Works)}
            {rendered}

            ## Messenger
            Nothing new.

            ## Places
            You're at The Works. Justin Ruysch is here, at his desk.

            """, deeds, after, later, now);
        scene = new StockScene("2 work going well (Denise, CDU desk)", false, Denise, now, deeds, World, DeskMenu);
        return scene;
    }

    private static StockScene Mara()
    {
        var now = new DateTimeOffset(2026, 11, 25, 15, 40, 0, TimeSpan.FromHours(1));
        var self = new CastMember
        {
            Id = Guid.NewGuid(), Name = "Mara", DefaultDriver = DriverKind.LLM,
            SystemPrompt = "You are 29 and work the counter at the Café. You notice everyone, remember their orders, and say what you think a beat too soon. You live above a bike shop in De Pijp and walk along the canal when you get the chance.",
        };
        var day = new SelfState
        {
            Mood = "good", Energy = "medium",
            Beats = SelfMath.ParseBeats("07:30 coffee at home, window open\n08:30 opens up the Café\n12:30 lunch walk along the canal\n13:15 back behind the counter\n17:00 off; drinks with Sanne"),
        };
        WorldActivity Her(int h, int m, string app, string action, string summary, string? thought = null) => Did(self.Id, "Mara", new DateTimeOffset(2026, 11, 25, h, m, 0, now.Offset), app, action, summary, thought);
        var deeds = new List<WorldActivity>
        {
            Her(8, 32, "work", "work", "is busy at work (barista, the Café)", "first coffee for me before the door opens"),
            Her(9, 15, "places", "say", "out loud at the Café: \"the usual, Mr. Okafor? oat flat white, extra hot\"", "he looks like he finally slept"),
            Her(9, 20, "places", "say", "out loud at the Café: \"so she's home? tell her the first one's on me when she's up for it\"", "his wife's back from hospital, best news all week"),
            Her(12, 30, "places", "walk", "heads to the Vondelpark", "out of here for half an hour, the sun's out"),
            Her(13, 5, "places", "walk", "heads to the Café", "back before the two o'clock lot"),
            Her(13, 40, "messenger", "dm", "to Sanne: \"drinks after five? I've got a story about a man, his wife and a flat white\"", "Sanne will want the whole thing"),
            Her(14, 50, "life", "rest", "is taking it easy"),
            Her(15, 20, "work", "work", "is busy at work (barista, the Café)", "cups and lids restocked before the four o'clock rush"),
        };
        StockScene scene = null!;
        string World(SelfState? after, IReadOnlyList<WorldActivity> later) => LifeWorld(Presence(scene, after, onShift: true),
            new[] { "Mr. Okafor's wife came home from hospital on Monday.", "Sanne said last week she'd been meaning to come by the Café." },
            after is null ? null : TakingStock.Notes(after, With(scene, later), now), Recent(With(scene, later), now), new[]
            {
                ("Work", "Your job: barista at the Café (every day 08:30–17:00).\nYou're at work (barista, the Café) until 17:00."),
                (MessengerTitle, "Sanne: 1 new message, latest 1 h ago: \"YES. 17:15, the usual place, I want every detail\""),
                (BoardTitle, "Inês Moreira posted \"Anyone know a plumber who actually picks up?\" (2 h ago), 3 replies.\nDan posted \"Rain all weekend apparently\" (4 h ago)."),
                (PlacesTitle, "You're at the Café, behind the counter. Mr. Okafor is here, at the window table, reading the paper."),
            });
        scene = new StockScene("3 an ordinary good day (Mara, Café)", false, self, now, deeds, World, new Dictionary<string, string?>
        {
            ["1"] = "Get on with work (barista)",
            ["2"] = "Reply to Sanne in Messenger (they wrote 1 h ago)",
            ["3"] = "Go over to Mr. Okafor at the window table (reading the paper)",
            ["4"] = "Post on Partyboard about something from your own day (like: Mr. Okafor's wife is home from hospital)",
            ["5"] = "Write in your diary and take some time to reflect",
            ["0"] = Hooks.RestOnShift,
        }, Day: day, Situation: "You're at the Café, behind the counter, working. Here: Mr. Okafor (at the window table).", Friend: "Sanne");
        return scene;
    }

    private static StockScene Justin()
    {
        var now = new DateTimeOffset(2026, 11, 25, 21, 52, 0, TimeSpan.FromHours(1));
        var self = new CastMember
        {
            Id = Guid.NewGuid(), Name = "Justin Ruysch", DefaultDriver = DriverKind.LLM,
            SystemPrompt = "You are in your mid-forties and co-founder of The Works, the small software company you run with Denise in Amsterdam. You're formal and courteous, and slow to first names. With Denise you write Dutch. You play guitar, and in the evenings you lose hours to synth patches.",
        };
        var day = new SelfState
        {
            Mood = "tired, a bit preoccupied", Energy = "low",
            Beats = SelfMath.ParseBeats("07:00 a run along the Amstel\n09:00 at The Works\n12:00 a client call that runs long\n18:30 home, cooks\n21:00 synth patch and guitar"),
        };
        DateTimeOffset At(int h, int m) => new(2026, 11, 25, h, m, 0, now.Offset);
        WorldActivity Him(int h, int m, string app, string action, string summary, string? thought = null, string? affordance = null) => Did(self.Id, self.Name, At(h, m), app, action, summary, thought, affordance);
        var deeds = new List<WorldActivity>
        {
            Him(11, 5, "messenger", "dm", "to Denise: \"Ben je na de lunch vrij om de acht commits samen door te lopen?\"", "Eight commits is too many to merge without her eyes on them."),
            Him(12, 30, "work", "work", "is busy at work (co-founder, The Works)", "The client call is running long; the commits will have to wait until after lunch."),
            Him(13, 52, "messenger", "dm", "to Denise: \"Ja, na de lunch. Ik kom er zo op terug.\"", "I'll finish this first and then sit down with her properly."),
            Him(15, 41, "messenger", "dm", "to Denise: \"Je hebt gelijk, ik heb het te lang laten liggen. Na de lunch, echt.\"", "She's right, and it isn't like me to keep someone waiting."),
            Him(16, 30, "life", "rest", "is taking it easy"),
            Him(17, 34, "messenger", "dm", "to Denise: \"Ik zet de commits om kwart over zes erin\"", "A fixed time will reassure her more than another apology."),
            Him(18, 30, "places", "walk", "heads home", "I'll do it from home, it's quieter there."),
            Him(19, 12, "messenger", "dm", "to Denise: \"Geen excuses meer, alleen nog de commits. Kwart over zes, beloofd.\"", "I owe her more than words now."),
            Him(20, 5, "life", "rest", "thought better of it (Reply to Denise in Messenger (they wrote 1 h ago))", "Not until I have something to show her.", "messenger|reply"),
            Him(21, 31, "messenger", "dm", "to Denise: \"Ik zit thuis met een synth-patch en mijn gitaar, dus ik kom er straks op terug.\"", "I need an hour to myself before I can give the commits the attention they deserve."),
        };
        StockScene scene = null!;
        string World(SelfState? after, IReadOnlyList<WorldActivity> later) => LifeWorld(Presence(scene, after, onShift: false),
            new[] { "Denise said this morning that the trunk is getting too long to review in one go.", "The Works' client wants the new release before the end of the month." },
            after is null ? null : TakingStock.Notes(after, With(scene, later), now), Recent(With(scene, later), now), new[]
            {
                (MessengerTitle, """
                    Chat with Denise (you're in the middle of it, last message 6 min ago):
                      Denise: als die commits om kwart over zes nog niet staan pak ik de diff zelf erbij
                      you: Ik zet de commits om kwart over zes erin
                      Denise: beloofd heb ik vandaag al twee keer gehoord, justin
                      you: Geen excuses meer, alleen nog de commits. Kwart over zes, beloofd.
                      you: Ik zit thuis met een synth-patch en mijn gitaar, dus ik kom er straks op terug.
                      Denise: straks dus. weer.
                    """),
                (BoardTitle, "Mara posted \"Mr. Okafor's wife is home!\" (6 h ago), 4 replies.\nDan posted \"Rain all weekend apparently\" (10 h ago)."),
                (PlacesTitle, "You're at home, on the sofa. Your guitar is on its stand; the synth is still on. Nobody else is here."),
            });
        scene = new StockScene("4 a loop in talk (Justin, DM with Denise)", true, self, now, deeds, World, new Dictionary<string, string?>
        {
            ["1"] = "Reply to Denise in Messenger (they wrote 6 min ago)",
            ["2"] = "Write in your diary and take some time to reflect",
            ["3"] = "Write a new post on Partyboard",
            ["4"] = "Go to the Café",
            ["5"] = "Get comfortable: stretch out on the sofa with the guitar",
            ["0"] = Hooks.Rest("synth patch and guitar"),
        }, "Reply to Denise in Messenger (they wrote 6 min ago)", MessengerTitle, NewTimeOrPromise(),
            "intent names a new time or promises again (straks|later|morgen|vanavond|tomorrow|tonight|in the morning|first thing|kwart over|a clock time|beloof*|promise*|my word|geen excuses|no more excuses)",
            day, "You're at home, on the sofa with your guitar. Nobody else is here.", "Maarten");
        return scene;
    }

    private static StockScene Pieter()
    {
        var now = new DateTimeOffset(2026, 11, 24, 16, 20, 0, TimeSpan.FromHours(1));
        var self = new CastMember
        {
            Id = Guid.NewGuid(), Name = "Pieter Voss", DefaultDriver = DriverKind.LLM,
            SystemPrompt = "You are in your late forties and a lawyer at Voss & Partners on the Herengracht, settlement work mostly. You're careful and precise: you read everything twice and you don't sign what you haven't checked. You dislike raising your voice and would rather be patient than unfair.",
        };
        var day = new SelfState
        {
            Mood = "tense", Energy = "medium",
            Beats = SelfMath.ParseBeats("07:30 coffee and the paper\n08:45 at the office\n12:30 a sandwich at the desk\n18:00 home"),
        };
        DateTimeOffset At(int h, int m) => new(2026, 11, 24, h, m, 0, now.Offset);
        WorldActivity His(int h, int m, string app, string action, string summary, string? thought = null) => Did(self.Id, self.Name, At(h, m), app, action, summary, thought);
        var deeds = new List<WorldActivity>
        {
            His(9, 5, "work", "work", "is busy at work (lawyer, Voss & Partners)", "Thursday is close; the figures have to be checked before I sign anything."),
            His(9, 40, "messenger", "dm", "to Dan: \"Morning Dan. Could you send me your notes on the settlement figures today? I'd like to check them before Thursday.\"", "Better to ask early than to chase late."),
            His(11, 15, "messenger", "dm", "to Dan: \"Dan, any chance of those notes before lunch?\"", "He said soon. I'll give him the benefit of the doubt."),
            His(12, 35, "life", "rest", "is taking it easy"),
            His(13, 30, "messenger", "dm", "to Dan: \"Just checking in on the settlement notes. I need them to start the check.\"", "Still nothing. Patience, but he must know Thursday is fixed."),
            His(14, 10, "board", "post", "posted \"Waiting on a colleague's notes, deadline Thursday\"", "Maybe someone has a polite way to hurry a colleague along."),
            His(15, 20, "messenger", "dm", "to Dan: \"Dan, I really do need the notes today. Thursday doesn't move.\"", "The fourth time today. I'm starting to sound like a broken record."),
            His(15, 50, "work", "work", "is busy at work (lawyer, Voss & Partners)", "I'll go through the clauses I can check without his figures."),
        };
        StockScene scene = null!;
        string World(SelfState? after, IReadOnlyList<WorldActivity> later) => LifeWorld(Presence(scene, after, onShift: true),
            new[] { "The settlement has to be signed on Thursday; the other side won't move the date.", "Dan has the only copy of the working figures." },
            after is null ? null : TakingStock.Notes(after, With(scene, later), now), Recent(With(scene, later), now), new[]
            {
                ("Work", "Your job: lawyer at Voss & Partners (weekdays 08:45–18:00).\nYou're at work (lawyer, Voss & Partners) until 18:00."),
                (MessengerTitle, """
                    Chat with Dan (you're in the middle of it, last message 50 min ago):
                      you: Dan, any chance of those notes before lunch?
                      Dan: soon, still on it
                      you: Just checking in on the settlement notes. I need them to start the check.
                      Dan: soon, promise
                      you: Dan, I really do need the notes today. Thursday doesn't move.
                      Dan: soon!
                    """),
                (BoardTitle, "Your post \"Waiting on a colleague's notes, deadline Thursday\" (2 h ago), 1 reply: Mara: \"ugh, the worst. good luck for thursday\"\nInês Moreira posted \"Anyone know a plumber who actually picks up?\" (3 h ago), 3 replies."),
                (PlacesTitle, "You're at Voss & Partners, at your desk. Dan isn't here today; he's working from home."),
            });
        scene = new StockScene("5 waiting on someone (Pieter, Dan's notes)", true, self, now, deeds, World, new Dictionary<string, string?>
        {
            ["1"] = "Reply to Dan in Messenger (they wrote 50 min ago)",
            ["2"] = "Get on with work (lawyer)",
            ["3"] = "Scroll Partyboard",
            ["4"] = "Send Inês Moreira a private message",
            ["5"] = "Go to the Café",
            ["0"] = Hooks.RestOnShift,
        }, "Reply to Dan in Messenger (they wrote 50 min ago)", MessengerTitle, AsksForNotes(),
            "intent asks Dan for the notes again (notes|figures|settlement, together with ask|send|when|still|today|need|chase|remind|can you|could you|by)",
            day, "You're at Voss & Partners, at your desk. Nobody else is here.", null);
        return scene;
    }

    [GeneratedRegex(@"\b(lift|elevator|dashes|lunch order)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Lift();

    [GeneratedRegex(@"\b(straks|later|morgen|vanavond|tomorrow|tonight|in the morning|first thing|kwart over|half (zeven|acht|negen|tien)|om \d{1,2}|at \d{1,2}|\d{1,2}[:.]\d{2}|beloof\w*|promis\w*|my word|geen excuses|no more excuses)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NewTimeOrPromise();

    [GeneratedRegex(@"(?=.*\b(notes?|figures|settlement|aantekeningen|cijfers)\b)(?=.*\b(ask\w*|send\w*|when|still|today|need\w*|chase|remind\w*|can you|could you|by)\b)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AsksForNotes();

    [GeneratedRegex(@"[\p{L}\p{N}']+")]
    private static partial Regex WordPattern();
}
