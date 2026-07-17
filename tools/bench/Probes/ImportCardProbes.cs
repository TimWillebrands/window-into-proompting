using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Import-spearhead validation 1 (scaffolding, #116): the trait→card render check — the real
/// extraction-contract risk. Phase 2 proved intrinsic self-traits CANNOT live in the memory
/// graph (latest-wins collapses them to one Stance), so the persona card is the only candidate
/// home. This probe answers: does a persona SYSTEM PROMPT folded from an imported flat trait
/// list read like a CHARACTER, or like a fact-sheet the speaking model recites?
///
/// It takes <c>artifact.personas</c> from a frozen phase-1 run (Dr. Lena Brandt, 12 traits;
/// Justin, 24 — the stress case), folds each naively (traits → bulleted SystemPrompt, NO
/// synthesized Bio — the phase-1 importer produces traits, not a one-liner), and drives the
/// real decision→speaking path so the composed prompts are captured (grain-call filter) and,
/// with a live session, one line is voiced per persona. Read:
///   • <c>{persona}.card</c> — the folded SystemPrompt (the contract artifact).
///   • the captured LLM calls — the FULL composed decision + speaking prompts.
///   • <c>{persona}.decision</c> — note the decision phase sees Bio ONLY, and imported
///     personas have no Bio, so it flies half-blind on the character.
///   • <c>{persona}.spoken</c> — judge in the persona's voice: character, or encyclopedia?
///     Is 24 traits (Justin) too many — does the card need a cap / compression pass? That
///     would be a NEW importer requirement, worth discovering now.
/// </summary>
public static class ImportCardProbes
{
    // ── knobs (env) ──────────────────────────────────────────────────────────────
    // IMPORT_ARTIFACT  path to a frozen phase-1 Probe Artifact JSON (trait source)
    private const string DefaultArtifact =
        "bench-runs/Import_CanonSectionsToArtifact/20260715-211927.json";

    private static readonly Guid DeniseId = BenchCast.DeniseId;
    private static readonly Guid RoomId = BenchCast.RoomId;

    // Denise's opener: emotionally loaded (a therapist OR a systems-penitent architect both have
    // a real reaction), but it NAMES NO PERSONA and ends on a period — so urge stays below the
    // 0.9 auto-respond shortcut and the decision LLM actually runs (bench trap, ADR 0011).
    private const string DeniseTrigger =
        "god, i don't even know why i'm saying this out loud, but i handed in my notice today — "
        + "fifteen years — and now i just feel completely untethered, like i cut the one rope i "
        + "actually knew how to hold on to.";

    private const string Scenario =
        "Late evening, the tail end of a small gathering at Denise's flat. Glasses half-empty, "
        + "the others drifting toward the balcony. Denise has just dropped onto the couch beside you.";

    [Probe("Import validation 1 (scaffolding, #116): trait→card render check — do imported flat trait lists fold into a persona SYSTEM PROMPT that reads as a CHARACTER or as a fact-sheet the model recites? Loads artifact.personas from a frozen phase-1 run (Dr. Lena Brandt 12 traits, Justin 24 — the stress case), folds each naively (bulleted traits → SystemPrompt, no synthesized Bio), and drives the REAL decision→speaking path against a live session so the composed prompts are captured and one line is voiced per persona. Read {persona}.card for the folded prompt, the captured LLM calls for the full composed decision+speaking prompts, {persona}.decision (note: decision sees Bio ONLY and imports have no Bio), and {persona}.spoken — judge in the persona's voice: character or encyclopedia, and is 24 traits too many (does the card need a cap / compression pass — a NEW importer requirement)? Env: IMPORT_ARTIFACT overrides the trait source.")]
    public static async Task Import_TraitsToPersonaCard(Bench bench)
    {
        var ct = bench.Cancellation;
        var path = ResolveFile(Environment.GetEnvironmentVariable("IMPORT_ARTIFACT") ?? DefaultArtifact);
        var personas = LoadPersonas(await File.ReadAllTextAsync(path, ct));
        bench.Observe("config", new { artifact = path, personaNames = personas.Keys.ToList() });
        bench.Observe("method",
            "Naive fold: each imported trait list becomes a bulleted SystemPrompt, Bio left null "
            + "(phase 1 produces traits, not a one-liner). We deliberately do NOT pre-compress — "
            + "the point is to discover whether the straightforward fold reads badly. The decision "
            + "phase composes on Bio only; speaking composes on Bio + SystemPrompt (see "
            + "SpeakingSession.ComposeIdentity / PersonaDecisionService.ShouldRespondSystemPrompt).");

        foreach (var name in new[] { "Dr. Lena Brandt", "Justin" })
        {
            if (!personas.TryGetValue(name, out var traits) || traits.Count == 0)
            {
                bench.Observe($"{Slug(name)}.MISSING", $"'{name}' absent or traitless in artifact.personas");
                continue;
            }
            await RunOneAsync(bench, name, traits, ct);
        }

        bench.Observe("judge",
            "Read each {persona}.card and its captured speaking prompt: does the folded trait list "
            + "read as a person you could BE, or as a dossier to recite? Then read {persona}.spoken — "
            + "is the voice specific and in-character, or does it name-drop its own traits ('as The "
            + "Penitent Architect, I...') like an encyclopedia entry? Compare Lena (12) vs Justin (24): "
            + "does the extra volume tip Justin into recitation / self-contradiction — evidence the "
            + "card needs a trait cap or a compression pass (a NEW importer requirement)? Finally note "
            + "the decision-phase gap: it sees Bio only, imports have none, so the '# You are' block is "
            + "just a name — is a synthesized Bio a required importer output?");
    }

    // ── one persona through the real path ────────────────────────────────────────

    private static async Task RunOneAsync(Bench bench, string name, IReadOnlyList<string> traits, CancellationToken ct)
    {
        var slug = Slug(name);
        var card = FoldCard(name, traits);
        bench.Observe($"{slug}.card (naive fold: traits → SystemPrompt, Bio = null)", new
        {
            traitCount = traits.Count,
            cardChars = card.Length,
            systemPrompt = card,
        });

        var self = new SelfView(
            Id: PersonaId(name),
            Name: name,
            Driver: DriverKind.LLM,
            Bio: null,                 // honest: the phase-1 importer emits traits, not a Bio
            SystemPrompt: card,
            Chattiness: 0.5,
            Impulsivity: 0.4);

        var present = new[]
        {
            new ParticipantView(self.Id, name, DriverKind.LLM),
            new ParticipantView(DeniseId, "Denise", DriverKind.LLM),
        };
        var history = new List<ChatMessage>
        {
            new()
            {
                MessageId = 1,
                Content = DeniseTrigger,
                SenderType = "user",
                SenderId = DeniseId,
                ChatGroupId = RoomId,
            },
        };

        // ── decision phase: composes + captures the '# You are' prompt (Bio-only) ──
        var decisionService = new PersonaDecisionService(bench.Router, bench.Logger("PersonaDecisionService"));
        var decision = await decisionService.ShouldRespondAsync(
            self, history, present, totalAiRoundsInGroup: 0,
            onEvent: (_, _, _) => Task.CompletedTask, cancellationToken: ct,
            scenario: Scenario, recollections: null);
        bench.Observe($"{slug}.decision (composed on Bio ONLY — imports have no Bio)", new
        {
            decision.Respond,
            gutReaction = decision.Reason,
            wouldSay = decision.Instruction,
        });

        // ── speaking phase: composes + captures the full card prompt; always voiced ──
        // Force a line even if she'd pass, so there's always a sample to judge the card by.
        var wouldSpeak = decision.Respond && !string.IsNullOrWhiteSpace(decision.Instruction);
        var instruction = wouldSpeak
            ? decision.Instruction
            : "Say one thing, in your own voice, about what Denise just told you.";
        var session = new SpeakingSession(bench.Router, present);
        var spoken = await session.GenerateResponseOnlyAsync(
            self, history, onEvent: (_, _, _) => Task.CompletedTask, cancellationToken: ct,
            turnInstruction: instruction, scenario: Scenario, gutReaction: decision.Reason);
        bench.Observe($"{slug}.spoken (in-voice line — read for CHARACTER vs fact-sheet recitation)", new
        {
            forcedPastDecline = !wouldSpeak,
            message = spoken.Message,
            reasoning = string.IsNullOrWhiteSpace(spoken.Reasoning) ? null : spoken.Reasoning,
            model = spoken.Metadata?.ModelName,
        });
    }

    // ── the fold under test: flat trait list → a persona card ────────────────────

    /// <summary>The naive importer fold — a header plus one bullet per trait. Deliberately
    /// unclever: if this reads as an encyclopedia, that IS the finding (the card needs a cap or
    /// a compression pass before traits become a system prompt).</summary>
    private static string FoldCard(string name, IReadOnlyList<string> traits)
        => new StringBuilder()
            .Append("You are ").Append(name).AppendLine(".")
            .AppendLine()
            .AppendLine(string.Join("\n", traits.Select(t => "- " + t.Trim())))
            .ToString().Trim();

    // ── artifact load + small helpers ────────────────────────────────────────────

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadPersonas(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var obs in doc.RootElement.GetProperty("Observations").EnumerateArray())
        {
            if (obs.GetProperty("Label").GetString() != "artifact.personas")
                continue;
            var dict = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in obs.GetProperty("Data").EnumerateObject())
                dict[prop.Name] = prop.Value.EnumerateArray()
                    .Select(t => t.GetString() ?? "").Where(t => t.Length > 0).ToList();
            return dict;
        }
        throw new KeyNotFoundException("Observation 'artifact.personas' not found in the frozen artifact.");
    }

    /// <summary>Stable per-persona id. These are name-only in the roster, so any deterministic
    /// guid works; MD5 keeps artifacts diffing cleanly across runs.</summary>
    private static Guid PersonaId(string name)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"import-card:{name}")));

    private static readonly string[] Honorifics = { "dr", "mr", "mrs", "ms", "prof" };

    private static string Slug(string name)
    {
        var token = name.Split(new[] { ' ', '.' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(t => !Honorifics.Contains(t.ToLowerInvariant()));
        return (token ?? name).ToLowerInvariant();
    }

    private static string ResolveFile(string path)
    {
        if (File.Exists(path)) return path;
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 5 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Frozen artifact not found: {path}");
    }
}
