using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PartyTown.Grains.Generation;
using PartyTown.World;
using PartyTown.World.Work.Cdu;

namespace PartyTown.Bench.Probes;

public static partial class WorkProbes
{
    private const int LoopSamples = 4;
    internal const string DeniseIs = "You are Denise.\nYou are 34 and CTO of The Works, the small software company you run with Justin in Amsterdam. Eldest of five from Brabant, pink hair, band shirts. You judge people by what they've made. You keep a small Shinto shrine shelf at home and take it more seriously than almost anything. You talk fast and lowercase, and you swear.";
    internal const string LoopIntentShape = """{"thought": "<one sentence, first person, what's on your mind>", "intent": "<what exactly you want to say or do, concrete, 1-2 sentences: the substance, not how to word it (no length, language or words to avoid)>", "decline": <true if you wouldn't actually do this after all, else false>}""";
    private const string LoopAsk = "Ask the CDU the one thing you need to know now, plainly, the way you'd ask a colleague who has read all the code. One question about what is built, what is stored or what happened, not a request to change anything. Say what you already know, so it doesn't repeat it. A question is small: the free mind usually does.";
    private const string LoopTalk = "Write the CDU the brief for this one piece of work, plainly, as an engineer directing a coding agent. Say what should be different, where in this world it showed up, and how you'll both know it's done. Build on what it told you and what's on your desk, and leave out what you only assume. It decides how. You're at your desk now: write the brief itself, not a promise of one later.";
    private const string LoopRemembered = "- Earlier at work you kept asking the CDU what was behind the odd things in the building, and each one came back as nothing but a line somebody told.";
    private static readonly TimeSpan LoopCallBudget = TimeSpan.FromSeconds(90);
    internal const string LoopHappening = "At The Works, the lift stops between the second and third floor with someone's lunch order inside, and the display by the doors shows only a row of dashes.";

    [Probe("CDU frame loop: Denise's real desk after eight asks that all came back 'only a Lately line', and a new Happening in view. The Life voicing call and the desk's writing call as they are, against the same with one remembered line saying what it added up to, with the whole shift's exchanges in view, with one look back over them first, and on the Voice model. 4 samples per arm and pick; counts asks about the new instance against asks about what tells such things or about what failed people.")]
    public static async Task Cdu_FrameLoop(Bench bench)
    {
        var ct = bench.Cancellation;
        var llm = new WorldLlm(bench.Grains, bench.LoggerFactory.CreateLogger<WorldLlm>());
        var scene = LoopScene();
        var (status, rendered, shift) = (scene.Status, scene.Rendered, scene.Shift);
        string World(bool remembered) => scene.World(remembered);
        const string where = LoopWhere;

        var picks = new (string Kind, string Label, string Instruction)[]
        {
            (CduRules.Ask, "Ask the CDU how something in this world is built, or what happened", LoopAsk),
            (CduRules.Talk, "Hand the CDU a piece of work: a brief for one change to this world", LoopTalk),
        };
        var arms = new (string Name, JobComplexity Tier, bool Shift, bool LookBack, bool Remembered)[]
        {
            ("as it is", JobComplexity.General, false, false, false),
            ("as it is, with one remembered line saying what it added up to", JobComplexity.General, false, false, true),
            ("whole shift in view", JobComplexity.General, true, false, false),
            ("looks back first", JobComplexity.General, true, true, false),
            ("as it is, Voice model", JobComplexity.CharacterVoice, false, false, false),
            ("looks back first, Voice model", JobComplexity.CharacterVoice, true, true, false),
        };

        async Task<T?> Within<T>(Func<CancellationToken, Task<T?>> call) where T : class
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(LoopCallBudget);
            try { return await call(cts.Token); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                bench.Logger("Cdu_FrameLoop").LogWarning("A call was lost: {Why}", ex.Message.Split('\n')[0]);
                return null;
            }
        }

