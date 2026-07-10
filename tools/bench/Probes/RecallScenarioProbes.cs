using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;

namespace PartyTown.Bench.Probes;

/// <summary>
/// End-to-end memory loop across two scenes — the "does recall actually carry across a gap?"
/// review instrument. Unlike <see cref="DecisionProbes"/> (fake recollections handed straight
/// to the decision service) these drive the real capture → store → recall → decide → speak
/// path against the bench's ephemeral AGE (<c>RequiresMemory</c>), so the specifics they test
/// for live ONLY in what was remembered, never in the Scene-2 prompt.
/// <para>
/// <b>The fiction (both probes).</b> Scene 1 is a phone call: mundane greetings, Denise
/// announces a big life change (quitting her agency to open a bakery, <i>Rise &amp; Grind</i>),
/// they agree to meet for coffee (the <i>Blue Mug</i>, Thursday). Scene 2 is that coffeeshop in
/// the SAME Party (recall is Party-scoped → it reaches across Rooms): Vlad is waiting, Denise
/// walks in covered in flour but names nothing specific. The bakery's name, the career leap, and
/// the Thursday/Blue Mug plan exist only in memory — so a callback proves the loop closed.
/// </para>
/// <list type="bullet">
/// <item><see cref="Recall_LifeChangeAcrossScenes"/> — <b>scripted control.</b> Both sides of
/// Scene 1 are authored; only Vlad's single Scene-2 turn is model-driven. Cheapest, most
/// deterministic; isolates the recall callback.</item>
/// <item><see cref="Recall_LifeChangeAcrossScenes_Full"/> — <b>full live, minimal
/// orchestration.</b> Only Denise's lines are pushed; Vlad's EVERY turn (both scenes) is
/// model-generated through the real decision→speaking path. Swap the model and the whole
/// exchange's quality moves — this is the by-model comparison instrument.</item>
/// </list>
/// </summary>
public static class RecallScenarioProbes
{
    // Both personas are present in both scenes. Denise speaks the life change; Vlad observes it,
    // then recalls in Scene 2 — so the relevant arm has a real chain to walk:
    // (Vlad)-[RECOLLECTS]->(Event)-[ABOUT]->(Denise, present).
    private static readonly ParticipantView DeniseView = new(BenchCast.DeniseId, "Denise", DriverKind.LLM);
    private static readonly ParticipantView VladView = new(BenchCast.VladId, "Vlad", DriverKind.LLM);
    private static readonly ParticipantView[] Present = { DeniseView, VladView };
    private static readonly Guid[] PresentIds = { BenchCast.DeniseId, BenchCast.VladId };

    private const string CoffeeshopScenario =
        "Thursday afternoon at the Blue Mug, a cramped coffeeshop on Almond Street. Vlad arrived "
        + "early and has been nursing a black coffee in the corner. The door just opened.";

    // Denise's Scene-2 arrival, shared by both probes. Names no persona (no auto-respond shortcut)
    // and hooks the bakery ("dough since five") WITHOUT naming it — the name, the career leap, and
    // the Thursday/Blue Mug plan live only in memory.
    private const string WalkInLine =
        "*drops into the chair across from him, brushing something pale off her sleeve* sorry, "
        + "sorry — i've been up to my elbows in dough since five this morning and completely lost "
        + "track of the time.";

    // ── Probe 1: scripted control ───────────────────────────────────────────────────────────

    private const int LifeChangeMessageId = 5;    // Denise: "i'm opening a bakery … Rise & Grind"
    private const int AgreeToMeetMessageId = 8;    // Vlad: "Fine. Thursday. … the Blue Mug."

