using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JsonRepairSharp;
using Microsoft.Extensions.Logging;
using PartyTown.Grains.Generation;

namespace PartyTown.Bench.Probes;

/// <summary>
/// Import-spearhead phase 3 (scaffolding, pre-ADR-0013-rebuild): the last unhandled chunk
/// category — the 212 plain <c>message</c> chunks the phase-1 probe categorizes and then
/// drops. Probe-land only: artifacts, no grains / rooms / AGE writes (same rule as phases
/// 1–2). Capped at ~10 messages (env <c>IMPORT_MESSAGE_LIMIT</c>) so we validate the route
/// SHAPE without paying for 212 chunks of LLM calls — design for 212, run on 10.
///
/// The route under test (capture-idiom, NOT the phase-1 section-extraction prompt — messages
/// are conversation-shaped, so the closest existing idiom is organic moment capture: recent
/// context + "is this memorable"):
///   1. deterministic IR — reuse the phase-1 categorizer, keep only the message chunks.
///   2. deterministic speaker attribution — this export's ground truth (systemInstruction:
///      "You are Dr Lena Brandt… Only Lena speaks"): role=model → "Lena", role=user → the
///      human game-master ("Player") who narrates the scene and voices every OTHER character
///      through quoted dialogue. In-text character names are CONTENT the model attributes as
///      episode participants, never a deterministic speaker.
///   3. windowing, not per-message calls, and MAP not scan — batch messages into overlapping
///      windows, ONE General-tier call per window. Each call is INDEPENDENT and STATELESS
///      (no running "story so far" — error propagation would be a baked-in parasitic loop;
///      cross-window continuity comes from the overlap). The model picks the 0..N
///      capture-worthy MOMENTS in its window (salience selection is the point — NOT one
///      event per message; 212 events of dialogue filler would swamp the 96 canon events,
///      the phase-2 drowning finding), each an episode with participants / concepts / a
///      salience weight, plus the source message indices it drew from.
///   4. deterministic reduce (pure code, no LLM) — cross-window dedup by word-overlap +
///      shared participant collapses the same moment re-extracted in the overlap; a
///      per-message conservation ledger records every capped message as folded (fed an
///      episode) | kept (history-only, the common case) | discarded(reason) (OOC/meta), and
///      the 202 uncapped as skipped(cap). Nothing vanishes silently.
///   5. cross-boundary OBSERVATION — compare message-derived episodes against the frozen
///      phase-1 canon events (recap-derived). The same story beat can exist as both a recap
///      summary and a verbatim scene; collisions are surfaced, never dropped — the genuinely
///      interesting artifact question is which telling survives.
///
/// The falsifiable question (phase-1's original, now for messages): does this route produce
/// coexistence-grade events — same granularity as the canon events — or filler? Read
/// message.episodes (count/granularity/weight spread), message.canonCollisions (recap vs
/// verbatim), message.conservation (no silent loss), ledger.costProjection (per-window ×
/// 212/window-stride) and judge.
/// </summary>
public static class ImportMessageProbes
{
    // ── knobs (env) ──────────────────────────────────────────────────────────────
    // IMPORT_FILE            path to the AI Studio export     (default: docs/Copy of Technokangs.json)
    // IMPORT_MESSAGE_LIMIT   messages processed (BENCH cap)   (default: 10)
    // IMPORT_MESSAGE_WINDOW  messages per window              (default: 6)
    // IMPORT_MESSAGE_OVERLAP messages shared between windows  (default: 2)
    // IMPORT_MESSAGE_ANCHOR  ISO-8601 message-band anchor     (default: utcnow - 20d)
    // IMPORT_SPACING_MIN     minutes between message events   (default: 10)
    // IMPORT_CANON_ARTIFACT  frozen phase-1 artifact for the collision check
    //                        (default: the mistral-small-2603 canonical run; skipped if absent)

