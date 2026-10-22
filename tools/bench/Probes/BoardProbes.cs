using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Self;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Board modality + voice: replays real threads at real cut points through the real Board prompts and
/// a fresh distillation of each persona's sheet. Fixture (cast sources + board) comes from BOARD_FIXTURE,
/// kept outside the repo since it's a live world's content.
/// </summary>
public static partial class BoardProbes
{
    private sealed record FixturePersona(Guid Id, string Name, string Source);
    private sealed record Fixture(List<FixturePersona> Personas, List<BoardThread> Threads);

    /// <summary>A reply case: thread cut after <c>Cut</c> replies, who writes, what the Life loop intended, and whether a reply is warranted.</summary>
    private sealed record ReplyCase(string Thread, int Cut, string Author, string Intent, string Expect);

    private static readonly ReplyCase[] Replies =
    {
        new("Renaming logging fields", 8, "Denise", "Agree with Pieter that the field name is frozen and confirm the changelog entry.", "pass"),
        new("Renaming logging fields", 1, "Pieter Voss", "Answer Denise about freezing the names and propose how to migrate.", "reply"),
        new("Welcome Lena", 8, "Lena Brandt", "Thank Tim and everyone and introduce myself.", "pass-or-new"),
        new("Subtle Signals", 19, "Lena Brandt", "Say I'm off to get soup and leaving the thread for now.", "pass"),
        new("The ginkgo is refusing", 2, "Pieter Voss", "Offer again to come Sunday and take the broom.", "pass-or-new"),
        new("Printer vs. the children", 0, "Pieter Voss", "Explain that the jam sensor keeps the fault in memory until a power cycle.", "reply"),
        new("Dead rear light", 0, "Eiko Tanaka", "Tell Pieter I'm at the café and about to head home.", "pass"),
        new("Dead rear light", 0, "Denise", "Point Pieter to a quick fix for the light.", "reply"),
    };

    private static readonly (string Author, string Intent)[] Posts =
    {
        ("Denise", "Post about finally fixing the tap cartridge after three weeks of dripping."),
        ("Pieter Voss", "Ask if anyone knows a fietsenmaker who fixes a rear light the same day."),
        ("Eiko Tanaka", "Post about the printer eating the children's-event flyers."),
        ("Lena Brandt", "Post about reading my new lease and the clause I don't understand."),
        ("Denise", "Hey Eiko, want to grab coffee and talk about minimal vs maximal design?"),
    };

    private const string SameQuestion = "What's your favourite rainy-day ritual?";

    [Probe("Board: fresh lived sheets → reply cases (should they pass?), new posts (status talk? DMs?), and all four on the same open question (do voices differ?). Needs BOARD_FIXTURE.")]
    public static async Task Board_ModalityAndVoice(Bench bench)
    {
        var ct = bench.Cancellation;
        var path = Environment.GetEnvironmentVariable("BOARD_FIXTURE") ?? throw new InvalidOperationException("Set BOARD_FIXTURE.");
        var fixture = JsonSerializer.Deserialize<Fixture>(await File.ReadAllTextAsync(path, ct), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var llm = new WorldLlm(bench.Grains, bench.LoggerFactory.CreateLogger<WorldLlm>());

        var cast = new Dictionary<string, CastMember>();
        foreach (var p in fixture.Personas)
        {
            var lived = await llm.CompleteAsync(JobComplexity.CharacterVoice, LivedSheets.DistillPrompt, $"Name: {p.Name}\n\nTheir description:\n{p.Source}", ct, 0.5);
            if (lived is null) continue;
            LivedSheets.Remember(p.Id, LivedSheets.Hash(p.Source), lived.Trim());
            cast[p.Name] = new CastMember { Id = p.Id, Name = p.Name, DefaultDriver = DriverKind.LLM, SystemPrompt = p.Source };
            bench.Observe($"{p.Name} · how you talk", Section(lived, "## How you talk"));
        }

        // A day built to tempt status talk: at the café, lunch coming, chores nagging.
        var day = new SelfState
        {
            Mood = "a bit tired", Energy = "low", Preoccupation = "a form due Friday",
            Beats = SelfMath.ParseBeats("08:00 coffee and email\n11:00 a long call\n12:30 lunch at the café\n14:00 errands"),
        };
        var now = new DateTimeOffset(2026, 10, 21, 12, 40, 0, TimeSpan.Zero);
        var presence = SelfRender.Presence(day, 0.6, now, Array.Empty<string>(), "You're at the Café, at the window table, on your phone.");

        var tally = new Dictionary<string, int>();
        void Count(string k) => tally[k] = tally.GetValueOrDefault(k) + 1;

        foreach (var c in Replies)
        {
            if (!cast.TryGetValue(c.Author, out var self) || fixture.Threads.FirstOrDefault(t => t.Title.StartsWith(c.Thread)) is not { } full) continue;
            var thread = Cut(full, c.Cut);
            var json = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice,
                BoardApp.WriterSystem(WorldLlm.Identity(self), presence),
                BoardApp.ReplyUser(BoardApp.Render(thread, self.Id), c.Intent, null), ct, 0.9, ["body"]);
            var body = json is null ? "(model failed)" : BoardApp.ReplyBody(json) ?? "(pass)";
            var verdict = body == "(pass)" ? "pass" : "reply";
            Count($"replies.expect:{c.Expect}.got:{verdict}");
            if (verdict == "reply" && StatusTalk().IsMatch(body)) Count("replies.statusTalk");
            bench.Observe($"reply · {c.Thread} @{c.Cut} · {c.Author} · expect {c.Expect}", new { c.Intent, body });
        }

        foreach (var (author, intent) in Posts)
        {
            if (!cast.TryGetValue(author, out var self)) continue;
            var json = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice,
                BoardApp.WriterSystem(WorldLlm.Identity(self), presence),
                BoardApp.PostUser(intent, null, fixture.Threads.Take(10).Select(t => t.Title)), ct, 0.9, ["title", "body"]);
            var body = json?["body"]?.ToString() ?? "(model failed)";
            if (StatusTalk().IsMatch(body)) Count("posts.statusTalk");
            bench.Observe($"post · {author}", new { intent, title = json?["title"]?.ToString(), body });
        }