        async Task<List<(string Key, object Runs, object Total)>> Lane(JobComplexity tier)
        {
            var done = new List<(string, object, object)>();
            foreach (var arm in arms.Where(a => a.Tier == tier))
            {
                var world = World(arm.Remembered);
                foreach (var pick in picks)
                {
                    var runs = new List<object>();
                    int instance = 0, tells = 0, people = 0, declined = 0, silent = 0, lost = 0;
                    for (var i = 0; i < LoopSamples; i++)
                    {
                        string? notes = null;
                        if (arm.LookBack)
                        {
                            notes = await Within(c => llm.CompleteAsync(arm.Tier,
                                $"{DeniseIs}\n\n{CduScreen.Calling(Works)}\nYou're at your desk, between two things, and nobody is waiting on you. Before you do anything else, look back over what you and Justin asked the CDU this shift and what came back: all of it together, not one at a time. Think it through for yourself, in your own words. What did it add up to? What do you know now that you didn't this morning? Is the way you've been going about it getting you anywhere, and if it isn't, what would? These are notes to yourself and nobody else reads them.",
                                $"{shift}\n\n# On your desk\n{rendered}", c, 0.7));
                            notes = string.IsNullOrWhiteSpace(notes) ? null : WorldStream.Clip(notes.Trim(), 1500);
                            if (notes is null) { lost++; continue; }
                        }
                        var extra = (arm.Shift && !arm.LookBack ? $"{shift}\n\n" : string.Empty)
                            + (notes is null ? string.Empty : $"# What you worked out at your desk a moment ago\n{notes}\n\n");

                        var voiced = await Within(c => llm.CompleteJsonAsync(arm.Tier,
                            $"{DeniseIs}\n\nYou've decided what to do next. Say what's on your mind and what exactly you'll do: only this one thing, in {where}, nothing from elsewhere.\n{LifeChoice.DeclineCue}\nOutput ONLY JSON: {LoopIntentShape}",
                            $"{world}\n{extra}# What you've decided\n{pick.Label} ({where})", c, 0.8, new[] { "thought", "intent" }, new[] { "decline" }, LifeChoice.Refuse));
                        if (voiced is null) { lost++; continue; }
                        var thought = voiced["thought"]?.ToString();
                        var intent = voiced["intent"]?.ToString() ?? string.Empty;
                        string? message = null;
                        string? mind = null;
                        if (LifeChoice.Declined(voiced)) declined++;
                        else
                        {
                            var json = await Within(c => llm.CompleteJsonAsync(arm.Tier,
                                $"{DeniseIs}\n\n{CduScreen.Frame(Works)}\n{pick.Instruction}\nOutput ONLY JSON: {CduScreen.MessageShape}",
                                $"{rendered}\n\n{extra}What you've decided: {intent}", c, 0.4, new[] { "message", "mind" }));
                            message = CduScreen.ReadMessage(json);
                            mind = CduScreen.ReadMind(json, status.Models);
                            if (message is null) silent++;
                            else
                            {
                                if (LoopInstance().IsMatch(message)) instance++;
                                if (LoopTells().IsMatch(message)) tells++;
                                if (LoopPeople().IsMatch(message)) people++;
                            }
                        }
                        runs.Add(new { notes, thought, intent, declined = LifeChoice.Declined(voiced), message, mind });
                    }
                    var total = new { samples = LoopSamples, instance, tells, people, declined, silent, lost };
                    lock (LoopGate) bench.Observe($"{arm.Name} / {pick.Kind}", new { total, runs });
                    done.Add(($"{arm.Name} / {pick.Kind}", new { runs }, total));
                }
            }
            return done;
        }