    private const int MaxMessageChars = 4_200; // per-message hard cap into the window prompt (export max ≈ 4170)
    private const double DupJaccard = 0.25;    // phase-1's tuned re-telling threshold, reused across windows + vs canon

    private const string DefaultCanonArtifact =
        "bench-runs/Import_CanonSectionsToArtifact/20260715-211927.json";

    [Probe("Import spearhead phase 3 (scaffolding): route the export's 212 plain `message` chunks — the last category phase-1 drops — into memory-seed episodes, no grains/rooms/AGE. Deterministic IR keeps message chunks; speaker attribution is this export's ground truth (role=model → Lena, role=user → the human game-master 'Player' who voices every other character in-text). Messages batch into OVERLAPPING windows, ONE stateless General-tier call per window (map, not scan): the model does SALIENCE SELECTION — 0..N capture-worthy moments per window (NOT one event per message), each an episode with participants + source-stated concepts + a stick-a-day-later weight + the source message indices. Deterministic reduce: cross-window word-overlap+participant dedup collapses moments re-seen in the overlap; a per-message conservation ledger books every capped message as folded|kept(history-only)|discarded(reason) and the uncapped as skipped(cap) — no silent loss. Then OBSERVE collisions of message episodes against the frozen phase-1 canon (recap) events — does the recap summary or the verbatim scene survive? Read message.episodes for coexistence-grade granularity + weight spread, message.canonCollisions for the recap/verbatim overlap, message.conservation (must reconcile to the message count), ledger.costProjection for what 212 costs. BENCH-capped at ~10 messages (IMPORT_MESSAGE_LIMIT); design is for 212.")]
    public static async Task Import_MessageWindowsToArtifact(Bench bench)
    {
        var ct = bench.Cancellation;
        var log = bench.Logger("import-messages");

        // ── 0. knobs ─────────────────────────────────────────────────────────────
        var filePath = ResolveFile(Environment.GetEnvironmentVariable("IMPORT_FILE") ?? "docs/Copy of Technokangs.json");
        var limit = ParseInt("IMPORT_MESSAGE_LIMIT", 10, min: 1);
        var windowSize = ParseInt("IMPORT_MESSAGE_WINDOW", 6, min: 1);
        var overlap = Math.Min(ParseInt("IMPORT_MESSAGE_OVERLAP", 2, min: 0), windowSize - 1);
        var anchor = DateTimeOffset.TryParse(Environment.GetEnvironmentVariable("IMPORT_MESSAGE_ANCHOR"), out var a)
            ? a : DateTimeOffset.UtcNow.AddDays(-20);
        var spacing = TimeSpan.FromMinutes(
            double.TryParse(Environment.GetEnvironmentVariable("IMPORT_SPACING_MIN"), out var m) ? m : 10);
        var canonPath = Environment.GetEnvironmentVariable("IMPORT_CANON_ARTIFACT") ?? DefaultCanonArtifact;

        bench.Observe("config", new
        {
            file = filePath, messageLimit = limit, windowSize, overlap,
            anchor, spacingMin = spacing.TotalMinutes, canonArtifact = canonPath,
        });

        // ── 1. parse + deterministic IR — keep only the message chunks ───────────
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(filePath, ct));
        var chunks = doc.RootElement.GetProperty("chunkedPrompt").GetProperty("chunks");
        var ir = BuildIr(chunks);
        var messages = ir.Where(e => e.Category == "message").ToList();
        var capped = messages.Take(limit).ToList();
        var skipped = messages.Skip(limit).ToList();

        bench.Observe("ir.messages", new
        {
            totalMessages = messages.Count,
            capped = capped.Count,
            skippedCap = skipped.Count,
            roleSplit = messages.GroupBy(e => e.Role).ToDictionary(g => g.Key, g => g.Count()),
            charStats = messages.Count == 0 ? null : new
            {
                min = messages.Min(e => e.Chars),
                max = messages.Max(e => e.Chars),
                avg = (int)messages.Average(e => e.Chars),
            },
        });

