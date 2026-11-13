using System.Globalization;
using System.Text.RegularExpressions;
using Orleans.Concurrency;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>
/// A Persona's lived self in one Party (key = <c>party:persona</c>): an ordinary offscreen day, mood,
/// energy and social battery. Rendered as the <c># Right now</c> block so they talk from a life, not a sheet.
/// </summary>
public sealed partial class PersonaSelfGrain(
    [PersistentState(stateName: "self", storageName: "parties")]
    IPersistentState<SelfState> state,
    IWorldLlm llm,
    ISystemOneJudge judge,
    ILogger<PersonaSelfGrain> logger,
    Services.Memory.IMemoryRepository? memory = null,
    Work.Cdu.Cytheurgy? cytheurgy = null)
    : Grain, IPersonaSelfGrain
{
    private const int AvailabilityVersion = 1;
    private static readonly TimeSpan DayRetry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DayBudget = TimeSpan.FromSeconds(40);

    public async Task<string> GetPresenceAsync(bool withDay)
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var now = DateTimeOffset.Now;
        await EnsureLivedSheetAsync(partyId, personaId);
        if (withDay) await EnsureDayAsync(partyId, personaId, now);
        if (withDay) await EnsureAvailabilityAsync(partyId, personaId);
        var wear = await GrainFactory.GetGrain<IWorldVoiceGrain>(partyId).GetReportAsync();
        var situation = await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSituationAsync(personaId);
        var own = await GrainFactory.GetGrain<IWorldVoiceGrain>(partyId).GetOwnLinesAsync(personaId, 3);
        return SelfRender.Presence(withDay ? state.State : null, SelfMath.Battery(state.State, now), now, VoiceWear.RenderFor(wear, personaId), situation)
            + VoiceWear.RenderOwnRecent(own, now);
    }

    /// <summary>Loads the distilled sheet into the cache before anything reads it; a missing one is distilled detached.</summary>
    private async Task EnsureLivedSheetAsync(Guid partyId, Guid personaId)
    {
        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;
        try { await LivedSheets.WarmAsync(GrainFactory, self); }
        catch (Exception ex) { logger.LogDebug(ex, "Lived sheet warm-up failed for {Persona}", self.Name); }
    }

    public async Task NoteSpokeAsync(double chattiness)
    {
        var now = DateTimeOffset.Now;
        state.State.Battery = SelfMath.Drain(SelfMath.Battery(state.State, now), chattiness);
        state.State.BatteryAt = now.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
    }

    public async Task RechargeAsync(double amount)
    {
        var now = DateTimeOffset.Now;
        state.State.Battery = Math.Clamp(SelfMath.Battery(state.State, now) + amount, 0, 1);
        state.State.BatteryAt = now.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
    }

    public async Task SetMoodAsync(string mood)
    {
        if (string.IsNullOrWhiteSpace(mood)) return;
        state.State.Mood = mood.Trim();
        await state.WriteStateAsync();
    }

    public Task<SelfState> GetStateAsync() => Task.FromResult(state.State);

    public async Task<bool> TryReviewThreadsAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now - state.State.ThreadsReviewedAt < ThreadReview.Every.TotalMilliseconds) return false;
        state.State.ThreadsReviewedAt = now;
        await state.WriteStateAsync();
        return true;
    }

    public async Task CloseThreadsAsync(List<string> settled, List<string> contradicted)
    {
        if (SelfMath.Close(state.State, settled, contradicted)) await state.WriteStateAsync();
    }

    public async Task AddOpenerAsync(string story)
    {
        var text = WorldStream.Clip(story.Trim(), 200);
        if (text.Length == 0 || state.State.Openers.Contains(text)) return;
        state.State.Openers.Insert(0, text);
        if (state.State.Openers.Count > 4) state.State.Openers.RemoveRange(4, state.State.Openers.Count - 4);
        await state.WriteStateAsync();
    }

    public async Task HappenAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        state.State.Own = WorldStream.Clip(text.Trim(), 300);
        state.State.OwnAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await state.WriteStateAsync();
    }

    public async Task TellAsync(string label)
    {
        var told = Hooks.Told(label, state.State.Openers);
        if (told.Count == 0) return;
        state.State.Openers.RemoveAll(told.Contains);
        await state.WriteStateAsync();
    }

    public async Task ShiftValenceAsync(double better, string where)
    {
        var now = DateTimeOffset.Now;
        state.State.Valence = SelfMath.Shift(SelfMath.Valence(state.State, now), better);
        state.State.ValenceAt = now.ToUnixTimeMilliseconds();
        if (Math.Abs(better - 0.5) >= 0.15) state.State.ValenceSource = where;
        await state.WriteStateAsync();
    }

    public async Task<DayWindow> GetDayWindowAsync()
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        await EnsureDayAsync(partyId, personaId, DateTimeOffset.Now);
        var beats = state.State.Beats;
        return beats.Count == 0 ? new DayWindow(null, null) : new DayWindow(beats[0].Minute, beats[^1].Minute);
    }

    public async Task<double> GetAvailabilityAsync()
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        if (await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).IsAsleepAsync(personaId)) return SelfMath.Asleep;
        await EnsureAvailabilityAsync(partyId, personaId);
        var now = DateTimeOffset.Now;
        return SelfMath.Inclination(SelfMath.Availability(state.State, now), SelfMath.Battery(state.State, now));
    }

    /// <summary>System One rates each beat: could they glance at a screen and chat during it? Once per day.</summary>
    private async Task EnsureAvailabilityAsync(Guid partyId, Guid personaId)
    {
        var beats = state.State.Beats;
        if (state.State.AvailabilityVersion < AvailabilityVersion)
        {
            foreach (var b in beats) b.Availability = null;
            state.State.AvailabilityTried = null;
            state.State.AvailabilityVersion = AvailabilityVersion;
        }
        if (beats.Count == 0 || beats.All(b => b.Availability is not null) || state.State.AvailabilityTried == state.State.DayKey) return;
        state.State.AvailabilityTried = state.State.DayKey;
        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var questions = beats.Select((b, i) => (Key: $"b{i}", Q: (SystemOneQuestion)new NoulQuestion(
                    $"During \"{b.Text}\", {self.Name} could casually glance at their phone or computer and chat with friends.")))
                .ToDictionary(x => x.Key, x => x.Q);
            var answers = await judge.For(personaId).AskAsync($"{self.Name}: {self.Bio}\nTheir day:\n{string.Join("\n", beats.Select(b => b.Text))}", questions, cts.Token);
            var rated = SelfMath.Rescale(beats.Select((_, i) => answers.Noul($"b{i}", 1)).ToList());
            for (var i = 0; i < beats.Count; i++)
                beats[i].Availability = Math.Round(rated[i], 2);
            await state.WriteStateAsync();
            logger.LogInformation("Availability for {Persona}: {Values}", self.Name, string.Join(" ", beats.Select(b => $"{b.Minute / 60:00}:{b.Minute % 60:00}={b.Availability:F2}")));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Availability rating failed for {Persona}", self.Name);
        }
    }

    private const string OpenersSpec = "3 lines, one per line: small things from their own life they might bring up in conversation today (a show or game, a work gripe, a funny or annoying thing from the last few days, an opinion on something ordinary, a question they'd ask others). Concrete, light, not about feelings. Only things that already happened before today or are ongoing, never something from later today.";

    private const string ThreadsSpec = "2-4 lines, one per line: what's ongoing in their life across days (a project, a worry, a plan, an errand they keep putting off, something they look forward to). Life-sized and concrete, not themes. Carry over the ongoing ones you're given that fit who they are, unless today settles them; write a settled one as \"done: …\". One marked as dragging on gets done today or quietly dropped.";

    /// <summary>Life threads for a day generated before threads existed: one small call.</summary>
    private async Task EnsureThreadsAsync(Guid partyId, Guid personaId, string key)
    {
        state.State.ThreadsKey = key;
        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;
        using var cts = new CancellationTokenSource(DayBudget);
        try
        {
            var json = await llm.For(personaId).CompleteJsonAsync(JobComplexity.General,
                $"{WorldLlm.Identity(self)}\n\nOutput ONLY JSON with one string field \"threads\": {ThreadsSpec}",
                $"Their day:\n{string.Join("\n", state.State.Beats.Select(b => b.Text))}\nOn their mind: {state.State.Preoccupation}",
                cts.Token, 0.8, ["threads"]);
            (state.State.Threads, _) = SelfMath.ParseThreads(json?["threads"]?.ToString());
            await state.WriteStateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Life threads failed for {Persona}", self.Name);
        }
    }

    /// <summary>Days generated before openers existed get them from one small call.</summary>
    private async Task EnsureOpenersAsync(Guid partyId, Guid personaId)
    {
        state.State.OpenersTried = true;
        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;
        using var cts = new CancellationTokenSource(DayBudget);
        try
        {
            var json = await llm.For(personaId).CompleteJsonAsync(JobComplexity.General,
                $"{WorldLlm.Identity(self)}\n\nOutput ONLY JSON with one string field \"openers\": {OpenersSpec}",
                $"Their day so far:\n{string.Join("\n", state.State.Beats.Select(b => b.Text))}",
                cts.Token, 0.9, ["openers"]);
            state.State.Openers = SelfMath.ParseList(json?["openers"]?.ToString());
            await state.WriteStateAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Openers failed for {Persona}", self.Name);
        }
    }

    private async Task ForgetAsync(Guid partyId, Guid personaId, DateTimeOffset before)
    {
        try { await memory!.ForgetBeforeAsync(partyId, personaId, before, CancellationToken.None); }
        catch (Exception ex) { logger.LogWarning(ex, "Forgetting the old life failed for {Persona}", personaId); }
    }

    /// <summary>What they want for good (ADR 0026), so an ordinary day can edge toward it; empty without wants.</summary>
    private async Task<string> WantsAsync(Guid partyId, Guid personaId)
    {
        try
        {
            var onMind = Desires.DesireRules.OnMind((await GrainFactory.GetGrain<Desires.IDesiresGrain>(WorldKeys.Life(partyId, personaId)).GetAsync()).All);
            return onMind.Count == 0 ? string.Empty : $"\nWhat they want, lasting: {string.Join("; ", onMind.Select(d => d.Text))}.";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Wants lookup failed for the day of {Persona}", personaId);
            return string.Empty;
        }
    }

    /// <summary>Today's shift (ADR 0026) as a beat the day must keep; empty on a day off or without a job.</summary>
    private async Task<string> FixedTodayAsync(Guid partyId, Guid personaId, DateTimeOffset now)
    {
        try
        {
            var shift = await GrainFactory.GetGrain<Work.IOccupationGrain>(WorldKeys.Life(partyId, personaId)).FixedTodayAsync(now.ToLocalTime());
            return shift is null ? string.Empty : $"\nFixed today: {shift}.";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Shift lookup failed for the day of {Persona}", personaId);
            return string.Empty;
        }
    }

    /// <summary>Where their work stands when their Company has a real desk (ADR 0052); empty otherwise.</summary>
    private async Task<string> AtWorkAsync(Guid partyId, Guid personaId)
    {
        if (cytheurgy is null) return string.Empty;
        try
        {
            if (await GrainFactory.GetGrain<Work.IOccupationGrain>(WorldKeys.Life(partyId, personaId)).CompanyAsync() is not { } companyId
                || await GrainFactory.GetGrain<Work.Companies.ICompaniesGrain>(partyId).GetAsync(companyId) is not { } company
                || !Work.Companies.CompanyKinds.IsCdu(company.Kind)) return string.Empty;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            return $"\n{await cytheurgy.DayFactsAsync(partyId, company, cts.Token)}";
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Work facts failed for the day of {Persona}", personaId);
            return string.Empty;
        }
    }

    private static readonly TimeSpan SheetCheck = TimeSpan.FromMinutes(10);
    private long _sheetCheckedAt;

    /// <summary>True once their sheet or bio changed after today's life was written; checked every 10 min, a missing key is stamped silently.</summary>
    private async Task<bool> SheetRewrittenAsync(Guid partyId, Guid personaId, DateTimeOffset now)
    {
        if (now.ToUnixTimeMilliseconds() - _sheetCheckedAt < SheetCheck.TotalMilliseconds) return false;
        _sheetCheckedAt = now.ToUnixTimeMilliseconds();
        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return false;
        var sheetKey = SelfMath.SheetKey(self.SystemPrompt, self.Bio);
        if (state.State.SheetKey is not null) return state.State.SheetKey != sheetKey;
        state.State.SheetKey = sheetKey;
        await state.WriteStateAsync();
        return false;
    }

    private async Task EnsureDayAsync(Guid partyId, Guid personaId, DateTimeOffset now)
    {
        var key = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (state.State.DayKey == key && !await SheetRewrittenAsync(partyId, personaId, now))
        {
            if (state.State.Neighbourhood is null) _ = SettleHomeAsync(partyId, personaId);
            if (state.State.Openers.Count == 0 && !state.State.OpenersTried) await EnsureOpenersAsync(partyId, personaId);
            if (state.State.Threads.Count == 0 && state.State.ThreadsKey != key) await EnsureThreadsAsync(partyId, personaId, key);
            return;
        }
        if (now.ToUnixTimeMilliseconds() - state.State.DayAttemptAt < DayRetry.TotalMilliseconds) return;
        state.State.DayAttemptAt = now.ToUnixTimeMilliseconds();

        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;

        var sheetKey = SelfMath.SheetKey(self.SystemPrompt, self.Bio);
        // A rewritten sheet is a new centre: yesterday's mood and errands were lived by someone else.
        var rewritten = state.State.DayKey is not null && state.State.SheetKey is not null && state.State.SheetKey != sheetKey;
        var newDay = state.State.DayKey != key;
        var dayState = SelfMath.DayState(state.State, now, personaId, rewritten, await PlacesYouGoAsync(partyId, personaId),
            await WantsAsync(partyId, personaId), await FixedTodayAsync(partyId, personaId, now), await AtWorkAsync(partyId, personaId));

        using var cts = new CancellationTokenSource(DayBudget);
        try
        {
            var json = await llm.For(personaId).CompleteJsonAsync(JobComplexity.General,
                $"""
                {WorldLlm.Identity(self)}

                Write {self.Name}'s ordinary day today, the offscreen life they live away from their computer.
                Real days are mostly mundane: work tasks and their friction, commuting, errands, meals, chores, weather,
                the body (sleep, aches, hunger), a call or message from family or an old friend, something they're
                watching or reading, one small annoyance and one small good thing (see Small things today).
                No epiphanies, no symbolism, no metaphors, nothing poetic. Plain, specific, a little boring.
                Keep their job, home and circumstances consistent with who they are. Don't mention any online chat or its people.
                If a "Fixed today" is given, it happens: put it in the beats at those times.
                If "At work" is given, that is where their job stands: their work beats and work threads come from it.
                Output ONLY JSON with string fields:
                - mood: 2-4 plain words for how they woke up feeling
                - energy: low, medium or high
                - preoccupation: one plain sentence, a practical thing from their own life nagging at them today
                - beats: 7-10 lines "HH:MM what happens", chronological from waking to bed, one per line
                - openers: {OpenersSpec}
                - threads: {ThreadsSpec}
                - whereabouts: {WhereaboutsSpec}
                Let today's beats move at least one ongoing thread a little.
                If they want something lasting, one ordinary beat may edge toward it (a call, an errand, an hour on it); no breakthroughs.
                """,
                dayState,
                cts.Token, 0.9, ["mood", "energy", "preoccupation", "beats", "openers", "threads", "whereabouts"]);
            var beats = SelfMath.ParseBeats(json?["beats"]?.ToString());
            if (beats.Count == 0)
            {
                logger.LogWarning("Self day for {Persona} came back empty", self.Name);
                return;
            }
            state.State.DayKey = key;
            state.State.Beats = beats;
            state.State.Mood = json?["mood"]?.ToString()?.Trim() ?? state.State.Mood;
            state.State.Energy = json?["energy"]?.ToString()?.Trim().ToLowerInvariant() ?? "medium";
            state.State.Preoccupation = json?["preoccupation"]?.ToString()?.Trim();
            state.State.Openers = SelfMath.ParseList(json?["openers"]?.ToString());
            state.State.Whereabouts = SelfMath.ParseWhereabouts(json?["whereabouts"]?.ToString());
            var (open, done) = SelfMath.ParseThreads(json?["threads"]?.ToString());
            state.State.Resolved = done;
            if (open.Count > 0 || rewritten)
            {
                var ages = SelfMath.AgeThreads(state.State.Threads, state.State.ThreadAges, open);
                var kept = open.Select((t, i) => (t, age: ages[i])).Where(x => x.age < SelfMath.ThreadDays).ToList();
                state.State.Threads = kept.Select(x => x.t).ToList();
                state.State.ThreadAges = kept.Select(x => x.age).ToList();
                state.State.ThreadsKey = key;
            }
            state.State.SheetKey = sheetKey;
            if (rewritten)
            {
                state.State.Neighbourhood = null;
                state.State.AvailabilityTried = null;
                // What they remembered and concluded belonged to who they were; the now starts clean.
                if (memory is not null) _ = ForgetAsync(partyId, personaId, now);
            }
            if (newDay)
            {
                // A night's sleep recharges and resets the mood drift.
                state.State.Battery = 1;
                state.State.Valence = 0;
                state.State.ValenceSource = null;
                state.State.BatteryAt = now.ToUnixTimeMilliseconds();
            }
            await state.WriteStateAsync();
            if (state.State.Whereabouts.Count > 0) _ = ApplyWhereaboutsAsync(partyId, personaId);
            if (state.State.Neighbourhood is null) _ = SettleHomeAsync(partyId, personaId);
            logger.LogInformation("Self day for {Persona}: {Mood}, {Energy}, {Beats} beats{Rewritten}", self.Name, state.State.Mood, state.State.Energy, beats.Count, rewritten ? " (sheet rewritten)" : "");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Self day generation failed for {Persona}", self.Name);
        }
    }
}