    [Probe("Scripted-control recall loop (real AGE + live session): Scene 1 is a fully-authored phone call (both sides) — greetings, Denise announces she's quitting to open 'Rise & Grind', they agree to meet at the 'Blue Mug' Thursday; both salient beats are captured. Scene 2 is that coffeeshop in the SAME Party — Vlad waits, Denise walks in naming nothing specific, and ONLY Vlad's single turn runs the real recall→decide→speak path. Cheapest variant; isolates the callback. Read scene2.vlad.recalled (did it travel, via which arm), the decision prompt's '# What you remember', and whether Vlad's line surfaces a memory-only specific. For full by-model comparison use Recall_LifeChangeAcrossScenes_Full.", RequiresMemory = true)]
    public static async Task Recall_LifeChangeAcrossScenes(Bench bench)
    {
        var ct = bench.Cancellation;
        var (party, callRoom, shopRoom) = NewScope(bench);

        // ── Scene 1: the phone call (fully scripted) ──────────────────────────────────────
        var call = Scene1Transcript(callRoom);
        bench.Observe("scene1.transcript", call.Select(m => $"{Name(m.SenderId)}: {m.Content}").ToList());

        // Production captures a moment when its message arrives, so each capture sees the
        // conversation up to and including that message — mirror that with a prefix slice.
        await CaptureAsync(bench, party, callRoom,
            call.Take(LifeChangeMessageId).ToList(), LifeChangeMessageId, "scene1.capture.lifeChange", ct);
        await CaptureAsync(bench, party, callRoom,
            call, AgreeToMeetMessageId, "scene1.capture.agreeToMeet", ct);
        await ObserveStoredGraphAsync(bench, party, ct);

        // ── Scene 2: the coffeeshop (only Vlad is live) ───────────────────────────────────
        bench.Observe("scene2.scenario", CoffeeshopScenario);
        bench.Observe("scene2.trigger (Denise walks in)", WalkInLine);
        var shopHistory = new List<ChatMessage> { UserLine(1, WalkInLine, shopRoom) };

        var recalled = await RecallForVladAsync(bench, party, WalkInLine, "scene2.vlad", ct);
        WarnIfNoRecall(bench, recalled);
        await RunVladTurnAsync(bench, shopHistory, recalled, CoffeeshopScenario, shopRoom, 2, "scene2.vlad", ct);

        ObserveJudgeHint(bench);
    }

    // ── Probe 2: full live, minimal orchestration ───────────────────────────────────────────

    /// <summary>The only authored dialogue in the full probe: Denise's turns. Each is a
    /// <c>user</c>-type statement with no "Vlad" and no trailing "?" — that keeps every beat
    /// below the 0.9 auto-respond shortcut (so the decision LLM, and thus recall-pick, runs each
    /// turn) while the cold-open floor keeps Vlad engaged in a 1:1. Specifics (Rise &amp; Grind,
    /// Blue Mug, Thursday) live in Denise's lines so capture stays deterministic and they're
    /// withheld from Scene 2. Lines react to Denise's own momentum, not to Vlad's exact words —
    /// the "assumptions within reason" that let Vlad drift without derailing the script.</summary>
    private static readonly (string Line, bool Capture)[] DeniseScript =
    {
        ("hi hi — it's Denise! god, you actually picked up. i was all set to leave a voicemail you'd ignore for a week.", false),
        ("ok i'll be quick, i know the phone's your sworn enemy. but i've got proper news, and you're the first call i'm making.", false),
        ("i quit the agency. handed my notice in on friday — fifteen years, just like that. i'm opening my own bakery. it's called Rise & Grind and i sign the lease monday.", true),
        ("anyway i can't do this justice down a phone line, so don't even argue — let's grab a coffee thursday. there's a little place on Almond Street, the Blue Mug. meet me there.", true),
    };