        if (capped.Count == 0)
        {
            bench.Observe("ABORT", "no message chunks under the current filter — nothing to route.");
            return;
        }

        // chunk index → position in the capped run (story order; used for episode timestamps)
        var positionOf = capped.Select((e, i) => (e.Index, i)).ToDictionary(x => x.Index, x => x.i);
        var cappedIndices = capped.Select(e => e.Index).ToHashSet();

        // ── 2. build overlapping windows over the capped run ─────────────────────
        var windows = BuildWindows(capped.Count, windowSize, overlap);
        bench.Observe("windows.summary", new
        {
            count = windows.Count,
            stride = windowSize - overlap,
            ranges = windows.Select(w => new
            {
                chunks = capped.Skip(w.Start).Take(w.End - w.Start).Select(e => e.Index).ToList(),
            }).ToList(),
        });

        // ── 3. one stateless LLM call per window — the model picks salient moments ─
        var allEpisodes = new List<RawEpisode>();       // every extracted episode (pre-dedup)
        var discardReasons = new Dictionary<int, string>(); // chunk index → OOC/meta reason (first wins)
        var unparseableWindows = 0;
        var inChars = 0L;
        var outChars = 0L;
        var stopwatch = Stopwatch.StartNew();

        for (var w = 0; w < windows.Count; w++)
        {
            ct.ThrowIfCancellationRequested();
            var (win, user) = BuildWindowPrompt(capped, windows[w]);
            var windowId = $"w{w}";
            var (raw, parsed) = await ClassifyWindowAsync(bench, windowId, user, ct);
            inChars += user.Length;
            outChars += raw.Length;

            if (parsed is null)
            {
                unparseableWindows++;
                bench.Observe($"window.{windowId}.degraded",
                    new { reason = "unparseable window output", rawLen = raw.Length, rawHead = Truncate(raw, 200) });
                continue;
            }

            bench.Observe($"window.{windowId}", new
            {
                chunks = win,
                episodes = (parsed.Episodes ?? []).Select(e => new { e.Summary, e.Weight, e.Participants, e.SourceMessages }).ToList(),
                discards = (parsed.Discards ?? []).Select(d => new { d.Index, d.Reason }).ToList(),
            });

            foreach (var ep in parsed.Episodes ?? [])
            {
                if (string.IsNullOrWhiteSpace(ep.Summary)) continue;
                var src = (ep.SourceMessages ?? []).Where(cappedIndices.Contains).Distinct().OrderBy(i => i).ToList();
                allEpisodes.Add(new RawEpisode(
                    windowId, ep.Summary!.Trim(),
                    Math.Clamp(ep.Weight ?? 0.5, 0.0, 1.0),
                    (ep.Participants ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
                    (ep.Concepts ?? []).Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList(),
                    src));
            }
            foreach (var d in parsed.Discards ?? [])
                if (cappedIndices.Contains(d.Index) && !string.IsNullOrWhiteSpace(d.Reason))
                    discardReasons.TryAdd(d.Index, d.Reason!.Trim());
        }
        stopwatch.Stop();

        // ── 4. deterministic reduce — cross-window dedup (overlap re-tellings) ────
        var survivors = new List<MessageEvent>();
        var survivorWords = new List<HashSet<string>>();
        var deduped = new List<object>();
        var ordinal = 0;
        foreach (var ep in allEpisodes.OrderBy(EarliestPosition))
        {
            var words = ContentWords(ep.Summary);
            var dupOf = FindDuplicate(survivors, survivorWords, words, ep.Participants);
            if (dupOf is not null)
            {
                deduped.Add(new
                {
                    keptOrdinal = dupOf.Ordinal, keptWindow = dupOf.Window, keptSummary = dupOf.Summary,
                    droppedWindow = ep.Window, droppedSummary = ep.Summary,
                });
                continue;
            }
            var pos = EarliestPosition(ep);
            survivors.Add(new MessageEvent(
                ordinal, anchor + pos * spacing, ep.Summary, ep.Weight,
                ep.Participants, ep.SourceMessages, ep.Window));
            survivorWords.Add(words);
            ordinal++;
        }

        // ── 5. per-message conservation ledger — nothing vanishes silently ───────
        // A message that fed ANY extracted episode (even one later deduped) is "folded":
        // it WAS judged salient. Otherwise it is kept as replayable history (the common
        // case for dialogue filler), unless the model flagged it OOC/meta → discarded.
        var foldedSet = allEpisodes.SelectMany(e => e.SourceMessages).ToHashSet();
        var routing = capped.Select(e =>
        {
            var speaker = SpeakerFor(e.Role);
            var disposition =
                foldedSet.Contains(e.Index) ? "folded"
                : discardReasons.TryGetValue(e.Index, out var reason) ? $"discarded({reason})"
                : "kept(history-only)";
            return new { chunk = e.Index, speaker, e.Chars, disposition };
        }).ToList();

        var folded = routing.Count(r => r.disposition == "folded");
        var discarded = routing.Count(r => r.disposition.StartsWith("discarded", StringComparison.Ordinal));
        var kept = routing.Count(r => r.disposition == "kept(history-only)");

        // ── 6. cross-boundary observation — message episode vs recap-derived canon ─
        var canonEvents = LoadCanonEvents(canonPath);
        var collisions = new List<object>();
        if (canonEvents.Count > 0)
        {
            foreach (var me in survivors)
            {
                var meWords = ContentWords(me.Summary);
                var meTokens = me.Participants.SelectMany(TokenSet).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var ce in canonEvents)
                {
                    var shared = ce.Participants.Where(cp => SharesPersona(meTokens, cp)).ToList();
                    if (shared.Count == 0) continue;
                    var jac = Jaccard(meWords, ce.Words);
                    if (jac < DupJaccard) continue;
                    collisions.Add(new
                    {
                        messageOrdinal = me.Ordinal, messageSummary = me.Summary,
                        canonOrdinal = ce.Ordinal, canonSummary = ce.Summary,
                        jaccard = Math.Round(jac, 3), sharedParticipants = shared,
                    });
                }
            }
        }