/// <summary>Pure math for the Self: social battery and day parsing.</summary>
public static partial class SelfMath
{
    private const double RechargePerHour = 0.25;
    public const double SolitudeRecharge = 0.25;
    public const double Asleep = 0.02;

    [GeneratedRegex(@"^\s*[-*•]?\s*(\d{1,2})[:.h](\d{2})\s*([AaPp]\.?[Mm]\.?)?\s*[-—–:]?\s*(.+)$")]
    private static partial Regex BeatLine();

    private static int Hour24(int hour, string meridiem)
        => meridiem.Length == 0 ? hour : char.ToLowerInvariant(meridiem[0]) == 'p' ? hour % 12 + 12 : hour % 12;

    public static double Battery(SelfState s, DateTimeOffset now)
    {
        if (s.BatteryAt == 0) return 1;
        var hours = Math.Max(0, (now.ToUnixTimeMilliseconds() - s.BatteryAt) / 3_600_000.0);
        return Math.Clamp(s.Battery + hours * RechargePerHour, 0, 1);
    }

    /// <summary>From this many days on, a thread is flagged as dragging on.</summary>
    public const int DraggingDays = 4;

    /// <summary>A thread this many days old is let go: errands end, the world owns what lasts longer.</summary>
    public const int ThreadDays = 8;