    [Probe("FULL live recall loop (real AGE + live session), minimal orchestration: only Denise's lines are pushed — Vlad's EVERY turn in BOTH scenes is model-generated through the real recall→decide→speak path, so swapping the model moves the whole exchange's quality (the by-model comparison instrument). Scene 1: Denise greets, announces quitting to open 'Rise & Grind', proposes the 'Blue Mug' Thursday (her two salient lines are captured); Vlad answers each live. Scene 2: same Party coffeeshop, Denise walks in naming nothing specific; Vlad recalls + answers. Read scene.transcript (Denise scripted / Vlad live) and especially Vlad's Scene-2 line: does he call back the bakery name / career leap / Thursday plan from memory ALONE? Denise's lines are user-type statements (no name, no '?') so the decision LLM runs every beat.", RequiresMemory = true)]
    public static async Task Recall_LifeChangeAcrossScenes_Full(Bench bench)
    {
        var ct = bench.Cancellation;
        var (party, callRoom, shopRoom) = NewScope(bench);

        // ── Scene 1: the phone call (Denise scripted, Vlad live) ──────────────────────────
        var history = new List<ChatMessage>();
        var id = 0;
        var turn = 0;
        foreach (var (line, capture) in DeniseScript)
        {
            turn++;
            var denise = UserLine(++id, line, callRoom);
            history.Add(denise);

            // Capture marks Denise's line the moment it "arrives" (before Vlad reacts), exactly
            // as production does — recentContext is the lead-up, the marked message is hers.
            if (capture)
                await CaptureAsync(bench, party, callRoom, history.ToList(), denise.MessageId,
                    $"scene1.t{turn}.capture", ct);

            // Vlad's turn is fully live: recall (empty early, fills after the captures), decide,
            // speak. His generated line is appended so the next beat sees a real exchange.
            var recalled = await RecallForVladAsync(bench, party, line, $"scene1.t{turn}.vlad", ct);
            var vlad = await RunVladTurnAsync(bench, history, recalled, scenario: null, callRoom,
                ++id, $"scene1.t{turn}.vlad", ct);
            if (vlad is not null)
                history.Add(vlad);
        }

        bench.Observe("scene1.transcript (Denise scripted · Vlad live)",
            history.Select(m => $"{Name(m.SenderId)}: {m.Content}").ToList());
        await ObserveStoredGraphAsync(bench, party, ct);

        // ── Scene 2: the coffeeshop (Denise scripted, Vlad live) ──────────────────────────
        bench.Observe("scene2.scenario", CoffeeshopScenario);
        bench.Observe("scene2.trigger (Denise walks in)", WalkInLine);
        var shopHistory = new List<ChatMessage> { UserLine(1, WalkInLine, shopRoom) };

        var shopRecall = await RecallForVladAsync(bench, party, WalkInLine, "scene2.vlad", ct);
        WarnIfNoRecall(bench, shopRecall);
        var reply = await RunVladTurnAsync(bench, shopHistory, shopRecall, CoffeeshopScenario,
            shopRoom, 2, "scene2.vlad", ct);

        bench.Observe("scene2.transcript (Denise scripted · Vlad live)", new[]
        {
            $"Denise: {WalkInLine}",
            reply is null ? "Vlad: (declined)" : $"Vlad: {reply.Content}",
        });
        ObserveJudgeHint(bench);
    }

    // ── Scene 1 transcript (scripted-control probe) ─────────────────────────────────────────

    /// <summary>The fully-authored phone call for the control probe, oldest→newest. Both are
    /// personas ("assistant"). The life change (msg 5) carries the bakery's name; the agreement
    /// (msg 8) carries the place and day — the specifics Scene 2 withholds.</summary>
    private static IReadOnlyList<ChatMessage> Scene1Transcript(Guid room)
    {
        string[] lines =
        {
            /* 1 Denise */ "Vlaaad! hi! it's Denise. you actually answered — i nearly fell off my chair.",
            /* 2 Vlad   */ "Denise. It is the middle of the afternoon. I was asleep. ...Hello.",
            /* 3 Denise */ "ok i'll be quick, i KNOW you hate the phone. but i have news. proper news.",
            /* 4 Vlad   */ "You have my attention. Briefly.",
            /* 5 Denise */ "i quit the agency. handed my notice in friday — fifteen years, done. i'm "
                + "opening a bakery, my own, it's called Rise & Grind and i sign the lease monday.",
            /* 6 Vlad   */ "...You're serious. Leaving the one thing you're good at, to sell croissants.",
            /* 7 Denise */ "to sell EVERYTHING, thank you. and don't be like that, you'll hurt my "
                + "sourdough's feelings. look — i can't do this justice on the phone. coffee? thursday?",
            /* 8 Vlad   */ "Fine. Thursday. There's a place on Almond Street, the Blue Mug. I'll be "
                + "in the corner, quietly regretting my own sociability.",
        };

        var senders = new[]
        {
            BenchCast.DeniseId, BenchCast.VladId, BenchCast.DeniseId, BenchCast.VladId,
            BenchCast.DeniseId, BenchCast.VladId, BenchCast.DeniseId, BenchCast.VladId,
        };

        var messages = new List<ChatMessage>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
            messages.Add(new ChatMessage
            {
                MessageId = i + 1,
                Content = lines[i],
                SenderType = "assistant",
                SenderId = senders[i],
                ChatGroupId = room,
            });
        return messages;
    }

    // ── Shared turn machinery ───────────────────────────────────────────────────────────────