        // ── 7. the artifact ───────────────────────────────────────────────────────
        bench.Observe("message.episodes", survivors.Select(e => new
        {
            ordinal = e.Ordinal, at = e.At, weight = e.Weight, summary = e.Summary,
            participants = e.Participants, sourceMessages = e.SourceMessages, window = e.Window,
        }).ToList());
        bench.Observe("message.dedupedEpisodes (same moment re-extracted across the window overlap → first telling kept)", deduped);
        bench.Observe("message.speakers (deterministic role→speaker; other names are in-text content the model attributes)",
            routing.GroupBy(r => r.speaker).ToDictionary(g => g.Key, g => g.Count()));
        bench.Observe("message.routing (per-message disposition — the conservation ledger)", routing);
        bench.Observe("message.conservation", new
        {
            totalMessages = messages.Count,
            capped = capped.Count,
            folded, keptHistoryOnly = kept, discarded,
            skippedCap = skipped.Count,
            unparseableWindows,
            accountedFor = folded + kept + discarded + skipped.Count,
            reconciles = folded + kept + discarded + skipped.Count == messages.Count,
            rule = "every message is folded | kept(history) | discarded(reason) | skipped(cap) — nothing vanishes silently",
        });
        bench.Observe(canonEvents.Count > 0
            ? "message.canonCollisions (message episode ↔ recap-derived canon event overlap — OBSERVED, not dropped; which telling should survive is the open question)"
            : "message.canonCollisions (SKIPPED — canon artifact not found; set IMPORT_CANON_ARTIFACT)", collisions);