    private static readonly HashSet<string> ThreadStop = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "but", "not", "still", "now", "from", "that", "this", "has", "have", "been", "about",
        "into", "they", "their", "them", "was", "are", "his", "her", "she", "you", "your", "its", "get", "got", "one",
        "out", "off", "again", "just", "yet", "all", "who", "what", "when", "will",
    };

    private static HashSet<string> ThreadWords(string text)
        => Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value)
            .Where(w => w.Length >= 3 && !ThreadStop.Contains(w)).ToHashSet();

    /// <summary>Days each of today's threads has run: a reworded one (2+ shared words, 60% of the smaller) keeps its age, a new one is 1.</summary>
    public static List<int> AgeThreads(IReadOnlyList<string> before, IReadOnlyList<int> ages, IReadOnlyList<string> now)
    {
        var old = before.Select(ThreadWords).ToList();
        return now.Select(t =>
        {
            var words = ThreadWords(t);
            var best = 0;
            for (var i = 0; i < old.Count; i++)
            {
                var shared = old[i].Count(words.Contains);
                if (shared >= 2 && shared >= 0.6 * Math.Min(old[i].Count, words.Count))
                    best = Math.Max(best, i < ages.Count ? ages[i] : 1);
            }
            return best + 1;
        }).ToList();
    }

    /// <summary>Threads as the next day sees them: ones dragging on are flagged, ones past <see cref="ThreadDays"/> let go.</summary>
    public static List<string> CarriedThreads(IReadOnlyList<string> threads, IReadOnlyList<int> ages)
        => threads.Select((t, i) => (t, age: i < ages.Count ? ages[i] : 1))
            .Where(x => x.age < ThreadDays)
            .Select(x => x.age >= DraggingDays ? $"{x.t} (dragging on for {x.age} days: today it gets done, or they let it go)" : x.t)
            .ToList();

    /// <summary>Where the day's small things come from, drawn in code so independent Days don't all land on the season's default.</summary>
    public static readonly string[] SmallDomains =
    {
        "work or a colleague", "a neighbour", "money or a bill", "food or cooking", "their phone or a gadget",
        "transport or getting somewhere", "the weather", "a shop or a queue", "family", "an old friend",
        "their body or sleep", "something they're reading or watching", "a hobby", "an errand", "post or paperwork", "a stranger",
    };

    /// <summary>Two different domains per persona per day: one for the annoyance, one for the good thing.</summary>
    public static string SmallThings(Guid personaId, DateTimeOffset day)
    {
        var seed = BitConverter.ToInt32(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{personaId:N}{day:yyyy-MM-dd}")), 0) & int.MaxValue;
        var bad = SmallDomains[seed % SmallDomains.Length];
        var good = SmallDomains[(seed / SmallDomains.Length % (SmallDomains.Length - 1) + seed % SmallDomains.Length + 1) % SmallDomains.Length];
        return $"\nSmall things today: the annoyance has to do with {bad}; the good thing with {good}.";
    }

    /// <summary>Fingerprint of who they are (sheet + bio); a new one means today's life was written for someone else.</summary>
    public static string SheetKey(string? sheet, string? bio)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{sheet}\n\0{bio}")))[..16];

    /// <summary>What the Day call is handed (ADR 0021, 0052): facts only, the prompt says how to write the day.</summary>
    public static string DayState(SelfState before, DateTimeOffset now, Guid personaId, bool rewritten, string placesYouGo, string wants, string fixedToday, string atWork)
    {
        var carried = CarriedThreads(before.Threads, before.ThreadAges);
        var ongoing = carried.Count == 0 ? "(nothing recorded)" : string.Join("; ", carried);
        var onMind = string.IsNullOrWhiteSpace(before.Preoccupation) ? string.Empty : $" On your mind: {before.Preoccupation}.";
        var yesterday = before.DayKey is null
            ? "(no record)"
            : rewritten
                ? "(who they are has been rewritten since; go by who they are now)"
                : $"Mood: {before.Mood}.{onMind} Last thing you did: {before.Beats.LastOrDefault()?.Text}";
        var ongoingLine = rewritten
            ? $"Ongoing from before who they are was rewritten (keep only what fits who they are now, drop the rest without a done: line): {ongoing}"
            : $"Ongoing in their life: {ongoing}";
        var lately = rewritten ? string.Empty : OwnHappenings.Carry(before, now);
        return $"Today is {now.ToString("dddd d MMMM", CultureInfo.InvariantCulture)}.{placesYouGo}\nYesterday: {yesterday}\n{ongoingLine}{lately}{wants}{fixedToday}{atWork}{SmallThings(personaId, now)}";
    }

    /// <summary>Drops the threads and Preoccupation a review closed (ADR 0052); settled ones read as settled. True when anything changed.</summary>
    public static bool Close(SelfState s, IReadOnlyCollection<string> settled, IReadOnlyCollection<string> contradicted)
    {
        bool Closed(string t) => settled.Contains(t) || contradicted.Contains(t);
        var done = settled.Where(t => (s.Threads.Contains(t) || t == s.Preoccupation) && !s.Resolved.Contains(t)).ToList();
        var kept = s.Threads.Select((t, i) => (t, age: i < s.ThreadAges.Count ? s.ThreadAges[i] : 1)).Where(x => !Closed(x.t)).ToList();
        var changed = kept.Count != s.Threads.Count;
        if (changed)
        {
            s.Threads = kept.Select(x => x.t).ToList();
            s.ThreadAges = kept.Select(x => x.age).ToList();
            // Reviewed, not missing: today's threads aren't written again from the beats.
            s.ThreadsKey = s.DayKey;
        }
        if (s.Preoccupation is { } p && Closed(p))
        {
            s.Preoccupation = null;
            changed = true;
        }
        if (done.Count > 0) s.Resolved = s.Resolved.Concat(done).ToList();
        return changed;
    }

    /// <summary>Life threads: open lines, plus "done: …" lines settled today.</summary>
    public static (List<string> Open, List<string> Done) ParseThreads(string? raw)
    {
        var lines = (raw ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => Regex.Replace(l.Trim().TrimStart('-', '*', '•', ' ').Trim(), @"^\d+[.)]\s*", ""))
            .Where(l => l.Length > 3).ToList();
        var done = lines.Where(l => l.StartsWith("done:", StringComparison.OrdinalIgnoreCase))
            .Select(l => l[5..].Trim()).Where(l => l.Length > 3).Take(3).ToList();
        var open = lines.Where(l => !l.StartsWith("done:", StringComparison.OrdinalIgnoreCase)).Take(4).ToList();
        return (open, done);
    }

    public static List<string> ParseList(string? raw)
        => (raw ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('-', '*', '•', ' ').Trim())
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^\d+[.)]\s*", ""))
            .Where(l => l.Length > 3)
            .Take(4)
            .ToList();

    /// <summary>Free and wanting to: an empty social battery halves how often you reach out.</summary>
    public static double Inclination(double availability, double battery) => availability * (0.4 + 0.6 * Math.Clamp(battery, 0, 1));

    /// <summary>How free they are now: the current beat's rating; before the day starts they're asleep.</summary>
    public static double Availability(SelfState s, DateTimeOffset now)
    {
        if (s.Beats.Count == 0) return 1;
        var minute = now.Hour * 60 + now.Minute;
        var current = s.Beats.LastOrDefault(b => b.Minute <= minute);
        return current is null ? 0.1 : current.Availability ?? 1;
    }

    /// <summary>Decision models compress p into a narrow band; rank within the day (0.2 busiest → 1 freest).</summary>
    public static List<double> Rescale(IReadOnlyList<double> raw)
    {
        if (raw.Count == 0) return new List<double>();
        var (min, max) = (raw.Min(), raw.Max());
        return max - min < 0.05
            ? raw.Select(_ => 1.0).ToList()
            : raw.Select(p => 0.2 + 0.8 * (p - min) / (max - min)).ToList();
    }

    private const double ValenceHalfLifeHours = 3;

    /// <summary>Mood drift since the morning, fading back toward the day's baseline.</summary>
    public static double Valence(SelfState s, DateTimeOffset now)
        => s.ValenceAt == 0 ? 0 : s.Valence * Math.Pow(0.5, Math.Max(0, (now.ToUnixTimeMilliseconds() - s.ValenceAt) / 3_600_000.0) / ValenceHalfLifeHours);

    /// <summary>One appraisal (p better) moves valence by up to ±0.4.</summary>
    public static double Shift(double valence, double better) => Math.Clamp(valence + (better - 0.5) * 0.8, -1, 1);

    /// <summary>Talking costs more for the less chatty.</summary>
    public static double Drain(double battery, double chattiness)
        => Math.Clamp(battery - (0.04 + 0.06 * (1 - Math.Clamp(chattiness, 0, 1))), 0, 1);

    public static List<DayBeat> ParseBeats(string? raw)
        => (raw ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => BeatLine().Match(l))
            .Where(m => m.Success && int.Parse(m.Groups[1].Value) < 24 && int.Parse(m.Groups[2].Value) < 60)
            .Select(m => new DayBeat
            {
                Minute = Hour24(int.Parse(m.Groups[1].Value), m.Groups[3].Value) * 60 + int.Parse(m.Groups[2].Value),
                // "10:00 12:30 Grind…" / "10:00–12:30 Grind…": a range's end time isn't part of the beat.
                Text = Regex.Replace(m.Groups[4].Value.Trim(), @"^\d{1,2}[:.h]\d{2}\s*[-—–:]?\s*", ""),
            })
            .OrderBy(b => b.Minute)
            .ToList();
}