    /// <summary>Runs ONE live Vlad turn — decision over the recalled set, then (if he speaks) the
    /// speaking phase with the resolved memory snippet — mirroring the ResponsePipeline handoff
    /// (decision selects by index, speaking executes). Observes the decision and the utterance
    /// under <paramref name="label"/>; returns Vlad's generated message (assistant, in
    /// <paramref name="room"/>) or null when he declined. <paramref name="recalled"/> is passed in
    /// so the caller can observe/warn on it; the composed prompts are captured automatically.</summary>
    private static async Task<ChatMessage?> RunVladTurnAsync(
        Bench bench,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<RecalledMemory> recalled,
        string? scenario,
        Guid room,
        int messageId,
        string label,
        CancellationToken ct)
    {
        var self = BenchCast.Vlad;
        var decisionService = new PersonaDecisionService(bench.Router, bench.Logger("PersonaDecisionService"));

        // totalAiRoundsInGroup = 0: a 1:1 exchange, not a group cascade. Denise's lines are
        // user-type so the trailing-assistant count is 0 anyway; passing it explicitly keeps the
        // instrument focused on response quality rather than the group spam-throttle.
        var decision = await decisionService.ShouldRespondAsync(
            self,
            history,
            Present,
            totalAiRoundsInGroup: 0,
            onEvent: (_, _, _) => Task.CompletedTask,
            cancellationToken: ct,
            scenario: scenario,
            recollections: recalled.Select(r => r.Snippet).ToList());

        // Same deployment policy as ResponsePipeline: model pick wins, salience floor
        // fires on null (RecallDeployment) — the artifact records which one chose.
        var (picked, autoDeployed) = RecallDeployment.ResolvePick(
            decision.MemoryToReference, decision.Respond, recalled);
        bench.Observe($"{label}.decision", new
        {
            decision.Respond,
            gutReaction = decision.Reason,
            wouldSay = decision.Instruction,
            memoryToReference = decision.MemoryToReference,
            pickedSnippet = picked?.Snippet,
            pickedBy = picked is null ? null : autoDeployed ? "salience-floor" : "model",
        });

        // Production parity: a pick (model or floor) fires the strengthening write, which
        // increments recall_count — the very gate that makes floor-deployment one-shot.
        // Awaited here (production fire-and-forgets) so the next turn's recall sees it.
        if (picked is not null && picked.EdgeId != Guid.Empty)
            await bench.Memory.StrengthenRecollectionAsync(picked.EdgeId, ct);

        if (!decision.Respond)
        {
            bench.Observe($"{label}.speaking", "(declined — no utterance generated)");
            return null;
        }

        var session = new SpeakingSession(bench.Router, Present);
        var result = await session.GenerateResponseOnlyAsync(
            self,
            history,
            onEvent: (_, _, _) => Task.CompletedTask,
            cancellationToken: ct,
            turnInstruction: decision.Instruction,
            scenario: scenario,
            memoryToReference: picked?.Snippet,
            gutReaction: decision.Reason);

        bench.Observe($"{label}.speaking", new
        {
            message = result.Message,
            reasoning = string.IsNullOrWhiteSpace(result.Reasoning) ? null : result.Reasoning,
            model = result.Metadata?.ModelName,
        });

        return new ChatMessage
        {
            MessageId = messageId,
            Content = result.Message ?? string.Empty,
            SenderType = "assistant",
            SenderId = self.Id,
            ChatGroupId = room,
        };
    }

    /// <summary>Recall for Vlad, wired exactly like ResponsePipeline (Party-scoped, anchored on
    /// the present cast + the triggering line, limit 10), and observed under
    /// <paramref name="label"/> with arm + salience. Pure DB read — no LLM.</summary>
    private static async Task<IReadOnlyList<RecalledMemory>> RecallForVladAsync(
        Bench bench, Guid party, string anchorText, string label, CancellationToken ct)
    {
        var recalled = await bench.Memory.RecallAsync(
            BenchCast.VladId, party, PresentIds, anchorText, limit: 10, ct);
        bench.Observe($"{label}.recalled (salience-ranked; arm = relevant|recent)",
            recalled.Select((r, i) => new
            {
                n = i + 1,
                r.Snippet,
                r.Weight,
                arm = r.Arm.ToString(),
                salience = Math.Round(r.Salience, 3),
            }).ToList());
        return recalled;
    }

    private static void WarnIfNoRecall(Bench bench, IReadOnlyList<RecalledMemory> recalled)
    {
        if (recalled.Count == 0)
            bench.Observe("WARNING",
                "Vlad recalled nothing in Scene 2 — either capture wrote no Recollections (no live "
                + "session / extractor declined) or the cross-Room recall didn't travel. The "
                + "decision below has nothing to call back; the loop is broken.");
    }