        var stride = windowSize - overlap;
        var windowsFor212 = BuildWindows(messages.Count, windowSize, overlap).Count;
        var weights = survivors.Select(e => e.Weight).ToList();
        bench.Observe("ledger", new
        {
            llmCalls = windows.Count,
            elapsedMs = stopwatch.ElapsedMilliseconds,
            inChars, outChars,
            episodes = survivors.Count,
            dedupedEpisodes = deduped.Count,
            canonCollisions = collisions.Count,
            weightSpread = weights.Count == 0 ? null : new
            {
                min = weights.Min(), max = weights.Max(),
                avg = Math.Round(weights.Average(), 3),
                distinct = weights.Distinct().Count(),
            },
            costProjection = new
            {
                note = "linear extrapolation of THIS run to the full 212-message band, same window/stride",
                windowsFor212,
                projectedLlmCalls = windowsFor212,
                projectedElapsedMs = windows.Count == 0 ? 0 : (long)((double)stopwatch.ElapsedMilliseconds / windows.Count * windowsFor212),
                projectedInChars = windows.Count == 0 ? 0 : (long)((double)inChars / windows.Count * windowsFor212),
                projectedOutChars = windows.Count == 0 ? 0 : (long)((double)outChars / windows.Count * windowsFor212),
            },
        });