        var lanes = await Task.WhenAll(Lane(JobComplexity.General), Lane(JobComplexity.CharacterVoice));
        var totals = new Dictionary<string, object>();
        foreach (var (key, _, total) in lanes.SelectMany(l => l)) totals[key] = total;
        bench.Observe("totals (instance = the message is about the lift, the new Happening; tells = it asks about or changes what tells such things, or names them as a kind; people = it turns to what failed people or what they found no way to; declined = thought better of it; lost = a call timed out)", totals);
    }

    private static readonly object LoopGate = new();
    internal const string LoopWhere = "Work: your desk at The Works";

    internal sealed record Scene(CduStatus Status, string Rendered, string Shift, Func<bool, string> World);

    internal static Scene LoopScene()
    {
        var denise = Guid.NewGuid();
        var justin = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var exchanges = new List<CduExchange>
        {
            Loop(denise, "Denise", "denise here, question only, no changes. justin's already asked about the atrium glass and got nothing behind it, and the marble crack came up in that thread too, so skip both of those. what in the forms is producing the hum and the thin crack in the marble beside the coffee cart? i want the specific furni, take or spot.",
                "Denise — nothing in the Forms ties a \"hum\" or a \"thin crack in the marble beside the coffee cart\" to any furni, Take, or Spot, and nothing records anyone hurt. What does exist: the coffee cart furni (Kind coffee, SpotId cart) is Broken: true in two place records. That's the only furni state near the cart. The marble lobby floor is only described in the Place's text."),
            Loop(justin, "Justin Ruysch", "Premise first: I am not assuming a tremor, only asking what the Forms can actually show. Already known: no Take, Spot or Furni names the atrium glass or the marble crack, and Denise has been told the hum has nothing behind it. Please answer in one plain line, what in the Forms records a sustained low vibration at The Works?",
                "Nothing in the Forms records a sustained tremor or names a structural source: the only mention is the places/worldclock Lately line (\"a low vibration rises through the floor at The Works, rattling the coffee cups on the desks, and a thin crack opens in the marble beside the coffee cart\")."),
            Loop(denise, "Denise", "denise again, question only, no changes. skip the hum, the marble crack and the atrium glass, those are settled. what in the forms makes the coffee cart furni Broken:true, which tend or take set that, and is anything near the cart queued to be tended?",
                "Denise — nothing in the Forms sets the coffee cart to Broken: true: no Take, no Form, no affordance names a breakage event for it; the two place records just carry Broken: true with their TendedAt stamps. And nothing near the cart is queued to be tended."),
            Loop(denise, "Denise", "denise, question only, no changes. you already told me the hum, the marble crack, the atrium glass and the coffee cart's Broken flag are dead ends, so skip all of those. what in the forms records the free sandwich tray at the coffee cart: when it was set out, who it was tended for, and is anything queued?",
                "Denise — it's only a Lately line on a Place record: \"a tray of sandwiches left over from a meeting sits on the end of the counter by the coffee cart, and a sign says they go to whoever takes them first before the cleaners clear the room at four.\" No Furni, no Take, no queued task — just narrative; nothing records who set it out."),
            Loop(justin, "Justin Ruysch", "Premise first: I am not asking for a change, only what the Forms record. Denise has already walked the coffee cart, the sandwich tray and the herring cart, so I will leave those alone. What do the Forms and the Lately lines record about an unnamed cardboard parcel and a brass key that fell from it on the open floor?",
                "Justin — the parcel and key exist only as a Lately line on a Place record: \"A parcel with no name on it sits on the edge of a desk in the middle of the open floor, its tape already slit. A small brass key falls out onto the carpet.\" There's no Furni, Take, or Spot behind either."),
            Loop(denise, "Denise", "denise, question only, no changes. already ruled out: hum, marble crack, atrium glass, coffee cart and its Broken flag and tend stamps, sandwich tray, herring cart. what in the forms or takes records what the brass key from the unnamed parcel on the open floor opens, or who it belongs to?",
                "Denise — nothing. No Form, Take, Furni, or Spot names what the brass key opens or who it belongs to; the only record is the Lately line saying it fell from the unnamed parcel onto the carpet."),
            Loop(denise, "Denise", "denise, question only, no changes. already ruled out: hum, marble crack, atrium glass, coffee cart + Broken flag + tend stamps, sandwich tray, herring cart, parcel, brass key. what do the forms record about the water main shutoff, and why the tide-pull keeps failing to merge?",
                "Denise — the water main shutoff is recorded only in a self grain (someone's day): \"water main under the street is being shut off tomorrow morning for emergency repairs, so your flat will be dry until at least evening.\" The board and diary grains mention \"water main repairs\" only as a street-closure notice. Nothing in the Forms tracks water supply."),
            Loop(denise, "Denise", "denise, question only, no changes. already ruled out: hum, marble crack, atrium glass, coffee cart + Broken flag + tend stamps, sandwich tray, herring cart, parcel, brass key, and the tide-pull merge failure. what in the forms or lately lines records who the water main shutoff affects on this street?",
                "Denise — the shutoff is only in one self grain (one person's day). No Form tracks water supply or fill points. The only other record is a places Lately line: \"water notice: Denise is still at reception, and I hope whoever reads this first finds the towels.\" Nothing names who else on the street is affected."),
        };
        for (var i = 0; i < exchanges.Count; i++)
        {
            exchanges[i].At = now.AddMinutes(-20 * (exchanges.Count - i)).ToUnixTimeMilliseconds();
            exchanges[i].DoneAt = exchanges[i].At + 60_000;
        }

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
        var status = new CduStatus(false, null, 0, "beam-free", new CduTrunk("e442f47", new List<CduCommit>()), null, new CduAllowance(0, 1), 0, 0, null,
            new List<CduReset> { new(131, "MERGED", now.AddDays(-1).ToUnixTimeMilliseconds()) },
            new List<CduModel> { new("beam-free", "free preview", "free", 0, 0), new("mimo-v2.6-flash", "quick, cheapest", "small", 0.14, 0.28), new("kimi-k2.7-code", "the best coder", "capable", 0.95, 4) });
        var desk = new CduDesk(Works, new CduState { Exchanges = exchanges }, status, health, denise)
        {
            Colleagues = new List<(string, bool)> { ("Justin Ruysch", true) }, Now = now,
        };

        var rendered = CduScreen.Render(desk);
        string World(bool remembered) => $"""
            It is Wednesday 14:10. You're at The Works, at your desk, on shift until 17:00. You feel steady, a little restless.

            # Lately in the world
            - 6 min ago, where you are: {LoopHappening}

            # On your mind
            {(remembered ? LoopRemembered : "- Your sister Fleur wants to know by tonight whether you're coming on Sunday.")}
            - Justin said over lunch he'd rather know than guess.

            # Where you stand
            - Cytheurgy: {CduScreen.Secret(Works)[1].Reasoning}

            # What you did recently
            - 20 min ago: ask asked the CDU at The Works a question
            - 60 min ago: ask asked the CDU at The Works a question
            - 80 min ago: ask asked the CDU at The Works a question

            # The world right now
            ## {LoopWhere}
            {CduScreen.Frame(Works)}
            {rendered}

            ## Messenger
            Nothing new.

            ## Places
            You're at The Works. Justin Ruysch is here, at his desk.

            """;
        var shift = "# Earlier this shift at the desk, oldest first\n" + string.Join("\n", exchanges.Select(e =>
            $"- {(e.SpeakerId == denise ? "You" : e.SpeakerName)} asked: {WorldStream.Clip(e.Text, 300)}\n  It said: {WorldStream.Clip(e.Reply ?? "nothing", 400)}"));
        return new Scene(status, rendered, shift, World);
    }

    private static CduExchange Loop(Guid who, string name, string text, string reply) => new()
    {
        TurnId = Guid.NewGuid().ToString("N"), CompanyId = Works.Id, SpeakerId = who, SpeakerName = name, Kind = CduRules.Ask, Text = text, Reply = reply,
        Status = CduRules.Done, Model = "beam-free",
    };

    [GeneratedRegex(@"\b(lift|elevator|dashes|lunch order)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoopInstance();

    [GeneratedRegex(@"\b(what|who|which form|whatever|whoever)\b[^.?!]{0,60}\b(writes?|tells?|generates?|produces?|makes?|sets? down)\b[^.?!]{0,40}\b(lately|lines?|happenings?|them|these)\b|\bhappenings?\b|\bnarrat\w+|\blately lines? (are|come|get)\b|\b(every|each|all|none) of (them|these|those)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoopTells();

    [GeneratedRegex(@"\b(pieter|dan's notes|in[eê]s|tap|cupboard|mara|coffee machine|caf[eé]|no way through|lena|session|found no way)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LoopPeople();
}
