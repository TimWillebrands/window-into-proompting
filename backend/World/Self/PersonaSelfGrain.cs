using System.Globalization;
using System.Text.RegularExpressions;
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
    ILogger<PersonaSelfGrain> logger)
    : Grain, IPersonaSelfGrain
{
    private const int AvailabilityVersion = 1;
    private static readonly TimeSpan DayRetry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DayBudget = TimeSpan.FromSeconds(40);

    public async Task<string> GetPresenceAsync(bool withDay)
    {
        var (partyId, personaId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var now = DateTimeOffset.Now;
        if (withDay) await EnsureDayAsync(partyId, personaId, now);
        if (withDay) await EnsureAvailabilityAsync(partyId, personaId);
        var wear = await GrainFactory.GetGrain<IWorldVoiceGrain>(partyId).GetReportAsync();
        var situation = await GrainFactory.GetGrain<Places.IPlacesGrain>(partyId).GetSituationAsync(personaId);
        var own = await GrainFactory.GetGrain<IWorldVoiceGrain>(partyId).GetOwnLinesAsync(personaId, 3);
        return SelfRender.Presence(withDay ? state.State : null, SelfMath.Battery(state.State, now), now, VoiceWear.RenderFor(wear, personaId), situation)
            + VoiceWear.RenderOwnRecent(own, now);
    }

    public async Task NoteSpokeAsync(double chattiness)
    {
        var now = DateTimeOffset.Now;
        state.State.Battery = SelfMath.Drain(SelfMath.Battery(state.State, now), chattiness);
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
        await EnsureAvailabilityAsync(partyId, personaId);
        return SelfMath.Availability(state.State, DateTimeOffset.Now);
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
        if (beats.Count == 0 || beats.All(b => b.Availability is not null) || state.State.AvailabilityTried == state.State.DayKey || !judge.Enabled) return;
        state.State.AvailabilityTried = state.State.DayKey;
        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var questions = beats.Select((b, i) => (Key: $"b{i}", Q: (SystemOneQuestion)new NoulQuestion(
                    $"During \"{b.Text}\", {self.Name} could casually glance at their phone or computer and chat with friends.")))
                .ToDictionary(x => x.Key, x => x.Q);
            var answers = await judge.AskAsync($"{self.Name}: {self.Bio}\nTheir day:\n{string.Join("\n", beats.Select(b => b.Text))}", questions, cts.Token);
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

    private const string OpenersSpec = "3 lines, one per line: small things from their own life they might bring up in conversation today (a show or game, a work gripe, a funny or annoying thing that happened, an opinion on something ordinary, a question they'd ask others). Concrete, light, not about feelings.";

    /// <summary>Days generated before openers existed get them from one small call.</summary>
    private async Task EnsureOpenersAsync(Guid partyId, Guid personaId)
    {
        state.State.OpenersTried = true;
        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;
        using var cts = new CancellationTokenSource(DayBudget);
        try
        {
            var json = await llm.CompleteJsonAsync(JobComplexity.General,
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

    private async Task EnsureDayAsync(Guid partyId, Guid personaId, DateTimeOffset now)
    {
        var key = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (state.State.DayKey == key)
        {
            if (state.State.Openers.Count == 0 && !state.State.OpenersTried) await EnsureOpenersAsync(partyId, personaId);
            return;
        }
        if (now.ToUnixTimeMilliseconds() - state.State.DayAttemptAt < DayRetry.TotalMilliseconds) return;
        state.State.DayAttemptAt = now.ToUnixTimeMilliseconds();

        var self = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == personaId);
        if (self is null) return;

        using var cts = new CancellationTokenSource(DayBudget);
        try
        {
            var yesterday = state.State.DayKey is null
                ? "(no record)"
                : $"Mood: {state.State.Mood}. On your mind: {state.State.Preoccupation}. Last thing you did: {state.State.Beats.LastOrDefault()?.Text}";
            var json = await llm.CompleteJsonAsync(JobComplexity.General,
                $"""
                {WorldLlm.Identity(self)}

                Write {self.Name}'s ordinary day today, the offscreen life they live away from their computer.
                Real days are mostly mundane: work tasks and their friction, commuting, errands, meals, chores, weather,
                the body (sleep, aches, hunger), a call or message from family or an old friend, something they're
                watching or reading, one small annoyance, one small good thing.
                No epiphanies, no symbolism, no metaphors, nothing poetic. Plain, specific, a little boring.
                Keep their job, home and circumstances consistent with who they are. Don't mention any online chat or its people.
                Output ONLY JSON with string fields:
                - mood: 2-4 plain words for how they woke up feeling
                - energy: low, medium or high
                - preoccupation: one plain sentence, a practical thing from their own life nagging at them today
                - beats: 7-10 lines "HH:MM what happens", chronological from waking to bed, one per line
                - openers: {OpenersSpec}
                """,
                $"Today is {now.ToString("dddd d MMMM", CultureInfo.InvariantCulture)}.\nYesterday: {yesterday}",
                cts.Token, 0.9, ["mood", "energy", "preoccupation", "beats", "openers"]);
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
            // A night's sleep recharges.
            state.State.Battery = 1;
            state.State.BatteryAt = now.ToUnixTimeMilliseconds();
            await state.WriteStateAsync();
            logger.LogInformation("Self day for {Persona}: {Mood}, {Energy}, {Beats} beats", self.Name, state.State.Mood, state.State.Energy, beats.Count);
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

    [GeneratedRegex(@"^\s*[-*•]?\s*(\d{1,2})[:.h](\d{2})\s*[-—–:]?\s*(.+)$")]
    private static partial Regex BeatLine();

    public static double Battery(SelfState s, DateTimeOffset now)
    {
        if (s.BatteryAt == 0) return 1;
        var hours = Math.Max(0, (now.ToUnixTimeMilliseconds() - s.BatteryAt) / 3_600_000.0);
        return Math.Clamp(s.Battery + hours * RechargePerHour, 0, 1);
    }

    public static List<string> ParseList(string? raw)
        => (raw ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('-', '*', '•', ' ').Trim())
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^\d+[.)]\s*", ""))
            .Where(l => l.Length > 3)
            .Take(4)
            .ToList();

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
                Minute = int.Parse(m.Groups[1].Value) * 60 + int.Parse(m.Groups[2].Value),
                Text = m.Groups[3].Value.Trim(),
            })
            .OrderBy(b => b.Minute)
            .ToList();
}

/// <summary>Renders the <c># Right now</c> block.</summary>
public static class SelfRender
{
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
            lines.Add(current is null ? "You're not really up yet." : $"Off-screen right now: {current.Text}");
            if (current?.Availability is { } free && free < 0.6)
                lines.Add(free < 0.35
                    ? "You're tied up with that. You'd only glance at the chat now and then, and keep it to a line."
                    : "You're half-busy. Replies come in between other things.");
            if (earlier.Count > 0) lines.Add($"Earlier today: {string.Join("; ", earlier.Select(Beat))}");
            if (next is not null) lines.Add($"Coming up: {Beat(next)}");
            if (!string.IsNullOrWhiteSpace(day.Mood)) lines.Add($"Mood: {day.Mood}. Energy: {day.Energy}.");
            if (!string.IsNullOrWhiteSpace(day.Preoccupation)) lines.Add($"Nagging at you (your own life, not this chat): {day.Preoccupation}");
            if (day.Openers.Count > 0) lines.Add($"Things you might bring up if there's a lull (or not at all): {string.Join("; ", day.Openers)}");
        }
        lines.Add(battery switch
        {
            < 0.25 => "Social battery: nearly empty. You've talked a lot; you'd keep it very short, or drift off.",
            < 0.5 => "Social battery: low. Shorter answers, less to prove.",
            _ => string.Empty,
        });
        lines.RemoveAll(string.IsNullOrEmpty);

        var block = lines.Count == 0
            ? string.Empty
            : $"# Right now\n{string.Join("\n", lines)}\n" + (day is { Beats.Count: > 0 }
                ? "This is your life outside the chat. Let it color you (what you'd mention, why you're short or distracted), but don't announce it or recite it."
                  + (string.IsNullOrWhiteSpace(situation) ? "\n" : " Where you are is the 'Where you are' line; the day is what's going on in your life around it.\n")
                : string.Empty);
        if (wear.Count > 0)
            block += $"\n# Gone stale\n{string.Join("\n", wear.Select(w => $"- {w}"))}\n";
        return block;
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
    [Alias("GetStateAsync")] Task<SelfState> GetStateAsync();
    [Alias("GetAvailabilityAsync")] Task<double> GetAvailabilityAsync();
    /// <summary>Today's first and last beat in local minutes since midnight; nulls before a day exists. Places uses it for wake and bed.</summary>
    [Alias("GetDayWindowAsync")] Task<DayWindow> GetDayWindowAsync();
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
}

[GenerateSerializer, Alias("PartyTown.World.Self.DayBeat")]
public sealed class DayBeat
{
    [Id(0)] public int Minute { get; set; }
    [Id(1)] public string Text { get; set; } = string.Empty;
    /// <summary>System One p(they could chat during this); null until rated.</summary>
    [Id(2)] public double? Availability { get; set; }
}