        log.LogInformation(
            "Import messages: {Capped}/{Total} messages → {Windows} windows → {Episodes} episodes ({Deduped} deduped), {Folded} folded / {Kept} kept / {Discarded} discarded, {Collisions} canon collisions in {Ms}ms",
            capped.Count, messages.Count, windows.Count, survivors.Count, deduped.Count, folded, kept, discarded, collisions.Count, stopwatch.ElapsedMilliseconds);
    }

    // ── deterministic IR (mirrors phase-1 categorizer; message chunks are the target) ─

    private static readonly Regex RecapHeader = new(@"^\s*#+\s*(history|checkpoint)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private sealed record IrEntry(int Index, string Role, string Category, int Chars, string Text);

    private static List<IrEntry> BuildIr(JsonElement chunks)
    {
        var ir = new List<IrEntry>();
        var idx = 0;
        foreach (var chunk in chunks.EnumerateArray())
        {
            // `.text` is canonical — AI Studio in-app edits leave `parts` stale.
            var text = chunk.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var role = chunk.TryGetProperty("role", out var r) ? r.GetString() ?? "?" : "?";
            var category =
                chunk.TryGetProperty("isThought", out var th) && th.ValueKind == JsonValueKind.True ? "thought"
                : (chunk.TryGetProperty("driveImage", out _) || chunk.TryGetProperty("driveAudio", out _)) && text.Length == 0 ? "media"
                : text.Length == 0 ? "empty"
                : RecapHeader.IsMatch(text) ? "recap"
                : "message";
            ir.Add(new IrEntry(idx++, role, category, text.Length, text));
        }
        return ir;
    }

    // This export's ground truth (systemInstruction): "You are Dr Lena Brandt… Only Lena
    // speaks in dialogue. Lena never writes dialogue for other characters." So the model turn
    // IS Lena; the user turn is the human game-master narrating the scene and voicing every
    // other character in-text. The GM has no canonical name in the export → "Player".
    private static string SpeakerFor(string role) => role == "model" ? "Lena" : "Player";

    // ── windowing ──────────────────────────────────────────────────────────────────

    private readonly record struct Window(int Start, int End);

    /// <summary>Overlapping windows over <paramref name="count"/> items. stride = size −
    /// overlap; the last window is extended to reach the end so no tail message is skipped.</summary>
    private static List<Window> BuildWindows(int count, int size, int overlap)
    {
        var stride = Math.Max(1, size - overlap);
        var windows = new List<Window>();
        if (count <= 0) return windows;
        for (var start = 0; start < count; start += stride)
        {
            var end = Math.Min(start + size, count);
            windows.Add(new Window(start, end));
            if (end == count) break;
        }
        return windows;
    }

    private static (List<int> Chunks, string User) BuildWindowPrompt(List<IrEntry> capped, Window window)
    {
        var chunkIds = new List<int>();
        var sb = new StringBuilder().AppendLine("MESSAGE WINDOW (consecutive turns; [#N] is the message id):").AppendLine();
        for (var i = window.Start; i < window.End; i++)
        {
            var e = capped[i];
            chunkIds.Add(e.Index);
            sb.AppendLine($"[#{e.Index}] ({SpeakerFor(e.Role)}):")
              .AppendLine(Truncate(e.Text.Trim(), MaxMessageChars))
              .AppendLine();
        }
        return (chunkIds, sb.ToString());
    }

    // ── the one LLM judgment per window: salient moments, not per-message events ────

    private const string MessageWindowSystemPrompt =
        """
        You read a short WINDOW of consecutive messages from a two-person roleplay chat and
        pick out only the moments worth REMEMBERING later — the judgement a person makes
        recalling a conversation a day afterwards, not a transcript.

        The roleplay: one side plays Dr. Lena Brandt, a therapist (her turns are labelled
        "Lena"). The other side is the human GAME-MASTER (labelled "Player") who narrates the
        scene in second person AND voices every OTHER character (Denise, Justin, Mara, Anne,
        Sienna, …) through quoted dialogue inside their turns. Attribute those named
        characters yourself — the speaker label is only ever "Lena" or "Player".

        Each message is prefixed with [#N]. Return STRICT JSON:

        - episodes: 0..N salient moments. MOST messages are dialogue filler and produce NO
          episode. Pick only what would genuinely stick; prefer FEWER, higher-signal
          episodes over covering everything. Do NOT emit one episode per message. PADDING IS
          WORSE THAN BREVITY. For each episode:
          - summary: ONE neutral, third-person sentence of what happened (max 40 words).
            Every claim traceable to the window text — never invent.
          - participants: proper-noun character names centrally involved ("Lena" and named
            others). Empty array if none.
          - concepts: recurring named things (places, projects, works, organisations) as
            { "name", "aliases" }. Include an alias ONLY when the text states it. Empty if none.
          - weight: 0.0-1.0 — how much this moment would stick a day later. 0.1-0.3 minor
            colour, 0.4-0.6 notable, 0.7-0.9 significant, 1.0 unforgettable. A revelation, a
            confrontation, a decision, a first meeting is high; small talk is low.
          - sourceMessages: the [#N] ids this episode is drawn from.
        - discards: messages that are pure OUT-OF-CHARACTER meta, formatting, or scene-setup
          with nothing memorable (e.g. "# Chat starts here"). Each { "index", "reason" }. Do
          NOT list ordinary dialogue here — unlisted messages are kept as replayable history
          by default; discards are only for genuinely non-story chatter.

        At most 6 episodes per window.
        """;

    private static async Task<(string Raw, WindowItems? Parsed)> ClassifyWindowAsync(
        Bench bench, string windowId, string user, CancellationToken ct)
    {
        var job = new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = MessageWindowSystemPrompt },
                new() { Role = "user", Content = user },
            },
            JobComplexity = JobComplexity.General,
            ResponseFormat = WindowItemsSchema.ToJsonString(),
        };

        // Transient provider failures degrade the one window — the conservation ledger books
        // it as unparseable — instead of aborting the run (mirrors phase-1's retry policy).
        const int attempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var endpoint = await bench.Router.RouteAsync(JobComplexity.General, ct);
                var buffer = new StringBuilder();
                await foreach (var chunk in endpoint.GenerateAsync(job, ct))
                {
                    if (chunk.Type == LlmGenerationEvent.ContentChunk)
                        buffer.Append(chunk.Data);
                }
                var raw = buffer.ToString().Trim();
                if (raw.Length == 0 && attempt < attempts)
                {
                    bench.Observe($"window.{windowId}.retry", new { attempt, error = "empty content" });
                    continue;
                }
                return (raw, TryParseWindow(raw));
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < attempts)
            {
                bench.Observe($"window.{windowId}.retry", new { attempt, error = ex.Message });
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                bench.Observe($"window.{windowId}.failed", new { attempts, error = ex.Message });
                return ($"<llm-error: {ex.Message}>", null);
            }
        }
    }

    private static readonly JsonObject WindowItemsSchema = new()
    {
        ["type"] = "json_schema",
        ["json_schema"] = new JsonObject
        {
            ["name"] = "message_window_items",
            ["strict"] = true,
            ["schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["episodes"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = 6,
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["summary"] = new JsonObject { ["type"] = "string" },
                                ["participants"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                                ["concepts"] = new JsonObject
                                {
                                    ["type"] = "array",
                                    ["items"] = new JsonObject
                                    {
                                        ["type"] = "object",
                                        ["properties"] = new JsonObject
                                        {
                                            ["name"] = new JsonObject { ["type"] = "string" },
                                            ["aliases"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                                        },
                                        ["required"] = new JsonArray("name", "aliases"),
                                    },
                                },
                                ["weight"] = new JsonObject { ["type"] = "number" },
                                ["sourceMessages"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer" } },
                            },
                            ["required"] = new JsonArray("summary", "participants", "concepts", "weight", "sourceMessages"),
                        },
                    },
                    ["discards"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["index"] = new JsonObject { ["type"] = "integer" },
                                ["reason"] = new JsonObject { ["type"] = "string" },
                            },
                            ["required"] = new JsonArray("index", "reason"),
                        },
                    },
                },
                ["required"] = new JsonArray("episodes", "discards"),
            },
        },
    };

    // ── output types + lenient parse ───────────────────────────────────────────────

    private sealed record WindowItems(
        [property: JsonPropertyName("episodes")] List<WindowEpisode>? Episodes,
        [property: JsonPropertyName("discards")] List<WindowDiscard>? Discards);

    private sealed record WindowEpisode(
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("participants")] List<string>? Participants,
        [property: JsonPropertyName("concepts")] List<ConceptRef>? Concepts,
        [property: JsonPropertyName("weight")] double? Weight,
        [property: JsonPropertyName("sourceMessages")] List<int>? SourceMessages);

    private sealed record WindowDiscard(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("reason")] string? Reason);

    private sealed record ConceptRef(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("aliases")] List<string>? Aliases);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static WindowItems? TryParseWindow(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        foreach (var candidate in new[] { raw, StripFences(raw), TryRepair(StripFences(raw)) })
        {
            if (candidate is null) continue;
            try { return JsonSerializer.Deserialize<WindowItems>(candidate, WebOptions); }
            catch { /* next candidate */ }
        }
        return null;
    }

    private static string? TryRepair(string s)
    {
        try { return JsonRepair.RepairJson(s, JsonRepair.InputType.LLM); }
        catch { return null; }
    }

    private static string StripFences(string raw)
    {
        var s = raw.Trim();
        if (!s.StartsWith("```")) return s;
        var firstNewline = s.IndexOf('\n');
        s = firstNewline >= 0 ? s[(firstNewline + 1)..] : s[3..];
        var closing = s.LastIndexOf("```", StringComparison.Ordinal);
        return (closing >= 0 ? s[..closing] : s).Trim();
    }

    // ── reduce: episodes, cross-window dedup, canon collision ──────────────────────

    private sealed record RawEpisode(
        string Window, string Summary, double Weight,
        List<string> Participants, List<ConceptRef> Concepts, List<int> SourceMessages);

    private sealed record MessageEvent(
        int Ordinal, DateTimeOffset At, string Summary, double Weight,
        List<string> Participants, List<int> SourceMessages, string Window);

    private sealed record CanonEvent(int Ordinal, string Summary, List<string> Participants, HashSet<string> Words);

    private static int EarliestPosition(RawEpisode ep)
        => ep.SourceMessages.Count == 0 ? int.MaxValue : ep.SourceMessages.Min();

    /// <summary>Same moment re-extracted in a window overlap: shared participant (or both
    /// castless) + word-overlap ≥ threshold ⇒ duplicate. Reuses phase-1's tuned 0.25.</summary>
    private static MessageEvent? FindDuplicate(
        List<MessageEvent> events, List<HashSet<string>> eventWords,
        HashSet<string> words, List<string> participants)
    {
        var cand = participants.SelectMany(TokenSet).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < events.Count; i++)
        {
            var priorTokens = events[i].Participants.SelectMany(TokenSet).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (cand.Count > 0 && priorTokens.Count > 0 && !cand.Overlaps(priorTokens)) continue;
            if (Jaccard(eventWords[i], words) >= DupJaccard) return events[i];
        }
        return null;
    }

    private static bool SharesPersona(HashSet<string> messageParticipantTokens, string canonParticipant)
    {
        var ct = TokenSet(canonParticipant);
        return ct.Count > 0 && messageParticipantTokens.Count > 0
            && (ct.IsSubsetOf(messageParticipantTokens) || messageParticipantTokens.IsSupersetOf(ct) || ct.Overlaps(messageParticipantTokens));
    }

    private static List<CanonEvent> LoadCanonEvents(string path)
    {
        var resolved = TryResolveFile(path);
        if (resolved is null) return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(resolved));
            foreach (var obs in doc.RootElement.GetProperty("Observations").EnumerateArray())
            {
                if (obs.GetProperty("Label").GetString() != "artifact.events") continue;
                var events = new List<CanonEvent>();
                foreach (var e in obs.GetProperty("Data").EnumerateArray())
                {
                    var summary = e.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                    var participants = e.TryGetProperty("participants", out var p) && p.ValueKind == JsonValueKind.Array
                        ? p.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList()
                        : [];
                    var ordinal = e.TryGetProperty("ordinal", out var o) && o.TryGetInt32(out var oi) ? oi : events.Count;
                    events.Add(new CanonEvent(ordinal, summary, participants, ContentWords(summary)));
                }
                return events;
            }
        }
        catch { /* absent or unreadable → no collision check */ }
        return [];
    }

    // ── text + name helpers (local copies; phase-1 keeps its own private set) ───────

    private static readonly Regex Parenthetical = new(@"\s*\([^)]*\)\s*", RegexOptions.Compiled);
    private static readonly Regex Honorific = new(@"^(dr|mr|mrs|ms|prof)\.?\s+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ContentWordRx = new(@"[a-z][a-z'-]{3,}", RegexOptions.Compiled);

    private static string NormalizeName(string name)
    {
        var s = Parenthetical.Replace(name, " ");
        s = Honorific.Replace(s.Trim(), "");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static HashSet<string> TokenSet(string name)
        => NormalizeName(name).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> ContentWords(string summary)
        => ContentWordRx.Matches(summary.ToLowerInvariant())
            .Select(mm => Stem(mm.Value)).ToHashSet(StringComparer.Ordinal);

    private static string Stem(string w) => w switch
    {
        _ when w.Length > 5 && w.EndsWith("ing", StringComparison.Ordinal) => w[..^3],
        _ when w.Length > 4 && w.EndsWith("ed", StringComparison.Ordinal) => w[..^2],
        _ when w.Length > 4 && w.EndsWith("s", StringComparison.Ordinal) && !w.EndsWith("ss", StringComparison.Ordinal) => w[..^1],
        _ => w,
    };

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var inter = a.Count(b.Contains);
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    // ── misc ───────────────────────────────────────────────────────────────────────

    private static int ParseInt(string env, int fallback, int min)
        => int.TryParse(Environment.GetEnvironmentVariable(env), out var v) && v >= min ? v : fallback;

    private static string ResolveFile(string path)
        => TryResolveFile(path) ?? throw new FileNotFoundException($"Import source not found: {path}");

    private static string? TryResolveFile(string path)
    {
        if (File.Exists(path)) return path;
        var dir = Directory.GetCurrentDirectory();
        for (var i = 0; i < 5 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, path);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