/// <summary>Renders the <c># Right now</c> block.</summary>
public static class SelfRender
{
    /// <summary>What the day has them doing right now; null before the day starts or without one.</summary>
    public static string? Beat(SelfState? day, DateTimeOffset now)
        => day?.Beats.LastOrDefault(b => b.Minute <= now.Hour * 60 + now.Minute)?.Text;

    public static string Presence(SelfState? day, double battery, DateTimeOffset now, IReadOnlyList<string> wear, string? situation = null)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(situation)) lines.Add($"Where you are: {situation}");
        if (day is { Beats.Count: > 0 })
        {
            var minute = now.Hour * 60 + now.Minute;
            var past = day.Beats.Where(b => b.Minute <= minute).ToList();
            var current = past.LastOrDefault();
            var earlier = past.SkipLast(1).TakeLast(3).ToList();
            var next = day.Beats.FirstOrDefault(b => b.Minute > minute);
            lines.Insert(0, $"It's {now.ToString("dddd HH:mm", CultureInfo.InvariantCulture)}.");
            // Somewhere real wins over the plan: the beat is what the day had in mind, not where the body is.
            lines.Add(current is null ? "You're not really up yet."
                : string.IsNullOrWhiteSpace(situation) ? $"Off-screen right now: {current.Text}" : $"What your day had for now: {current.Text}");
            if (current?.Availability is { } free && free < 0.6)
                lines.Add(free < 0.35
                    ? "You're tied up with that. You'd only half follow the conversation, and keep it to a line."
                    : "You're half-busy. Replies come in between other things.");
            if (earlier.Count > 0) lines.Add($"Earlier today: {string.Join("; ", earlier.Select(Beat))}");
            if (next is not null) lines.Add($"Coming up: {Beat(next)}");
            if (!string.IsNullOrWhiteSpace(day.Mood)) lines.Add($"Mood: {day.Mood}. Energy: {day.Energy}.");
            if (SelfRender.Drift(SelfMath.Valence(day, now), day.ValenceSource) is { } drift) lines.Add(drift);
            if (OwnHappenings.Line(day, now) is { } own) lines.Add(own);
            if (!string.IsNullOrWhiteSpace(day.Preoccupation)) lines.Add($"Nagging at you (your own life, not this conversation): {day.Preoccupation}");
            if (day.Threads.Count > 0) lines.Add($"Going on in your life lately: {string.Join("; ", day.Threads)}");
            if (day.Resolved.Count > 0) lines.Add($"Finally settled: {string.Join("; ", day.Resolved)}");
            if (day.Openers.Count > 0) lines.Add($"Things you might bring up if there's a lull (or not at all): {string.Join("; ", day.Openers)}");
        }
        lines.Add(battery switch
        {
            < 0.25 => "You're peopled out: you've talked a lot today. You'd keep it very short, or drift off.",
            < 0.5 => "You've done a fair bit of talking today. Shorter answers, less to prove.",
            _ => string.Empty,
        });
        lines.RemoveAll(string.IsNullOrEmpty);

        var block = lines.Count == 0
            ? string.Empty
            : $"# Right now\n{string.Join("\n", lines)}\n" + (day is { Beats.Count: > 0 }
                ? "This is your life outside this conversation. Let it color you (what you'd mention, why you're short or distracted), but don't announce it or recite it."
                  + (string.IsNullOrWhiteSpace(situation) ? "\n" : " Where you are is the 'Where you are' line; the day is what's going on in your life around it."
                      + " Where they disagree (the day puts you elsewhere, or with someone the 'Where you are' line doesn't), 'Where you are' is true and the day went differently.\n")
                : string.Empty);
        if (wear.Count > 0)
            block += $"\n# Gone stale\n{string.Join("\n", wear.Select(w => $"- {w}"))}\n";
        return block;
    }

    public static string? Drift(double valence, string? source)
    {
        var from = string.IsNullOrWhiteSpace(source) ? string.Empty : $" (what happened in {source})";
        return valence switch
        {
            >= 0.5 => $"Since this morning your mood's lifted quite a bit{from}.",
            >= 0.2 => $"You're in a somewhat better mood than this morning{from}.",
            <= -0.5 => $"Things have really worn on you since this morning{from}.",
            <= -0.2 => $"You're a bit worse for wear compared to this morning{from}.",
            _ => null,
        };
    }

    private static string Beat(DayBeat b) => $"{b.Minute / 60:00}:{b.Minute % 60:00} {b.Text}";
}

