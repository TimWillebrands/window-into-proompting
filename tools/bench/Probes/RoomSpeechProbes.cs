using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Reflex;
using PartyTown.World.Self;

namespace PartyTown.Bench.Probes;

public static class RoomSpeechProbes
{
    private const int Samples = 4;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(60);
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 9, 40, 0, TimeSpan.FromHours(1));
    private static readonly SemaphoreSlim Lanes = new(12);

    private sealed class Tally
    {
        public int Total;
        public int Lost;
    }

    private sealed record Member(SelfView Self, string Doing, double Load, string Mood, string Situation, string PlaceSituation, string PlaceDoing, double PlaceLoad);

    private sealed record Said(Guid From, string Text, double MinutesAgo);

    private sealed record Scene(
        string Key, bool Place, IReadOnlyList<Said> Lines, string? Cue, bool WindingDown,
        IReadOnlyDictionary<Guid, IReadOnlyList<RecalledMemory>> Memories);

    private sealed class Turn
    {
        public string Persona = "";
        public string Kind = "reply";
        public double Urge;
        public bool Skipped;
        public string? Reflex;
        public bool Torn;
        public Dictionary<string, double>? Shaped;
        public bool? MayPass;
        public bool? Passed;
        public int? Memory;
        public string? MemorySnippet;
        public bool AutoDeployed;
        public Dictionary<string, double>? RecallOdds;
        public double? Gravity;
        public string? Length;
        public string? Line;
        public int Words;
        public int LlmCalls;
        public int JudgeCalls;
        public long ReflexMs;
        public long LagMs;
        public long CheapMs;
        public long VoiceMs;
        public string? Timed;
    }

    private static readonly ParticipantView Sam = BenchCast.Sam;
    private static readonly ParticipantView Ruben = new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Ruben", DriverKind.LLM);

    private static readonly SelfView Vlad = BenchCast.Vlad;
    private static readonly SelfView Denise = BenchCast.Denise;

    private static readonly SelfView Mara = new(
        Guid.Parse("55555555-5555-5555-5555-555555555555"), "Mara", DriverKind.LLM, null,
        "You are 29 and work the counter at the Café. You notice everyone, remember their orders, and say what you think a beat too soon. You live above a bike shop in De Pijp and walk along the canal when you get the chance.",
        0.6, 0.7);

    private static readonly SelfView Pieter = new(
        Guid.Parse("66666666-6666-6666-6666-666666666666"), "Pieter Voss", DriverKind.LLM, null,
        "You are in your late forties and a lawyer at Voss & Partners on the Herengracht, settlement work mostly. You're careful and precise: you read everything twice and you don't sign what you haven't checked. You dislike raising your voice and would rather be patient than unfair.",
        0.35, 0.2);

    private static readonly Member[] Cast =
    {
        new(Vlad, "reading at home with the curtains drawn", 0.1, "dry", "You're at home, curtains drawn, a book open. Nobody else is here.",
            "You're at the Café, at the table by the window with Denise and Pieter. Mara is behind the counter.", "sitting at a table at the Café", 0.1),
        new(Denise, "at her desk at the agency, between calls", 0.2, "upbeat", "You're at your desk at the agency, between calls.",
            "You're at the Café, at the table by the window with Vlad and Pieter. Mara is behind the counter.", "sitting at a table at the Café", 0.1),
        new(Mara, "working the counter at the Café", 0.3, "good", "You're behind the counter at the Café, a slow moment between orders.",
            "You're behind the counter at the Café, a slow moment. Vlad, Denise and Pieter are at the table by the window.", "working the counter at the Café", 0.35),
        new(Pieter, "at his desk at the firm, a file open", 0.25, "steady", "You're at your desk at Voss & Partners, a file open.",
            "You're at the Café, at the table by the window with Vlad and Denise. Mara is behind the counter.", "sitting at a table at the Café", 0.1),
    };

    private static RecalledMemory Mem(string snippet, double salience, RecallArm arm, int recallCount = 0, double weight = 0.6)
        => new(Guid.NewGuid(), snippet, weight, recallCount, null, Now.AddDays(-12), salience, arm);

    private static IReadOnlyList<RecalledMemory> PieterRemembers() => new[]
    {
        Mem("Mara told you last month that her sister's landlord in Utrecht wants her out over the dog, and you offered to read the lease if it came to that.", 0.72, RecallArm.Relevant),
        Mem("Mara talked you into the oat flat white; you drank half and said nothing.", 0.66, RecallArm.Relevant, 2),
        Mem("You settled the Brouwer estate on the Herengracht after eleven months; nobody thanked anyone.", 0.61, RecallArm.Recent),
        Mem("Denise booked the wrong room for the firm's drinks and turned it into a better evening than planned.", 0.58, RecallArm.Relevant, 1),
        Mem("Hanneke beat you at squash three games to one and wouldn't stop grinning.", 0.55, RecallArm.Recent),
        Mem("Vlad told you he hasn't seen a sunrise on purpose since 1890.", 0.52, RecallArm.Relevant, 1),
        Mem("You reread a settlement draft at midnight and found the comma that would have cost the client a house.", 0.49, RecallArm.Recent),
        Mem("Justin played you a synth patch he called 'rain on a tram' and you liked it more than you said.", 0.45, RecallArm.Recent),
        Mem("The coffee machine at the office broke and you walked to the Café in the rain.", 0.41, RecallArm.Recent),
        Mem("You lent Denise your umbrella and haven't seen it since.", 0.38, RecallArm.Relevant, 3),
    };

    private static IReadOnlyList<RecalledMemory> NothingMuch(string who) => who switch
    {
        "Vlad" => new[]
        {
            Mem("Denise once tried to throw you a surprise party at noon.", 0.63, RecallArm.Relevant, 1),
            Mem("You reread Dracula last winter and found it sentimental.", 0.5, RecallArm.Recent),
            Mem("Pieter asked you what you did for a living and you said 'nights'.", 0.44, RecallArm.Relevant, 2),
        },
        _ => new[]
        {
            Mem("You planned a wedding on a boat and the boat was late.", 0.6, RecallArm.Recent),
            Mem("Vlad sent you a single full stop as a reply to your party invite.", 0.52, RecallArm.Relevant, 1),
            Mem("Pieter read your venue contract twice and found the cancellation clause.", 0.47, RecallArm.Relevant, 2),
        },
    };

    private static readonly ThermostatRead Fizzling = new(0, 0.3, 0.8, 0, true);

    private static List<Scene> Scenes()
    {
        var none = new Dictionary<Guid, IReadOnlyList<RecalledMemory>>();
        return new()
        {
            new("G1 human small talk in a quiet group", false, new[]
            {
                new Said(Sam.Id, "goedemorgen allemaal, iemand zin in koffie straks?", 0),
            }, null, false, none),
            new("G2 human big news in the group", false, new[]
            {
                new Said(Sam.Id, "ik heb net mijn baan opgezegd", 0),
            }, null, false, none),
            new("G3 Denise asks Pieter by name", false, new[]
            {
                new Said(Mara.Id, "mijn huisbaas heeft vanochtend een brief gestuurd, huur gaat 10% omhoog per januari", 6),
                new Said(Vlad.Id, "Landlords. Some things never change.", 5),
                new Said(Denise.Id, "Pieter, jij bent toch jurist? Mag dat zomaar, 10% in één keer?", 0),
            }, null, false, none),
            new("G4 two have traded five lines, a banal next one", false, new[]
            {
                new Said(Denise.Id, "heb jij die nieuwe serie al gezien, over dat hotel?", 4),
                new Said(Mara.Id, "ja! twee afleveringen gisteren", 3.5),
                new Said(Denise.Id, "die receptioniste haha", 3),
                new Said(Mara.Id, "ja die is geweldig", 2),
                new Said(Denise.Id, "ik wil ook zo'n hotel runnen", 1.5),
                new Said(Mara.Id, "haha ja, wie niet toch?", 0),
            }, null, false, none),
            new("G5 a fizzling conversation", false, new[]
            {
                new Said(Sam.Id, "zaterdag borrel in de Café? 17:00", 12),
                new Said(Denise.Id, "ja!! ik ben erbij", 11),
                new Said(Mara.Id, "ik ook, ik werk tot 17 dus ik schuif zo aan", 10),
                new Said(Pieter.Id, "Prima, 17:00.", 8),
                new Said(Mara.Id, "top", 2.5),
                new Said(Pieter.Id, "👍", 2),
                new Said(Denise.Id, "dus zaterdag 17:00, toch?", 0),
            }, Thermostat.Cue(Fizzling.HeavyStreak, Fizzling.Circling, Fizzling.Fizzling, Fizzling.Reconfirming), true, none),
            new("G6 a stranger walks up in person", true, new[]
            {
                new Said(Denise.Id, "zullen we straks nog even langs de markt?", 4),
                new Said(Pieter.Id, "Als het droog blijft.", 3.5),
                new Said(Ruben.Id, "sorry dat ik stoor, maar weten jullie of de eenden in de gracht van de gemeente zijn?", 0),
            }, null, false, none),
            new("G7 a line one of them remembers something about", false, new[]
            {
                new Said(Denise.Id, "goedemorgen! wat een weer", 5),
                new Said(Vlad.Id, "Grey. Perfect.", 4),
                new Said(Mara.Id, "mijn zus belde net, haar verhuurder in Utrecht wil haar er nu echt uitzetten vanwege die hond", 0),
            }, null, false, new Dictionary<Guid, IReadOnlyList<RecalledMemory>>
            {
                [Pieter.Id] = PieterRemembers(),
                [Vlad.Id] = NothingMuch("Vlad"),
                [Denise.Id] = NothingMuch("Denise"),
            }),
        };
    }

    private static IReadOnlyList<ParticipantView> Present(Scene s)
    {
        var views = Cast.Select(m => new ParticipantView(m.Self.Id, m.Self.Name, DriverKind.LLM)).ToList();
        views.Add(s.Place ? Ruben : Sam);
        return views;
    }

    private static List<ChatMessage> History(Scene s, Guid room)
    {
        var humans = new HashSet<Guid> { Sam.Id };
        return s.Lines.Select((l, i) => new ChatMessage
        {
            MessageId = i + 1, ChatGroupId = room, Content = l.Text, SenderId = l.From,
            SenderType = humans.Contains(l.From) ? "user" : "assistant",
            SendAt = Now.AddMinutes(-l.MinutesAgo).ToUnixTimeMilliseconds(),
        }).ToList();
    }

    private static string Name(IReadOnlyList<ParticipantView> present, Guid id)
        => present.Where(p => p.Id == id).Select(p => p.Name).FirstOrDefault() ?? "Someone";

    private static Bond BondOf(Scene s, Guid from)
        => from == Sam.Id ? new Bond(20, 0.25, 20) : from == Ruben.Id ? Bond.None : new Bond(40, 0.3, s.Lines.Count > 1 ? 0.1 : 18);

    private static string Presence(Member m, Scene s)
    {
        var day = new SelfState { Mood = m.Mood, Energy = "medium", Beats = SelfMath.ParseBeats("08:00 up, coffee\n09:00 " + (s.Place ? m.PlaceDoing : m.Doing)) };
        var presence = SelfRender.Presence(day, 0.8, Now, Array.Empty<string>(), s.Place ? m.PlaceSituation : m.Situation);
        return string.IsNullOrWhiteSpace(s.Cue) ? presence : $"{presence}\n# The conversation\n{s.Cue}\n";
    }

    private static RoomCast RoomOf(Scene s) => new()
    {
        Kind = s.Place ? ChatGroupKinds.Place : ChatGroupKinds.Group,
        Members = Present(s).Select(p => new CastMember { Id = p.Id, Name = p.Name, DefaultDriver = p.Driver }).ToList(),
    };

    private sealed class Answered(SystemOneAnswers answers) : ISystemOneJudge
    {
        public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct) => Task.FromResult(answers);
    }

    private static async Task<T?> Within<T>(Bench bench, Tally tally, string what, Func<CancellationToken, Task<T?>> call) where T : class
    {
        Interlocked.Increment(ref tally.Total);
        await Lanes.WaitAsync(bench.Cancellation);
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
            bench.Logger("RoomSpeechProbes").LogWarning("A call was lost ({What}): {Why}", what, ex.Message.Split('\n')[0]);
            return null;
        }
        finally
        {
            Lanes.Release();
        }
    }

    private sealed record Ctx(Bench Bench, Tally Tally, ISystemOneJudge Judge, Scene Scene, Guid Room, IReadOnlyList<ParticipantView> Present, List<ChatMessage> History, ChatMessage Trigger);

    private static async Task<Turn> ReplyAsync(Ctx c, Member m, bool answer)
    {
        var self = m.Self;
        var t = new Turn { Persona = self.Name, Kind = answer ? "owed" : "reply" };
        var rounds = UrgeMath.CountExchangeRounds(c.History);
        var urge = UrgeMath.CalculateResponseUrge(self, c.History, rounds);
        t.Urge = Math.Round(urge.Total, 2);
        var named = urge.MentionScore > 0;
        if (!answer)
        {
            var recentSelf = UrgeMath.CountRecentSelfMessages(c.History, self.Id);
            if (UrgeMath.IsObviousSkip(urge, rounds, recentSelf))
            {
                t.Skipped = true;
                t.Reflex = "pre-gate skip";
                return t;
            }
            var verdict = await ReflexAsync(c, m, urge, t);
            if (verdict is not null) return verdict;
        }

        var fromHuman = c.Trigger.SenderId == Sam.Id;
        t.MayPass = Passing.Allowed(answer, false, named, fromHuman, t.Torn, c.Scene.WindingDown);
        var memories = c.Scene.Memories.GetValueOrDefault(self.Id) ?? Array.Empty<RecalledMemory>();
        await SpeakAsync(c, m, t, null, null, memories, Presence(m, c.Scene), answer, opening: false, mayPass: t.MayPass == true);
        return t;
    }

    private static async Task<Turn?> ReflexAsync(Ctx c, Member m, ResponseUrge urge, Turn t)
    {
        var self = m.Self;
        var load = c.Scene.Place ? m.PlaceLoad : m.Load;
        var kind = c.Scene.Place ? PerceptKind.Speech : PerceptKind.Line;
        var spokeLately = c.History.Any(h => h.SenderId == self.Id && h.MessageId != c.Trigger.MessageId
            && Now.ToUnixTimeMilliseconds() - (h.SendAt ?? 0) < ReflexMath.ExchangeHolds.TotalMilliseconds);
        if (spokeLately && !(c.Scene.WindingDown && c.Trigger.SenderId != Sam.Id))
        {
            t.Reflex = "focus (mid-conversation)";
            t.LagMs = (long)ReflexMath.ReplyLag(kind, 0, Random.Shared.NextDouble(), Random.Shared.NextDouble()).TotalMilliseconds;
            return null;
        }
        var percept = ResponsePipeline.PerceptOf(c.Trigger, RoomOf(c.Scene), self.Id, c.History, urge, c.Room, c.Scene.WindingDown ? Fizzling : null);
        var bond = BondOf(c.Scene, c.Trigger.SenderId);
        var sense = new Sense(self.Name, Gravity.Bio(self, null), c.Scene.Place ? m.PlaceDoing : m.Doing, m.Mood, load, 0.8, bond, Array.Empty<string>());
        var strength = ReflexMath.Strength(percept, bond, 0);
        if (!ReflexMath.Noticed(strength, load, Random.Shared.NextDouble()))
        {
            t.Reflex = "didn't notice";
            t.Skipped = true;
            return t;
        }
        var clock = Stopwatch.StartNew();
        t.JudgeCalls++;
        var answers = await Within(c.Bench, c.Tally, $"{c.Scene.Key} {self.Name} reflex", async ct =>
            (SystemOneAnswers?)await c.Judge.AskAsync(ReflexWeighing.State(percept, sense), ReflexWeighing.Ask(percept, self.Name), ct));
        t.ReflexMs = clock.ElapsedMilliseconds;
        if (answers is null || !answers.Choices.TryGetValue(ReflexWeighing.Key, out var raw) || raw.Count == 0)
        {
            t.Reflex = "no view (through)";
            return null;
        }
        var shaped = ReflexMath.Shape(raw, new Landscape(self.Impulsivity, self.Chattiness, percept.FromHuman));
        answers.Choices[ReflexWeighing.Key] = shaped;
        t.Torn = ReflexMath.Torn(shaped);
        t.Shaped = shaped.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2));
        var picked = answers.SampleChoice(ReflexWeighing.Key, 0.8) ?? ReflexOptions.CarryOn;
        t.Reflex = picked;
        switch (picked)
        {
            case ReflexOptions.Now or ReflexOptions.Speak:
                t.LagMs = (long)ReflexMath.ReplyLag(kind, load, Random.Shared.NextDouble(), Random.Shared.NextDouble()).TotalMilliseconds;
                return null;
            case ReflexOptions.Brief:
            case ReflexOptions.Soon when percept.FromHuman:
                t.Kind = "ack";
                await SpeakAsync(c, m, t, ReflexVoice.Ack(sense.Doing), null, Array.Empty<RecalledMemory>(), Presence(m, c.Scene), false, opening: true, mayPass: false);
                return t;
            default:
                t.Skipped = true;
                return t;
        }
    }

    private static async Task SpeakAsync(Ctx c, Member m, Turn t, string? instruction, string? feeling, IReadOnlyList<RecalledMemory> memories, string presence, bool owed, bool opening, bool mayPass)
    {
        var self = m.Self;
        var answering = opening ? null : c.Trigger;
        var state = Gravity.State(self.Name, Gravity.Bio(self, null), self.Id, c.Present, c.History, answering, instruction, owed, c.Scene.Place);
        var snippets = memories.Select(r => r.Snippet).ToList();
        var questions = new Dictionary<string, SystemOneQuestion> { [Gravity.Key] = Gravity.Question(self.Name) };
        if (snippets.Count > 0) questions[Gravity.RecallKey] = Gravity.Recall(self.Name, snippets);
        var clock = Stopwatch.StartNew();
        t.JudgeCalls++;
        var answers = await Within(c.Bench, c.Tally, $"{c.Scene.Key} {self.Name} gravity+memory", async ct => (SystemOneAnswers?)await c.Judge.AskAsync(state, questions, ct));
        t.CheapMs += clock.ElapsedMilliseconds;
        var weighed = answers is null
            ? Gravity.Weighed.Unweighed
            : await Gravity.WeighAsync(new Answered(answers), self.Name, state, snippets, NullLogger.Instance, CancellationToken.None);
        t.Gravity = weighed.Gravity is { } g ? Math.Round(g, 3) : null;
        if (answers is not null && answers.Choices.TryGetValue(Gravity.RecallKey, out var odds))
            t.RecallOdds = odds.OrderByDescending(kv => kv.Value).Take(4).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 3));
        var (picked, auto) = memories.Count == 0 ? (null, false) : RecallDeployment.ResolvePick(weighed, memories);
        t.Memory = picked is null ? null : memories.ToList().IndexOf(picked) + 1;
        t.MemorySnippet = picked?.Snippet;
        t.AutoDeployed = auto;
        var held = ChatAttention.Window(c.History).Held;
        clock.Restart();
        SpeakingResult? result = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            t.LlmCalls++;
            result = await Within(c.Bench, c.Tally, $"{c.Scene.Key} {self.Name} voice", async ct => (SpeakingResult?)await new SpeakingSession(c.Bench.Router, c.Present)
                .GenerateResponseOnlyAsync(self, held, (_, _, _) => Task.CompletedTask, ct, instruction, picked?.Snippet, null, feeling, presence, null, c.Scene.Place, t.Gravity,
                    opening: opening, pass: mayPass ? Passing.Cue : null));
            if (result is not { Passed: true } || mayPass) break;
        }
        t.VoiceMs = clock.ElapsedMilliseconds;
        t.Length = result?.Length.ToString();
        t.Passed = result?.Passed;
        t.Line = result is { Passed: false } && !string.IsNullOrWhiteSpace(result.Message) ? result.Message : null;
        t.Words = UtteranceRhythm.Words(t.Line);
    }

    private static Member Keenest(IEnumerable<(Member M, Turn T)> listeners)
        => listeners.OrderByDescending(l => l.T.Urge).ThenByDescending(l => l.M.Self.Chattiness).First().M;

    private static int TimedSpeakers(List<Turn> turns)
    {
        var landing = turns.Where(t => t.Line is not null && t.Kind == "reply")
            .Select(t => (T: t, Begin: t.ReflexMs + t.LagMs, Land: t.ReflexMs + t.LagMs + t.CheapMs + t.VoiceMs))
            .OrderBy(x => x.Land).ToList();
        var landed = new List<long>();
        foreach (var (t, begin, land) in landing)
        {
            var before = landed.Where(l => l < land).ToList();
            if (before.Any(l => l < begin)) { t.Timed = "read on to a newer line"; continue; }
            if (before.Any(l => l >= begin && l < begin + t.CheapMs)) { t.Timed = "race-cancelled before the voice"; continue; }
            t.Timed = "lands";
            landed.Add(land);
        }
        foreach (var t in turns.Where(t => t.Line is not null && t.Kind != "reply")) t.Timed = "lands";
        return turns.Count(t => t.Timed == "lands");
    }

    [Probe("Room speech, the whole reply path in groups and Places (ADR 0071): seven scenes (a human's small talk and big news in a quiet group, a Persona asking another by name, two who traded five lines and a banal next one, a fizzling conversation with the thermostat's cue, a stranger walking up in person, a line one Persona remembers something about), four Personas, 4 samples (ROOM_SPEECH_SCENES=G1,G5 runs only those). Per Persona: the Urge pre-gate, Reflex through the real judge with the room's facts, the memory System One samples in the gravity call, whether the voice passed, the line verbatim; per sample the speakers, both independent and as the read-on and race timing would let them land, and the owed answer when nobody spoke to a human; LLM and judge calls and wall time per spoken line. Needs OPENROUTER_API_KEY.")]
    public static async Task Room_Speech(Bench bench)
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
        var summary = new Dictionary<string, object>();

        var only = Environment.GetEnvironmentVariable("ROOM_SPEECH_SCENES") is { Length: > 0 } pick
            ? pick.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : null;
        await Task.WhenAll(Scenes().Where(sc => only is null || only.Contains(sc.Key[..2])).Select(async s =>
        {
            var room = Guid.NewGuid();
            var present = Present(s);
            var history = History(s, room);
            var trigger = history[^1];
            var listeners = Cast.Where(m => m.Self.Id != trigger.SenderId).ToList();
            var samples = new List<object>();
            var speakers = new List<int>();
            var timed = new List<int>();
            var allTurns = new List<Turn>();
            var clock = Stopwatch.StartNew();
            for (var i = 0; i < Samples; i++)
            {
                var ctx = new Ctx(bench, tally, judge, s, room, present, history, trigger);
                var turns = (await Task.WhenAll(listeners.Select(m => ReplyAsync(ctx, m, answer: false)))).ToList();
                if (trigger.SenderId == Sam.Id && turns.All(t => t.Line is null && t.Kind != "ack"))
                {
                    var keen = Keenest(listeners.Zip(turns));
                    turns.Add(await ReplyAsync(ctx, keen, answer: true));
                }
                var spoke = turns.Count(t => t.Line is not null);
                speakers.Add(spoke);
                timed.Add(TimedSpeakers(turns));
                allTurns.AddRange(turns);
                samples.Add(new
                {
                    sample = i + 1,
                    speakers = spoke,
                    timedSpeakers = timed[^1],
                    turns = turns.Select(t => new
                    {
                        t.Persona, t.Kind, t.Urge, t.Reflex, t.Torn, t.Shaped, t.MayPass, t.Passed,
                        t.Memory, t.MemorySnippet, t.AutoDeployed, t.RecallOdds, t.Gravity, t.Length, t.Line, t.Words, t.Timed,
                        t.LlmCalls, t.JudgeCalls, ms = new { reflex = t.ReflexMs, lag = t.LagMs, cheap = t.CheapMs, voice = t.VoiceMs },
                    }).ToList(),
                });
            }
            var spokeTurns = allTurns.Where(t => t.Line is not null).ToList();
            var lines = Math.Max(1, spokeTurns.Count);
            var row = new
            {
                speakers,
                timedSpeakers = timed,
                reflex = allTurns.Where(t => t.Kind != "owed").GroupBy(t => t.Persona).ToDictionary(g => g.Key, g => g.Select(t => t.Reflex).ToList()),
                passes = allTurns.Where(t => t.Passed is not null && t.Kind != "ack").GroupBy(t => t.Persona).ToDictionary(g => g.Key, g => $"{g.Count(t => t.Passed == true)}/{g.Count()} passed{(g.Any(t => t.MayPass == false) ? $", {g.Count(t => t.MayPass == false)} not offered" : "")}"),
                memory = allTurns.Where(t => t.Memory is not null).Select(t => $"{t.Persona} #{t.Memory}{(t.AutoDeployed ? " (auto)" : "")}").ToList(),
                owedAnswers = allTurns.Count(t => t.Kind == "owed"),
                llmCallsPerLine = Math.Round(allTurns.Sum(t => t.LlmCalls) / (double)lines, 2),
                judgeCallsPerLine = Math.Round(allTurns.Sum(t => t.JudgeCalls) / (double)lines, 2),
                msPerLine = spokeTurns.Count == 0 ? 0 : Math.Round(spokeTurns.Average(t => (double)(t.ReflexMs + t.CheapMs + t.VoiceMs))),
                lines = spokeTurns.Select(t => $"{t.Persona} ({t.Kind}, {t.Words}w): {t.Line}").ToList(),
            };
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{s.Key,-52} speakers={string.Join(",", speakers)} timed={string.Join(",", timed)} ({clock.Elapsed.TotalSeconds:F0}s)"));
            lock (gate)
            {
                summary[s.Key] = row;
                bench.Observe($"scene · {s.Key}", new
                {
                    row,
                    history = history.Select(h => $"{Name(present, h.SenderId)}: {h.Content}").ToList(),
                    s.Cue,
                    samples,
                });
            }
        }));
        bench.Observe("summary", summary.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value));
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }

    private const int PassSamples = 8;

    private static readonly (string Name, string Cue)[] PassWordings =
    {
        ("as built", Passing.Cue),
        ("you don't have to", "You don't have to say anything. " + Passing.Cue),
        ("silence is an answer", "Saying nothing is an answer too: people let lines go by all the time. " + Passing.Cue),
    };

    [Probe("Pass wording (ADR 0071): who the voice lets a line go by for. Nine Persona-in-scene pairs from Room_Speech (the fizzling G5 for the two in Focus, the banal G4 line for the one in Focus, a human's small talk and big news, the stranger in person), three wordings of the pass cue, 8 Speaking-phase samples each, unweighed draw; per pair and wording the share of passes and the lines.")]
    public static async Task Room_Pass(Bench bench)
    {
        var tally = new Tally();
        var scenes = Scenes().ToDictionary(sc => sc.Key[..2]);
        var pairs = new (string Scene, SelfView Who, string Want)[]
        {
            ("G5", Mara, "let it end"), ("G5", Pieter, "let it end"),
            ("G4", Denise, "either"),
            ("G1", Mara, "answer"), ("G1", Denise, "answer"),
            ("G2", Denise, "answer"), ("G2", Mara, "answer"),
            ("G6", Pieter, "either"), ("G6", Vlad, "either"),
        };
        var gate = new object();
        var table = new Dictionary<string, Dictionary<string, string>>();
        await Task.WhenAll(pairs.Select(async pair =>
        {
            var sc = scenes[pair.Scene];
            var m = Cast.First(c => c.Self.Id == pair.Who.Id);
            var room = Guid.NewGuid();
            var present = Present(sc);
            var history = History(sc, room);
            var row = new Dictionary<string, string>();
            var lines = new Dictionary<string, List<string>>();
            foreach (var (name, cue) in PassWordings)
            {
                var said = new List<string>();
                for (var i = 0; i < PassSamples; i++)
                {
                    var result = await Within(bench, tally, $"{pair.Scene} {m.Self.Name} {name}", async ct => (SpeakingResult?)await new SpeakingSession(bench.Router, present)
                        .GenerateResponseOnlyAsync(m.Self, history, (_, _, _) => Task.CompletedTask, ct, presence: Presence(m, sc), inPerson: sc.Place, pass: cue));
                    if (result is not null) said.Add(result.Passed ? Passing.Mark : result.Message ?? "");
                }
                row[name] = $"{said.Count(l => l == Passing.Mark)}/{said.Count}";
                lines[name] = said;
            }
            var key = $"{sc.Key[..2]} {m.Self.Name} ({pair.Want})";
            Console.WriteLine($"{key,-34} " + string.Join("  ", row.Select(kv => $"{kv.Key}={kv.Value}")));
            lock (gate)
            {
                table[key] = row;
                bench.Observe($"pair · {key}", lines);
            }
        }));
        bench.Observe("passes per pair and wording", table.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value));
        bench.Observe("calls", new { tally.Total, tally.Lost });
    }
}
