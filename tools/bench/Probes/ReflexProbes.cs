using System.Diagnostics;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Reflex;
using PartyTown.World.Self;
using Microsoft.Extensions.Logging.Abstractions;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Instrument (ADR 0031): how real System One answers Reflex across channels, before and after the landscape.
/// Calibration lives here: if p compresses, the Torn margin and the pulls have to be read against this.
/// </summary>
public static class ReflexProbes
{
    private sealed record Scenario(string Name, Percept Percept, Sense Sense);

    private static readonly Guid Friend = Guid.NewGuid();
    private static readonly Guid Room = Guid.NewGuid();

    private const string Bio = "A 34-year-old graphic designer, dry humour, hates small talk but loyal to the few people she likes.";

    private static Sense S(double load = 0.1, string? doing = "having a slow morning at home", string mood = "fine", double battery = 0.9, Bond? bond = null, params string[] between)
        => new("Denise", Bio, doing, mood, load, battery, bond ?? new Bond(60, 0.3, 20), between);

    private static Percept P(PerceptKind kind, string text = "", bool human = false, bool named = false, bool near = true, string from = "Hana", string[]? context = null)
        => new(kind, Friend, from, human, text, Room, named, near, RoomName: "kitchen", Context: context);

    private static readonly Bond Stranger = Bond.None;
    private static readonly Bond Strained = new(45, -0.7, 30);
    private static readonly Bond Colleague = new(30, 0.1, 0.15);
    private static readonly Bond AbsentFriend = new(80, 0.5, 60);

    private static readonly Scenario[] Scenarios =
    {
        new("dm friend, idle, casual", P(PerceptKind.Direct, "lol did you see what sam posted"), S()),
        new("dm friend, in a client meeting, casual", P(PerceptKind.Direct, "lol did you see what sam posted"), S(0.85, "in a client meeting at work")),
        new("dm friend, in a client meeting, urgent", P(PerceptKind.Direct, "im at the hospital with my dad, can you call me"), S(0.85, "in a client meeting at work")),
        new("dm stranger, working", P(PerceptKind.Direct, "hey", from: "Ruben"), S(0.6, "finishing a logo at her desk", bond: Stranger)),
        new("dm human, free", P(PerceptKind.Direct, "hey what are you up to?", human: true, from: "Tim"), S(bond: new Bond(12, 0.2, 5))),
        new("dm human, on shift", P(PerceptKind.Direct, "hey what are you up to?", human: true, from: "Tim"), S(0.85, "on shift at the print shop", bond: new Bond(12, 0.2, 5))),
        new("dm strained, free", P(PerceptKind.Direct, "we need to talk about what you said"), S(bond: Strained, between: "She still hasn't apologised for the party.")),
        new("dm friend, peopled out", P(PerceptKind.Direct, "look at this cat"), S(battery: 0.12, mood: "drained")),
        new("room chatter, idle", P(PerceptKind.Line, "anyone seen the new bakery on the corner", context: new[] { "Sam: i want bread", "Pieter: bread is good" }), S()),
        new("room chatter on her thing, busy", P(PerceptKind.Line, "who picked this awful font for the poster", context: new[] { "Sam: posters are up" }), S(0.6, "finishing a logo at her desk")),
        new("room named, busy", P(PerceptKind.Line, "Denise you still coming saturday?", named: true), S(0.6, "finishing a logo at her desk")),
        new("speech to her face, a question", P(PerceptKind.Speech, "what do you think, should I take the job?"), S()),
        new("speech needling, strained", P(PerceptKind.Speech, "funny seeing you here, thought you were too busy for us"), S(bond: Strained)),
        new("arrival friend after days", P(PerceptKind.Arrival), S(bond: AbsentFriend)),
        new("arrival stranger, reading", P(PerceptKind.Arrival, from: "Ruben"), S(0.35, "reading on a bench", bond: Stranger)),
        new("arrival strained, tired", P(PerceptKind.Arrival), S(battery: 0.3, mood: "tired", bond: Strained, between: "She still hasn't apologised for the party.")),
        new("arrival colleague just seen, working", P(PerceptKind.Arrival), S(0.6, "working at the café counter", bond: Colleague)),
        new("arrival human, idle", P(PerceptKind.Arrival, human: true, from: "Tim"), S(bond: new Bond(12, 0.2, 20))),
        new("arrival across the room, friend", P(PerceptKind.Arrival, near: false), S(bond: AbsentFriend)),
        new("overheard, arguing", P(PerceptKind.Overheard, "arguing", named: true), S()),
        new("overheard, laughing", P(PerceptKind.Overheard, "laughing", named: true, near: false), S()),
        new("knock friend", P(PerceptKind.Knock), S()),
        new("knock strained", P(PerceptKind.Knock), S(bond: Strained)),
    };