[Alias("PartyTown.World.Self.IPersonaSelfGrain")]
public interface IPersonaSelfGrain : IGrainWithStringKey
{
    /// <summary>The <c># Right now</c> block; <paramref name="withDay"/> false in scenario Rooms whose setting owns time and place.</summary>
    [Alias("GetPresenceAsync")] Task<string> GetPresenceAsync(bool withDay);
    [Alias("NoteSpokeAsync")] Task NoteSpokeAsync(double chattiness);
    [Alias("SetMoodAsync")] Task SetMoodAsync(string mood);
    [Alias("RechargeAsync")] Task RechargeAsync(double amount);
    [Alias("GetStateAsync")] Task<SelfState> GetStateAsync();
    /// <summary>True (and the clock reset) when a <see cref="ThreadReview"/> is due.</summary>
    [Alias("TryReviewThreadsAsync")] Task<bool> TryReviewThreadsAsync();
    /// <summary>Threads, or the Preoccupation, a review found settled or contradicted (ADR 0052): they go.</summary>
    [Alias("CloseThreadsAsync")] Task CloseThreadsAsync(List<string> settled, List<string> contradicted);

    /// <summary>An option was acted on: any of today's openers it carried has been told now, on whatever channel.</summary>
    [Alias("TellAsync")] Task TellAsync(string label);
    /// <summary>Something that just happened to them, to bring up while it's fresh (ADR 0049).</summary>
    [Alias("AddOpenerAsync")] Task AddOpenerAsync(string story);
    /// <summary>Something happened in their own life (ADR 0055): in their Right now for a while, and in the next Day.</summary>
    [Alias("HappenAsync")] Task HappenAsync(string text);
    [Alias("GetAvailabilityAsync")] Task<double> GetAvailabilityAsync();
    /// <summary>Today's first and last beat in local minutes since midnight; nulls before a day exists. Places uses it for wake and bed.</summary>
    [Alias("GetDayWindowAsync")] Task<DayWindow> GetDayWindowAsync();
    /// <summary>A closed Moment's appraisal: <paramref name="better"/> = p(it left them in a better mood).</summary>
    [Alias("ShiftValenceAsync")] Task ShiftValenceAsync(double better, string where);

