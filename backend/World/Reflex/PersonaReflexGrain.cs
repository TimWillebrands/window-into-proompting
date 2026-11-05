using System.Globalization;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.World.Reflex;

/// <summary>
/// A Persona's stimulus-driven attention (key = <c>party:persona</c>, ADR 0031): filter, weigh, then act or put it off.
/// Not reentrant on purpose: one thing at a time is the bottleneck being modelled, so nothing here may await a caller.
/// </summary>
public sealed class PersonaReflexGrain(
    [PersistentState(stateName: "reflex", storageName: "parties")]
    IPersistentState<ReflexState> state,
    ISystemOneJudge judge,
    WorldMemory memory,
    ILogger<PersonaReflexGrain> logger,
    TimeProvider? time = null)
    : Grain, IPersonaReflexGrain
{
    private static readonly TimeSpan JudgeBudget = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan RecallBudget = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan BetweenStaysFresh = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NodLasts = TimeSpan.FromSeconds(8);
    private const double ChoiceTemperature = 0.8;

    private readonly Dictionary<(PerceptKind, Guid), (double Seen, long At)> _habits = new();
    private readonly Dictionary<Guid, (long At, IReadOnlyList<string> Lines)> _between = new();
    private (Guid Room, long At)? _exchange;

    private DateTimeOffset UtcNow => (time ?? TimeProvider.System).GetUtcNow();
    private DateTimeOffset LocalNow => (time ?? TimeProvider.System).GetLocalNow();
    private (Guid Party, Guid Persona) Keys => WorldKeys.ParseLife(this.GetPrimaryKeyString());
    private IWorldClockGrain Clock => GrainFactory.GetGrain<IWorldClockGrain>(Keys.Party);
    private IPlacesGrain Places => GrainFactory.GetGrain<IPlacesGrain>(Keys.Party);
    private IPersonaGrain Persona => GrainFactory.GetGrain<IPersonaGrain>(Keys.Persona);

    public async Task<ReflexVerdict> PerceiveAsync(Percept p)
    {
        var now = UtcNow;
        var ms = now.ToUnixTimeMilliseconds();
        if (!judge.Enabled || !(await Clock.GetStatusAsync()).Running) return ReflexVerdict.Through;

        // Already in this conversation: it holds the workspace, so no appraisal, just reading time.
        if (p.RoomId is { } room && InExchange(room, ms))
            return ReflexVerdict.Reply(Lag(p, 0), false, "mid-conversation");
        if (p.RoomId is { } heldRoom && state.State.Intentions.FirstOrDefault(i => i.RoomId == heldRoom) is { } held)
        {
            Intentions.Renew(held, WorldStream.Clip(p.Text, 160), ms);
            await SaveAsync();
            return ReflexVerdict.Taken($"{ReflexVerdict.Holding} still meaning to get back to them");
        }

        var cast = await GrainFactory.GetGrain<IPartyGrain>(Keys.Party).GetCastAsync();
        if (cast.FirstOrDefault(c => c.Id == Keys.Persona && c.DefaultDriver == DriverKind.LLM) is not { } self) return ReflexVerdict.Through;
        var sense = await SenseAsync(self, p, now);
        if (sense is null) return ReflexVerdict.Through;

        var seen = Habituate(p, ms);
        var strength = ReflexMath.Strength(p, sense.Bond, seen);
        if (!ReflexMath.Noticed(strength, sense.Load, Random.Shared.NextDouble()))
        {
            logger.LogInformation("Reflex {Persona} missed {Kind} from {From} (strength {Strength:F2}, load {Load:F2})", self.Name, p.Kind, p.FromName, strength, sense.Load);
            return ReflexVerdict.Taken("didn't notice");
        }

        var weighed = await WeighAsync(p, sense);
        if (weighed is null) return ReflexVerdict.Through;

        var shaped = ReflexMath.Shape(weighed.Choices[ReflexWeighing.Key], new Landscape(self.Impulsivity, self.Chattiness, p.FromHuman));
        weighed.Choices[ReflexWeighing.Key] = shaped;
        var torn = ReflexMath.Torn(shaped);
        var picked = weighed.SampleChoice(ReflexWeighing.Key, ChoiceTemperature) ?? ReflexOptions.CarryOn;
        logger.LogInformation("Reflex {Persona} on {Kind} from {From}: {Picked} ({Weights}) load {Load:F2}{Torn}",
            self.Name, p.Kind, p.FromName, picked, Describe(shaped), sense.Load, torn ? " torn" : "");
        return await ActAsync(p, self, sense, picked, torn, ms);
    }

    public async Task NoteSpokeAsync(Guid roomId)
    {
        if (state.State.Intentions.FirstOrDefault(i => i.RoomId == roomId && i.Acking) is { } acked)
        {
            acked.Acking = false;
            await SaveAsync();
            return;
        }
        _exchange = (roomId, UtcNow.ToUnixTimeMilliseconds());
        if (state.State.Intentions.RemoveAll(i => i.RoomId == roomId) > 0) await SaveAsync();
    }

    public Task<string> GetMindAsync() => Task.FromResult(Intentions.Render(state.State.Intentions));

    public Task<List<Intention>> GetIntentionsAsync() => Task.FromResult(state.State.Intentions.ToList());

    public async Task SurfaceAsync()
    {
        var ms = UtcNow.ToUnixTimeMilliseconds();
        var load = await LoadAsync(null, UtcNow);
        foreach (var i in state.State.Intentions.ToList())
        {
            switch (Intentions.Step(i, ms, load))
            {
                case IntentionStep.Surface:
                    state.State.Intentions.Remove(i);
                    Spawn(() => ResumeAsync(i), "resume");
                    break;
                case IntentionStep.Forget:
                    state.State.Intentions.Remove(i);
                    logger.LogInformation("Reflex {Key} forgot to get back to {From}", this.GetPrimaryKeyString(), i.FromName);
                    break;
                case IntentionStep.Postpone:
                    i.DueAt = ms + (long)ReflexMath.BreakpointCheck.TotalMilliseconds;
                    break;
            }
        }
        await SaveAsync();
    }

    private bool InExchange(Guid room, long ms)
        => _exchange is { } e && e.Room == room && ms - e.At < ReflexMath.ExchangeHolds.TotalMilliseconds;

    private long Lag(Percept p, double load)
        => (long)ReflexMath.ReplyLag(p.Kind, load, Random.Shared.NextDouble(), Random.Shared.NextDouble()).TotalMilliseconds;

    private double Habituate(Percept p, long ms)
    {
        var key = (p.Kind, p.FromId);
        var seen = _habits.TryGetValue(key, out var h) ? ReflexMath.Recover(h.Seen, (ms - h.At) / 60_000.0) : 0;
        _habits[key] = (seen + 1, ms);
        return seen;
    }

    private async Task<Sense?> SenseAsync(CastMember self, Percept p, DateTimeOffset now)
    {
        var key = this.GetPrimaryKeyString();
        var selfTask = GrainFactory.GetGrain<IPersonaSelfGrain>(key).GetStateAsync();
        var tieTask = GrainFactory.GetGrain<IPersonaTiesGrain>(key).GetTieAsync(p.FromId);
        var betweenTask = BetweenAsync(p, now);
        var loadTask = LoadAsync(p, now);
        var body = await Places.GetBodyAsync(Keys.Persona);
        // Sleep has its own rule (ADR 0020): only a human wakes them, and that path is unchanged.
        if (body is { Asleep: true }) return null;

        var s = await selfTask;
        var tie = await tieTask;
        var local = LocalNow;
        var beat = s.Beats.LastOrDefault(b => b.Minute <= local.Hour * 60 + local.Minute)?.Text;
        var doing = body?.AwayAs is { } away ? $"{away}{(beat is null ? "" : $": {beat}")}" : beat;
        double? apart = tie.LastAt > 0 ? (now.ToUnixTimeMilliseconds() - tie.LastAt) / 3_600_000.0 : null;
        var bio = (LivedSheets.Lived(self.Id, self.SystemPrompt) is { } lived ? LivedSheets.Bio(lived) : null) ?? self.Bio ?? self.SystemPrompt;
        return new Sense(self.Name, bio, doing, s.Mood, await loadTask, SelfMath.Battery(s, local),
            new Bond(tie.Exchanges, tie.Warmth, apart), await betweenTask);
    }

    /// <summary>How much of them is spoken for, as seen from the channel <paramref name="p"/> came in on; null asks in general.</summary>
    private async Task<double> LoadAsync(Percept? p, DateTimeOffset now)
    {
        var key = this.GetPrimaryKeyString();
        var s = await GrainFactory.GetGrain<IPersonaSelfGrain>(key).GetStateAsync();
        var body = await Places.GetBodyAsync(Keys.Persona);
        var onShift = false;
        try { onShift = (await GrainFactory.GetGrain<Work.IOccupationGrain>(key).GetAsync()).OnShift; }
        catch (Exception ex) { logger.LogDebug(ex, "Shift lookup failed for {Key}", key); }
        var elsewhere = _exchange is { } e && e.Room != p?.RoomId && now.ToUnixTimeMilliseconds() - e.At < ReflexMath.ExchangeHolds.TotalMilliseconds;
        var occupied = ReflexMath.Occupies(body?.Activity, PlacesGrain.OnDevice(body?.Activity), p?.Kind);
        return ReflexMath.Load(SelfMath.Availability(s, LocalNow), onShift, body?.AwayAs is not null, occupied, elsewhere);
    }

    /// <summary>What memory says about them, cached: the graph stays off the reflex path most of the time.</summary>
    private async Task<IReadOnlyList<string>> BetweenAsync(Percept p, DateTimeOffset now)
    {
        var ms = now.ToUnixTimeMilliseconds();
        if (_between.TryGetValue(p.FromId, out var hit) && ms - hit.At < BetweenStaysFresh.TotalMilliseconds) return hit.Lines;
        IReadOnlyList<string> lines = Array.Empty<string>();
        try
        {
            using var cts = new CancellationTokenSource(RecallBudget);
            var (memories, stances) = await memory.RecallAsync(Keys.Party, Keys.Persona, new List<Guid> { p.FromId }, p.Text, cts.Token);
            lines = stances.Take(1).Concat(memories.Take(2).Select(m => m.Snippet)).ToList();
        }
        catch (Exception ex) { logger.LogDebug(ex, "Reflex recall skipped for {Key}", this.GetPrimaryKeyString()); }
        _between[p.FromId] = (ms, lines);
        return lines;
    }

    private async Task<SystemOneAnswers?> WeighAsync(Percept p, Sense sense)
    {
        try
        {
            using var cts = new CancellationTokenSource(JudgeBudget);
            var answers = await judge.AskAsync(ReflexWeighing.State(p, sense), ReflexWeighing.Ask(p, sense.Name), cts.Token);
            return answers.Choices.TryGetValue(ReflexWeighing.Key, out var dist) && dist.Count > 0 ? answers : null;
        }
        catch (Exception ex)
        {
            logger.LogInformation("Reflex judge unavailable for {Key}: {Error}", this.GetPrimaryKeyString(), ex.Message);
            return null;
        }
    }

    private async Task<ReflexVerdict> ActAsync(Percept p, CastMember self, Sense sense, string picked, bool torn, long ms)
    {
        switch (picked)
        {
            case ReflexOptions.Now or ReflexOptions.Speak:
                return ReflexVerdict.Reply(Lag(p, sense.Load), torn, picked);
            case ReflexOptions.Soon:
                await HoldAsync(p, sense, ms);
                Log(p, self, "hold", $"saw {Ref(p)}, and is finishing up first");
                return ReflexVerdict.Taken($"{ReflexVerdict.Holding} finishing up first");
            case ReflexOptions.Brief:
                // Held before the line goes out: a slow or failed line must not cost them the real answer.
                await HoldAsync(p, sense, ms, acking: true);
                Spawn(() => AckAsync(p, sense), "ack");
                return ReflexVerdict.Taken($"{ReflexVerdict.Holding} a quick line now, properly later");
            case ReflexOptions.Listen or ReflexOptions.Leave or ReflexOptions.CarryOn:
                return ReflexVerdict.Taken(picked);
            default:
                Spawn(() => DoAsync(picked, p, self, sense), picked);
                return ReflexVerdict.Taken(picked);
        }
    }

    private async Task HoldAsync(Percept p, Sense sense, long ms, bool acking = false)
    {
        if (p.RoomId is not { } room) return;
        var off = ReflexMath.PutOff(sense.Load, p.FromHuman, Random.Shared.NextDouble(), Random.Shared.NextDouble());
        state.State.Intentions.RemoveAll(i => i.RoomId == room);
        state.State.Intentions.Add(new Intention
        {
            RoomId = room, FromId = p.FromId, FromName = p.FromName, Gist = WorldStream.Clip(p.Text, 160),
            Doing = sense.Doing ?? "busy", HeldAt = ms, DueAt = ms + (long)off.TotalMilliseconds,
            OwedBy = p.FromHuman ? ms + (long)ReflexMath.OwedWithin.TotalMilliseconds : null,
            ForgetAt = ms + (long)ReflexMath.ForgottenAfter.TotalMilliseconds,
            Acking = acking,
        });
        // Having put it off, the next line from them is news, not a continuation.
        if (_exchange?.Room == room) _exchange = null;
        await SaveAsync();
    }

    private async Task AckAsync(Percept p, Sense sense)
    {
        try
        {
            if (p.RoomId is { } room && await Clock.TrySpendAsync(p.FromHuman))
            {
                await Persona.InitiateAsync(room, ReflexVoice.Ack(sense.Doing), null);
            }
        }
        finally
        {
            if (state.State.Intentions.FirstOrDefault(i => i.RoomId == p.RoomId && i.Acking) is { } held)
            {
                held.Acking = false;
                await SaveAsync();
            }
        }
    }

    private async Task DoAsync(string picked, Percept p, CastMember self, Sense sense)
    {
        var first = ReflexWeighing.First(p.FromName);
        switch (picked)
        {
            case ReflexOptions.Greet when p.RoomId is { } talk && await Clock.TrySpendAsync(p.FromHuman):
                await Persona.InitiateAsync(talk, ReflexVoice.Greet(first), null);
                Log(p, self, "greet", $"said hello to {first}");
                break;
            case ReflexOptions.Greet or ReflexOptions.Nod:
                if (string.IsNullOrEmpty((await Places.GetBodyAsync(self.Id))?.Activity))
                    await Places.SetActivityAsync(self.Id, $"nods at {first}", NodLasts);
                Log(p, self, "nod", $"nodded at {first}");
                break;
            case ReflexOptions.Move:
                if (await MoveAwayAsync(self.Id)) Log(p, self, "walk", $"moved away from {first}");
                break;
            case ReflexOptions.GoOver when p.PlaceId is { } place:
                await Places.MoveAsync(self.Id, place, p.SpotId);
                Log(p, self, "walk", $"went over to {first}");
                break;
            case ReflexOptions.CallOut when p.RoomId is { } room && await Clock.TrySpendAsync(p.FromHuman):
                await Places.SetVoiceAsync(self.Id, Volumes.Shout, null);
                await Persona.InitiateAsync(room, ReflexVoice.CallOver(first), null);
                Log(p, self, "shout", $"called out to {first}");
                break;
            case ReflexOptions.Open:
                if (await Places.LetInAsync(self.Id, p.FromId)) Log(p, self, "letin", $"let {first} in");
                break;
        }
    }

    private async Task<bool> MoveAwayAsync(Guid self)
    {
        var snapshot = await Places.GetSnapshotAsync();
        var me = snapshot.Bodies.FirstOrDefault(b => b.ParticipantId == self);
        var here = snapshot.Places.FirstOrDefault(pl => pl.Id == me?.PlaceId);
        if (me is null || here is null) return false;
        var taken = snapshot.Bodies.Where(b => b.PlaceId == here.Id && b.ParticipantId != self).Select(b => b.SpotId).ToHashSet();
        var spot = here.Spots.Where(s => s.Id != me.SpotId).OrderBy(s => taken.Contains(s.Id)).ThenBy(_ => Random.Shared.Next()).FirstOrDefault();
        return spot is not null && await Places.MoveAsync(self, here.Id, spot.Id) is not null;
    }

    private async Task ResumeAsync(Intention i)
    {
        var first = ReflexWeighing.First(i.FromName);
        try
        {
            await Persona.InitiateAsync(i.RoomId, ReflexVoice.GetBack(first, i.Gist, i.Doing), null);
        }
        catch (Exception ex) when (i.OwedBy is not null && i.Tries + 1 < ReflexMath.MaxTries)
        {
            // Owed means owed: a model outage puts it back rather than losing it.
            logger.LogWarning(ex, "Reflex couldn't get back to {From}; trying again", i.FromName);
            i.Tries++;
            i.DueAt = UtcNow.ToUnixTimeMilliseconds() + (long)ReflexMath.BreakpointCheck.TotalMilliseconds;
            i.OwedBy = i.DueAt;
            state.State.Intentions.Add(i);
            await SaveAsync();
        }
    }

    private static string Ref(Percept p) => p.Kind == PerceptKind.Direct ? $"a message from {ReflexWeighing.First(p.FromName)}" : $"what {ReflexWeighing.First(p.FromName)} said";

    /// <summary>Goes under the channel's own App, so the feed's privacy rules for it apply.</summary>
    private void Log(Percept p, CastMember self, string action, string summary)
    {
        var app = p.Kind switch { PerceptKind.Direct => "messenger", PerceptKind.Line => "rooms", _ => "places" };
        Spawn(() => Clock.RecordAsync(new WorldActivity
        {
            PersonaId = self.Id, PersonaName = self.Name, App = app, Action = action, Summary = summary,
            TargetId = p.RoomId, WithPersonaId = p.Kind == PerceptKind.Direct ? p.FromId : null,
        }), "log");
    }

    /// <summary>Detached and non-fatal: speech takes seconds, and this grain must stay free to perceive.</summary>
    private void Spawn(Func<Task> work, string what)
        => _ = RunAsync(work, what);

    private async Task RunAsync(Func<Task> work, string what)
    {
        try { await work(); }
        catch (Exception ex) { logger.LogWarning(ex, "Reflex {What} failed for {Key}", what, this.GetPrimaryKeyString()); }
    }

    private async Task SaveAsync()
    {
        await state.WriteStateAsync();
        var next = Intentions.NextLook(state.State.Intentions);
        Spawn(() => Clock.RemindAsync(Keys.Persona, next), "remind");
    }

    private static string Describe(IReadOnlyDictionary<string, double> shaped)
        => string.Join(" ", shaped.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}={kv.Value.ToString("F2", CultureInfo.InvariantCulture)}"));
}

