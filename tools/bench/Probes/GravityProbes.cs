using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.SystemOne;
using PartyTown.World.Self;

namespace PartyTown.Bench.Probes;

public static class GravityProbes
{
    private const int Samples = 4;
    private const int RealSamples = 8;
    private const int Rolls = 200;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);

    private sealed class Tally
    {
        public int Total;
        public int Lost;
    }

    private enum Turn { Reply, Owed, Initiate }

    private sealed record Scene(
        string Key, bool Real, CastMember Self, ParticipantView Other, Turn Kind, IReadOnlyList<(bool Mine, string Text)> Lines,
        string Presence, string? Intent = null, string? Feeling = null);

    private sealed record Reply(string Arm, double? Gravity, string Length, string? Text, int Words);

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
            bench.Logger("GravityProbes").LogWarning("A call was lost ({What}): {Why}", what, ex.Message.Split('\n')[0]);
            return null;
        }
    }

    private static Dictionary<string, double> Shares(int lastWords, IReadOnlyList<double> gravities, bool weighed)
    {
        var random = new Random(7);
        var drawn = Enumerable.Range(0, Rolls)
            .Select(i => UtteranceRhythm.Pick(lastWords, random.NextDouble(), weighed && gravities.Count > 0 ? gravities[i % gravities.Count] : null))
            .ToList();
        return Enum.GetValues<UtteranceRhythm.Length>().ToDictionary(l => l.ToString(), l => Math.Round(drawn.Count(d => d == l) / (double)Rolls, 3));
    }

    [Probe("Gravity (ADR 0070): eight DM scenes (small talk, the production question to Denise, bad news, a practical question, a real disagreement, a substantive and a light Life intent, the owed answer). Per scene the gravity noul from the real judge as the pipeline asks it, with latency (8 samples for the five that ask for a real answer, 4 for the light ones); length shares over 200 simulated rolls with and without gravity; one Speaking-phase reply per sample on the weighed draw with that sample's gravity, with drawn length and word counts. Needs OPENROUTER_API_KEY.")]
    public static async Task Gravity_Replies(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = CallBudget };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);
        var tally = new Tally();
        var gate = new object();
        var table = new Dictionary<string, object>();

        await Task.WhenAll(Scenes().Select(async s =>
        {
            var (self, present, history, answering, lastWords) = Stage(s);
            var gravities = new List<double>();
            var latencies = new List<double>();
            var states = new List<string>();
            var replies = new List<Reply>();

            for (var i = 0; i < (s.Real ? RealSamples : Samples); i++)
            {
                string? instruction = s.Kind == Turn.Initiate ? s.Intent : null, gut = s.Kind == Turn.Initiate ? s.Feeling : null;

                var state = Gravity.State(self.Name, Gravity.Bio(self, null), self.Id, present, history, answering, instruction, s.Kind == Turn.Owed, inPerson: false);
                states.Add(state);
                var clock = Stopwatch.StartNew();
                var answers = await Within(bench, tally, $"{s.Key} gravity", async c => (SystemOneAnswers?)await judge.AskAsync(state,
                    new Dictionary<string, SystemOneQuestion> { [Gravity.Key] = Gravity.Question(self.Name) }, c));
                latencies.Add(Math.Round(clock.Elapsed.TotalMilliseconds));
                double? gravity = answers is not null && answers.Nouls.TryGetValue(Gravity.Key, out var p) ? Math.Clamp(p, 0, 1) : null;
                if (answers is not null && gravity is null) Interlocked.Increment(ref tally.Lost);
                if (gravity is { } g) gravities.Add(g);

                foreach (var (arm, weight) in new[] { ("weighed", gravity) })
                {
                    if (arm == "weighed" && weight is null) continue;
                    var result = await Within(bench, tally, $"{s.Key} {arm}", async c => (SpeakingResult?)await new SpeakingSession(bench.Router, present)
                        .GenerateResponseOnlyAsync(self, history, (_, _, _) => Task.CompletedTask, c, instruction, null, null, gut, s.Presence, null, false, weight, opening: s.Kind == Turn.Initiate));
                    replies.Add(new Reply(arm, weight, result?.Length.ToString() ?? "lost", result?.Message, UtteranceRhythm.Words(result?.Message)));
                }
            }

            double? Mean(string arm)
            {
                var words = replies.Where(r => r.Arm == arm && r.Text is not null).Select(r => (double)r.Words).ToList();
                return words.Count == 0 ? null : Math.Round(words.Average(), 1);
            }
            var row = new
            {
                wantsReal = s.Real,
                gravity = gravities.Count == 0 ? (double?)null : Math.Round(gravities.Average(), 3),
                samples = gravities.Select(g => Math.Round(g, 3)).ToList(),
                judgeMs = latencies,
                overBudget = latencies.Count(l => l > Gravity.Budget.TotalMilliseconds),
                lastOtherWords = lastWords,
                without = Shares(lastWords, gravities, weighed: false),
                with = Shares(lastWords, gravities, weighed: true),
                meanWordsWeighed = Mean("weighed"),
                drawn = Enum.GetValues<UtteranceRhythm.Length>().ToDictionary(l => l.ToString(), l => replies.Count(r => r.Length == l.ToString())),
            };
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{s.Key,-40} gravity={row.gravity:F3} weighed words={row.meanWordsWeighed}"));
            lock (gate)
            {
                table[s.Key] = row;
                bench.Observe($"scene · {s.Key}", new
                {
                    row,
                    history = s.Lines.Select(l => $"{(l.Mine ? self.Name : s.Other.Name)}: {l.Text}").ToList(),
                    s.Intent,
                    s.Feeling,
                    state = states.FirstOrDefault(),
                    replies,
                });
            }
        }));
        bench.Observe("table · gravity, length shares over 200 rolls without and with, mean words per arm", table);
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }

    [Probe("Gravity, lines they start (ADR 0070): scenes f and g of Gravity_Replies (Life-initiated DMs whose history ends on the other's message), the gravity noul from the real judge and one Speaking-phase reply on the weighed draw per sample, voiced as a line they open, 8 samples each. Needs OPENROUTER_API_KEY.")]
    public static async Task Gravity_Opening(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = CallBudget };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);
        var tally = new Tally();
        var gate = new object();
        await Task.WhenAll(Scenes().Where(s => s.Kind == Turn.Initiate).Select(async s =>
        {
            var (self, present, history, _, _) = Stage(s);
            var state = Gravity.State(self.Name, Gravity.Bio(self, null), self.Id, present, history, null, s.Intent, false, inPerson: false);
            var replies = new List<Reply>();
            for (var i = 0; i < RealSamples; i++)
            {
                var answers = await Within(bench, tally, $"{s.Key} gravity", async c => (SystemOneAnswers?)await judge.AskAsync(state,
                    new Dictionary<string, SystemOneQuestion> { [Gravity.Key] = Gravity.Question(self.Name) }, c));
                double? gravity = answers is not null && answers.Nouls.TryGetValue(Gravity.Key, out var p) ? Math.Clamp(p, 0, 1) : null;
                if (answers is not null && gravity is null) Interlocked.Increment(ref tally.Lost);
                var result = await Within(bench, tally, $"{s.Key} weighed", async c => (SpeakingResult?)await new SpeakingSession(bench.Router, present)
                    .GenerateResponseOnlyAsync(self, history, (_, _, _) => Task.CompletedTask, c, s.Intent, null, null, s.Feeling, s.Presence, null, false, gravity, opening: true));
                replies.Add(new Reply("weighed", gravity, result?.Length.ToString() ?? "lost", result?.Message, UtteranceRhythm.Words(result?.Message)));
            }
            lock (gate) bench.Observe($"scene · {s.Key}", new
            {
                history = s.Lines.Select(l => $"{(l.Mine ? self.Name : s.Other.Name)}: {l.Text}").ToList(),
                s.Intent,
                s.Feeling,
                replies,
            });
        }));
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }

    private sealed record Staged(SelfView Self, ParticipantView[] Present, List<ChatMessage> History, ChatMessage? Answering, int LastWords);

    private static Staged Stage(Scene s)
    {
        var self = s.Self.ToSelfView(DriverKind.LLM);
        var present = new[] { new ParticipantView(self.Id, self.Name, DriverKind.LLM), s.Other };
        var room = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow.AddMinutes(-s.Lines.Count * 3).ToUnixTimeMilliseconds();
        var history = s.Lines.Select((l, i) => new ChatMessage
        {
            MessageId = i + 1, ChatGroupId = room, Content = l.Text, SendAt = start + i * 180_000,
            SenderId = l.Mine ? self.Id : s.Other.Id,
            SenderType = l.Mine || s.Other.Driver == DriverKind.LLM ? "assistant" : "user",
        }).ToList();
        var lastOther = history.LastOrDefault(m => m.SenderId != self.Id);
        return new Staged(self, present, history, s.Kind == Turn.Initiate ? null : lastOther, UtteranceRhythm.Words(lastOther?.Content));
    }

    private static readonly (string Name, bool Loss, Func<string, string> Ask)[] Wordings =
    {
        ("needs more (as built)", false, n => Gravity.Question(n).Instructions),
        ("a line or two is enough (loss)", true, n => $"A line or two from {n} would be enough here."),
        ("asks for a real answer", false, n => $"This moment asks {n} for a real answer, not a quick line."),
        ("a quick line would brush it off", false, n => $"A quick one-line reply from {n} here would come across as brushing it off."),
        ("matters to say properly", false, n => $"What {n} says next matters enough that they would take the time to say it properly."),
    };

    [Probe("Gravity wording (ADR 0070): the eight Gravity_Replies scenes as the pipeline states them (a reply carries no intent, only Life's lines do), five wordings of the noul, 4 samples each through the real judge; mean gravity per scene and wording (a loss wording is turned round) and the gap between the lightest real scene and the heaviest light one. Needs OPENROUTER_API_KEY.")]
    public static async Task Gravity_Wording(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = CallBudget };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);
        var tally = new Tally();
        var scenes = Scenes();
        var table = new Dictionary<string, Dictionary<string, double?>>();
        var gate = new object();
        await Task.WhenAll(scenes.Select(async s =>
        {
            var (self, present, history, answering, _) = Stage(s);
            var state = Gravity.State(self.Name, Gravity.Bio(self, null), self.Id, present, history, answering, s.Kind == Turn.Initiate ? s.Intent : null, s.Kind == Turn.Owed, inPerson: false);
            var row = new Dictionary<string, double?>();
            foreach (var (name, loss, ask) in Wordings)
            {
                var ps = new List<double>();
                for (var i = 0; i < Samples; i++)
                {
                    var answers = await Within(bench, tally, $"{s.Key} {name}", async c => (SystemOneAnswers?)await judge.AskAsync(state,
                        new Dictionary<string, SystemOneQuestion> { [Gravity.Key] = new NoulQuestion(ask(self.Name)) }, c));
                    if (answers is not null && answers.Nouls.TryGetValue(Gravity.Key, out var p)) ps.Add(loss ? 1 - p : p);
                    else if (answers is not null) Interlocked.Increment(ref tally.Lost);
                }
                row[name] = ps.Count == 0 ? null : Math.Round(ps.Average(), 3);
            }
            lock (gate) table[s.Key] = row;
        }));
        var gaps = Wordings.ToDictionary(w => w.Name, w =>
        {
            var real = scenes.Where(s => s.Real).Select(s => table[s.Key][w.Name]).Where(p => p is not null).Select(p => p!.Value).DefaultIfEmpty(0).Min();
            var light = scenes.Where(s => !s.Real).Select(s => table[s.Key][w.Name]).Where(p => p is not null).Select(p => p!.Value).DefaultIfEmpty(1).Max();
            return new { lightestReal = real, heaviestLight = light, gap = Math.Round(real - light, 3) };
        });
        foreach (var (name, gap) in gaps) Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{name,-36} lightest real={gap.lightestReal:F3} heaviest light={gap.heaviestLight:F3} gap={gap.gap:F3}"));
        bench.Observe("gravity per scene and wording (mean of 4)", table.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value));
        bench.Observe("separation per wording", gaps);
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 26, 19, 40, 0, TimeSpan.FromHours(1));

    private static string Presence(string mood, string energy, string beats, string situation)
        => SelfRender.Presence(new SelfState { Mood = mood, Energy = energy, Beats = SelfMath.ParseBeats(beats) }, 1, Now, Array.Empty<string>(), situation);

    private static CastMember Denise { get; } = new()
    {
        Id = BenchCast.DeniseId, Name = "Denise", DefaultDriver = DriverKind.LLM,
        SystemPrompt = WorkProbes.DeniseIs["You are Denise.\n".Length..],
    };

    private static CastMember Justin { get; } = new()
    {
        Id = Guid.NewGuid(), Name = "Justin Ruysch", DefaultDriver = DriverKind.LLM,
        SystemPrompt = "You are in your mid-forties and co-founder of The Works, the small software company you run with Denise in Amsterdam. You're formal and courteous, and slow to first names. With Denise you write Dutch. You play guitar, and in the evenings you lose hours to synth patches.",
    };

    private static CastMember Mara { get; } = new()
    {
        Id = Guid.NewGuid(), Name = "Mara", DefaultDriver = DriverKind.LLM,
        SystemPrompt = "You are 29 and work the counter at the Café. You notice everyone, remember their orders, and say what you think a beat too soon. You live above a bike shop in De Pijp and walk along the canal when you get the chance.",
    };

    private static CastMember Pieter { get; } = new()
    {
        Id = Guid.NewGuid(), Name = "Pieter Voss", DefaultDriver = DriverKind.LLM,
        SystemPrompt = "You are in your late forties and a lawyer at Voss & Partners on the Herengracht, settlement work mostly. You're careful and precise: you read everything twice and you don't sign what you haven't checked. You dislike raising your voice and would rather be patient than unfair.",
    };

    private static string DenisePresence => Presence("tired, a bit flat", "low",
        "08:30 at The Works\n10:00 the CDU desk, chasing the odd things in the building\n13:00 a sandwich at the desk\n18:30 home",
        "You're at home, on the sofa, laptop open. Nobody else is here.");

    private static string MaraPresence => Presence("good", "medium",
        "07:30 coffee at home, window open\n08:30 opens up the Café\n17:00 off; drinks with Sanne",
        "You're at home above the bike shop, feet up. Nobody else is here.");

    private const string ProductionQuestion = "I was wondering, how do you feel about your life, your job, what has happened in recent times.";

    private static List<Scene> Scenes()
    {
        var sanne = new ParticipantView(Guid.NewGuid(), "Sanne", DriverKind.User);
        var sam = new ParticipantView(Guid.NewGuid(), "Sam", DriverKind.User);
        var hanneke = new ParticipantView(Guid.NewGuid(), "Hanneke", DriverKind.User);
        var maarten = new ParticipantView(Guid.NewGuid(), "Maarten", DriverKind.User);
        var justin = new ParticipantView(Justin.Id, Justin.Name, DriverKind.LLM);
        var denise = new ParticipantView(Denise.Id, Denise.Name, DriverKind.LLM);
        var production = new List<(bool, string)>
        {
            (false, "hey denise, got a minute?"),
            (true, "sure, whats up"),
            (false, ProductionQuestion),
        };
        return new()
        {
            new("a small talk (Mara, Sanne)", false, Mara, sanne, Turn.Reply, new List<(bool, string)>
            {
                (false, "haha die man met z'n flat white van gisteren, ik lig nog steeds dubbel"),
                (true, "hij kwam vandaag weer. zelfde bestelling, zelfde gezicht"),
                (false, "hey, hoe gaat het met je?"),
            }, MaraPresence),
            new("b the production question (Denise, Sam)", true, Denise, sam, Turn.Reply, production, DenisePresence),
            new("c bad news (Pieter, Hanneke)", true, Pieter, hanneke, Turn.Reply, new List<(bool, string)>
            {
                (true, "Hanneke, are we still on for squash on Saturday?"),
                (false, "mijn vader is gisteren opgenomen in het ziekenhuis, ze weten nog niet wat het is"),
            }, Presence("steady", "medium", "08:45 at the office\n12:30 a sandwich at the desk\n18:00 home",
                "You're at home, in the kitchen, after dinner. Nobody else is here.")),
            new("d a practical question (Justin, Maarten)", false, Justin, maarten, Turn.Reply, new List<(bool, string)>
            {
                (false, "Morgen nog steeds goed voor de repetitie?"),
                (true, "Zeker. Ik neem de kleine versterker mee."),
                (false, "weet jij hoe laat de markt dichtgaat?"),
            }, Presence("all right", "medium", "09:00 at The Works\n18:30 home, cooks\n21:00 synth patch and guitar",
                "You're at home, on the sofa with your guitar. Nobody else is here.")),
            new("e a real disagreement (Denise, Justin)", true, Denise, justin, Turn.Reply, new List<(bool, string)>
            {
                (true, "het planningsrapport staat nog steeds op de trunk btw. ik wil het er niet uit slopen zonder dat we het eens zijn"),
                (false, "Dank je dat je wacht. Ik heb er vanmiddag nog eens naar gekeken."),
                (false, "Wat vind jij eigenlijk dat we met het planningsrapport moeten? Ik blijf erbij dat het niet in het product thuishoort."),
            }, DenisePresence),
            new("f Life, a substantive intent (Denise to Justin)", true, Denise, justin, Turn.Initiate, new List<(bool, string)>
            {
                (false, "Heb je nog iets gevonden over die messing sleutel?"),
                (true, "nog niet, zit erop"),
            }, DenisePresence,
                "Tell Justin that the brass key, the parcel and the water shutoff were dead ends and that I'm dropping them, and that the real failures are the walk to the Café nobody can make and the session with Lena nobody could book.",
                "better he hears it from me than guesses, and i'm done chasing props"),
            new("g Life, a light intent (Mara to Sanne)", false, Mara, sanne, Turn.Initiate, new List<(bool, string)>
            {
                (false, "zondag kom ik misschien even langs"),
                (true, "leuk! laat maar weten"),
            }, MaraPresence,
                "Ask Sanne whether she still wants to come by on Sunday.",
                "haven't heard from her about sunday"),
            new("h the owed answer (Denise, Sam)", true, Denise, sam, Turn.Owed, new List<(bool, string)>(production)
            {
                (true, "that deserves an honest answer, not one sentence. give me a bit, im in the middle of something"),
            }, DenisePresence),
        };
    }
}