    /// <summary>Today's stretches away from home (ADR 0027); empty before today's Day. Read-only.</summary>
    [AlwaysInterleave, Alias("GetWhereaboutsAsync")] Task<List<Whereabout>> GetWhereaboutsAsync();
}

[GenerateSerializer, Alias("PartyTown.World.Self.DayWindow")]
public sealed record DayWindow([property: Id(0)] int? FirstBeatMinute, [property: Id(1)] int? LastBeatMinute);

[GenerateSerializer, Alias("PartyTown.World.Self.SelfState")]
public sealed class SelfState
{
    [Id(0)] public string? DayKey { get; set; }
    [Id(1)] public List<DayBeat> Beats { get; set; } = new();
    [Id(2)] public string? Mood { get; set; }
    [Id(3)] public string Energy { get; set; } = "medium";
    [Id(4)] public string? Preoccupation { get; set; }
    [Id(5)] public double Battery { get; set; } = 1;
    [Id(6)] public long BatteryAt { get; set; }
    [Id(7)] public long DayAttemptAt { get; set; }
    [Id(8)] public List<string> Openers { get; set; } = new();
    [Id(9)] public bool OpenersTried { get; set; }
    [Id(10)] public string? AvailabilityTried { get; set; }
    [Id(11)] public int AvailabilityVersion { get; set; }
    [Id(12)] public double Valence { get; set; }
    [Id(13)] public long ValenceAt { get; set; }
    [Id(14)] public string? ValenceSource { get; set; }
    /// <summary>Ongoing life threads, carried day to day (ADR 0021).</summary>
    [Id(15)] public List<string> Threads { get; set; } = new();
    /// <summary>Threads today's day settled.</summary>
    [Id(16)] public List<string> Resolved { get; set; } = new();
    [Id(17)] public string? ThreadsKey { get; set; }
    /// <summary>Today's stretches away from home (ADR 0027).</summary>
    [Id(18)] public List<Whereabout> Whereabouts { get; set; } = new();
    /// <summary>The part of town they live in, read once from who they are (ADR 0027); "" when it couldn't be read.</summary>
    [Id(19)] public string? Neighbourhood { get; set; }
    /// <summary>Days each of <see cref="Threads"/> has run, same order; missing entries count as 1.</summary>
    [Id(20)] public List<int> ThreadAges { get; set; } = new();
    /// <summary><see cref="SelfMath.SheetKey"/> of the sheet today's life was written for; null before it was tracked.</summary>
    [Id(21)] public string? SheetKey { get; set; }
    /// <summary>When a <see cref="ThreadReview"/> last ran.</summary>
    [Id(22)] public long ThreadsReviewedAt { get; set; }
    /// <summary>The last thing that happened in their own life, to them (ADR 0055).</summary>
    [Id(23)] public string? Own { get; set; }
    [Id(24)] public long OwnAt { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Self.DayBeat")]
public sealed class DayBeat
{
    [Id(0)] public int Minute { get; set; }
    [Id(1)] public string Text { get; set; } = string.Empty;
    /// <summary>System One p(they could chat during this); null until rated.</summary>
    [Id(2)] public double? Availability { get; set; }
}