[Alias("PartyTown.World.Reflex.IPersonaReflexGrain")]
public interface IPersonaReflexGrain : IGrainWithStringKey
{
    /// <summary>Something reached them: the one seam every modality comes through.</summary>
    [Alias("PerceiveAsync")] Task<ReflexVerdict> PerceiveAsync(Percept percept);
    /// <summary>They spoke in <paramref name="roomId"/>: that holds their focus, and settles what they owed there.</summary>
    [Alias("NoteSpokeAsync")] Task NoteSpokeAsync(Guid roomId);
    /// <summary>What's still on their mind, for their prompts.</summary>
    [Alias("GetMindAsync")] Task<string> GetMindAsync();
    [Alias("GetIntentionsAsync")] Task<List<Intention>> GetIntentionsAsync();
    /// <summary>A look at what they've put off; the World clock calls this when one comes due.</summary>
    [Alias("SurfaceAsync")] Task SurfaceAsync();
}

/// <summary>Reflex for the response pipeline and Life: never fatal, never slower than a bound.</summary>
public sealed class ReflexGate(IGrainFactory grains, ILogger<ReflexGate> logger)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(3);

    private IPersonaReflexGrain Of(Guid partyId, Guid personaId) => grains.GetGrain<IPersonaReflexGrain>(WorldKeys.Life(partyId, personaId));

    public async Task<ReflexVerdict> PerceiveAsync(Guid partyId, Guid personaId, Percept percept)
    {
        try { return await Of(partyId, personaId).PerceiveAsync(percept).WaitAsync(Bound); }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reflex failed for {Persona}; replying as usual", personaId);
            return ReflexVerdict.Through;
        }
    }

    public async Task NoteSpokeAsync(Guid partyId, Guid personaId, Guid roomId)
    {
        try { await Of(partyId, personaId).NoteSpokeAsync(roomId); }
        catch (Exception ex) { logger.LogDebug(ex, "Reflex note failed for {Persona}", personaId); }
    }

    public async Task<string> MindAsync(Guid partyId, Guid personaId)
    {
        try { return await Of(partyId, personaId).GetMindAsync(); }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Reflex mind failed for {Persona}", personaId);
            return string.Empty;
        }
    }
}