        var question = new BoardThread { Id = Guid.NewGuid(), AuthorId = Guid.NewGuid(), AuthorName = "Tim", Title = SameQuestion, Body = "It's pouring here. Mine is tea, a blanket and an old adventure game. What do you all do when the weather keeps you in?" };
        var answers = new Dictionary<string, string>();
        foreach (var (name, self) in cast)
        {
            var json = await llm.CompleteJsonAsync(JobComplexity.CharacterVoice,
                BoardApp.WriterSystem(WorldLlm.Identity(self), presence),
                BoardApp.ReplyUser(BoardApp.Render(question, self.Id), "Answer Tim's question.", null), ct, 0.9, ["body"]);
            answers[name] = json is null ? "(model failed)" : BoardApp.ReplyBody(json) ?? "(pass)";
        }
        bench.Observe("same question · side by side", answers);
        bench.Observe("same question · shape (words, lowercase share, '?' count, shared-vocab overlap vs others)", answers.ToDictionary(
            a => a.Key,
            a => new
            {
                words = Words(a.Value).Count,
                lowercaseStart = a.Value.Length > 0 && char.IsLower(a.Value[0]),
                questions = a.Value.Count(ch => ch == '?'),
                overlap = Math.Round(answers.Where(o => o.Key != a.Key).Average(o => VoiceWear.Overlap(Words(a.Value), Words(o.Value))), 2),
            }));
        bench.Observe("tally", tally);
    }

    private static BoardThread Cut(BoardThread t, int replies) => new()
    {
        Id = t.Id, AuthorId = t.AuthorId, AuthorName = t.AuthorName, Title = t.Title, Body = t.Body,
        CreatedAt = t.CreatedAt, LastActivityAt = t.LastActivityAt, Replies = t.Replies.Take(replies).ToList(),
    };

    private static string Section(string text, string heading)
    {
        var i = text.IndexOf(heading, StringComparison.Ordinal);
        if (i < 0) return "(missing)";
        var j = text.IndexOf("\n## ", i + heading.Length, StringComparison.Ordinal);
        return (j < 0 ? text[i..] : text[i..j]).Trim();
    }

    private static List<string> Words(string s) => Word().Matches(s.ToLowerInvariant()).Select(m => m.Value).Where(w => w.Length > 3).ToList();

    [GeneratedRegex(@"[\p{L}']+")]
    private static partial Regex Word();

    /// <summary>Narrating your whereabouts or exit: "going to get soup", "I'm at the café", "logging off".</summary>
    [GeneratedRegex(@"(?i)\b(going to (get|grab|eat|head)|heading (off|home|out)|i'm at the|i'm off|off to (get|grab)|logging off|done for (now|today)|back later|gotta go|brb|soup now|before my (neck|back))\b")]
    private static partial Regex StatusTalk();
}