    /// <summary>Captures one marked moment and records what the extractor produced — the Event
    /// flag, the Recollection count, and which personas now carry a memory of it. Capture needs a
    /// live session (the extractor routes LLM calls); a failure is observed, not thrown, so the
    /// artifact still writes.</summary>
    private static async Task CaptureAsync(
        Bench bench,
        Guid party,
        Guid room,
        IReadOnlyList<ChatMessage> recentContext,
        int messageId,
        string label,
        CancellationToken ct)
    {
        try
        {
            var result = await bench.Memory.CaptureMomentAsync(party, room, messageId, Present, recentContext, ct);
            bench.Observe(label, new
            {
                marked = recentContext.FirstOrDefault(m => m.MessageId == messageId)?.Content,
                result.EventCreated,
                result.RecollectionsCreated,
                result.ConceptsTouched,
                remembering = result.RecollectionPersonaIds.Select(Name).ToList(),
            });
        }
        catch (Exception ex)
        {
            bench.Observe($"{label}.ERROR", new { ex.GetType().Name, ex.Message });
        }
    }

    /// <summary>Dumps the per-Party subgraph after Scene 1 — Events (neutral descriptions),
    /// Concepts (tags), and Recollection edges (who remembers what) — the reviewer's ground
    /// truth for "what was remembered" before judging "what was recalled".</summary>
    private static async Task ObserveStoredGraphAsync(Bench bench, Guid party, CancellationToken ct)
    {
        var graph = await bench.Memory.GetPartyMemoryGraphAsync(party, ct);

        bench.Observe("scene1.stored.events",
            graph.Nodes.Where(n => n.Kind == "Event").Select(n => n.Description).ToList());
        bench.Observe("scene1.stored.concepts",
            graph.Nodes.Where(n => n.Kind == "Concept").Select(n => n.Display ?? n.Id).ToList());
        bench.Observe("scene1.stored.recollections",
            graph.Links.Where(l => l.Kind == "RECOLLECTS")
                .Select(l => new { who = PersonaNameFromNodeId(l.Source), l.Snippet })
                .ToList());
    }

    private static void ObserveJudgeHint(Bench bench) => bench.Observe("judge",
        "Did the right memories surface for Vlad (life change + the plan to meet), via the relevant "
        + "arm (Event ABOUT Denise, who's present) not just recency? Did the decision pick one "
        + "(memoryToReference)? Most of all: does Vlad's final line call back a specific the prompt "
        + "never gave him — the bakery name 'Rise & Grind', 'you actually quit', or 'the Thursday we "
        + "set up'? Specifics from memory = the loop closed; a generic greeting = recall surfaced but "
        + "didn't land. In the _Full probe, also read every Vlad turn for in-character voice quality "
        + "across the model under test.");

    // ── Small builders / lookups ────────────────────────────────────────────────────────────

    /// <summary>Fresh Party + Room scopes per run keep the artifact clean against an accumulating
    /// graph if the container is ever reused; the cast ids stay fixed so output diffs across runs.</summary>
    private static (Guid Party, Guid CallRoom, Guid ShopRoom) NewScope(Bench bench)
    {
        var party = Guid.NewGuid();
        var callRoom = Guid.NewGuid();
        var shopRoom = Guid.NewGuid();
        bench.Observe("scope", new { party, callRoom, shopRoom, denise = BenchCast.DeniseId, vlad = BenchCast.VladId });
        return (party, callRoom, shopRoom);
    }

    // Denise's pushed stimulus is "user"-type: it raises the cold-open floor (keeping Vlad engaged
    // in a 1:1) and resets the trailing-assistant count each beat, while her Driver stays LLM in
    // Present so capture/recall still treat her as a remembering persona.
    private static ChatMessage UserLine(int messageId, string content, Guid room) => new()
    {
        MessageId = messageId,
        Content = content,
        SenderType = "user",
        SenderId = BenchCast.DeniseId,
        ChatGroupId = room,
    };

    private static readonly IReadOnlyDictionary<Guid, string> NamesById = new Dictionary<Guid, string>
    {
        [BenchCast.DeniseId] = "Denise",
        [BenchCast.VladId] = "Vlad",
        [BenchCast.SamId] = "Sam",
    };

    private static string Name(Guid id) => NamesById.TryGetValue(id, out var n) ? n : id.ToString("N")[..8];

    /// <summary>Recollection edges hang off a Participant node id shaped "part:{personaId}:{partyId}"
    /// (see <c>MemoryRepository.GetPartyMemoryGraphAsync</c>) — pull the persona id back out to name
    /// the rememberer.</summary>
    private static string PersonaNameFromNodeId(string nodeId)
    {
        var parts = nodeId.Split(':');
        return parts.Length >= 2 && Guid.TryParse(parts[1], out var id) ? Name(id) : nodeId;
    }
}