    [Probe("Reflex calibration: 23 Percepts across every channel through the real judge; raw vs shaped distribution, torn flag, latency. Needs OPENROUTER_API_KEY.")]
    public static async Task Reflex_Calibration(Bench bench)
    {
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("OPENROUTER_API_KEY not set: nothing to weigh.");
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var judge = new HttpSystemOneJudge(http, "openrouter-jev", HttpSystemOneJudge.OpenRouterUrl, key, HttpSystemOneJudge.OpenRouterModel, NullLogger<HttpSystemOneJudge>.Instance);

        foreach (var s in Scenarios)
        {
            var clock = Stopwatch.StartNew();
            var answers = await judge.AskAsync(ReflexWeighing.State(s.Percept, s.Sense), ReflexWeighing.Ask(s.Percept, s.Sense.Name), bench.Cancellation);
            var ms = clock.ElapsedMilliseconds;
            if (!answers.Choices.TryGetValue(ReflexWeighing.Key, out var raw))
            {
                Console.WriteLine($"{s.Name,-42} (no answer)");
                continue;
            }
            var shaped = ReflexMath.Shape(raw, new Landscape(0.5, 0.5, s.Percept.FromHuman));
            var strength = ReflexMath.Strength(s.Percept, s.Sense.Bond, 0);
            var noticed = strength >= ReflexMath.Threshold(s.Sense.Load);
            string Fmt(IReadOnlyDictionary<string, double> d) => string.Join(" ", d.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value:F2}"));
            Console.WriteLine($"{s.Name,-42} {ms,5}ms s={strength:F2}/{ReflexMath.Threshold(s.Sense.Load):F2}{(noticed ? "" : " UNNOTICED")}{(ReflexMath.Torn(shaped) ? " TORN" : "")}\n    raw    {Fmt(raw)}\n    shaped {Fmt(shaped)}");
            bench.Observe(s.Name, new { ms, strength, noticed, torn = ReflexMath.Torn(shaped), raw, shaped });
        }
    }

    private const int Samples = 3;

    [Probe("Reflex voicing: what a Persona actually says for a quick ack, a get-back after a delay, a greeting and a call across the room. 2 personas, 3 samples; read them for stage management (\"sorry for the delay\" every time) and for repeating the brief.")]
    public static async Task Reflex_Voicing(Bench bench)
    {
        var now = new DateTimeOffset(2026, 10, 21, 14, 30, 0, TimeSpan.FromHours(1));
        var day = new SelfState
        {
            Mood = "steady", Energy = "medium",
            Beats = SelfMath.ParseBeats("08:00 coffee\n09:00 at the studio\n14:00 client meeting about the rebrand"),
        };
        var presence = SelfRender.Presence(day, 0.8, now, Array.Empty<string>(), "You're at the studio, in a meeting room.");
        var casual = "hey what are you up to?";
        var urgent = "im at the hospital with my dad, can you call me when you can";

        var turns = new (string Name, string Said, string Instruction)[]
        {
            ("ack casual", casual, ReflexVoice.Ack("a client meeting about the rebrand")),
            ("ack urgent", urgent, ReflexVoice.Ack("a client meeting about the rebrand")),
            ("get back casual", casual, ReflexVoice.GetBack("Sam", casual, "in a client meeting about the rebrand")),
            ("get back urgent", urgent, ReflexVoice.GetBack("Sam", urgent, "in a client meeting about the rebrand")),
            ("greet", "", ReflexVoice.Greet("Sam")),
            ("call over", "", ReflexVoice.CallOver("Sam")),
        };

        foreach (var self in new[] { BenchCast.Vlad, BenchCast.Denise })
        foreach (var (name, said, instruction) in turns)
        {
            var history = string.IsNullOrEmpty(said)
                ? Array.Empty<ChatMessage>()
                : new[] { new ChatMessage { MessageId = 1, Content = said, SenderType = "user", SenderId = BenchCast.SamId, ChatGroupId = BenchCast.RoomId } };
            var present = new[] { BenchCast.Sam, new ParticipantView(self.Id, self.Name, DriverKind.LLM) };
            var replies = new List<string>();
            for (var i = 0; i < Samples; i++)
            {
                var result = await new SpeakingSession(bench.Router, present).GenerateResponseOnlyAsync(
                    self, history, (_, _, _) => Task.CompletedTask, bench.Cancellation, turnInstruction: instruction, presence: presence);
                replies.Add(result.Message ?? string.Empty);
            }
            bench.Observe($"{self.Name} · {name}", new { instruction, replies });
            Console.WriteLine($"{self.Name} · {name}\n  " + string.Join("\n  ", replies));
        }
    }
}
