using System.Text.Json.Nodes;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;

namespace PartyTown.World.Skills;

/// <summary>
/// A Persona's Intrinsic Skills (ADR 0054), key = persona id: what they've known how to do for years, in every Party.
/// Authored, or distilled once from the lived sheet; a new sheet re-distils unless an author has set them.
/// </summary>
public sealed class IntrinsicSkillsGrain(
    [PersistentState(stateName: "intrinsicSkills", storageName: "parties")]
    IPersistentState<IntrinsicSkillsState> state,
    IWorldLlm llm,
    ILogger<IntrinsicSkillsGrain> logger)
    : Grain, IIntrinsicSkillsGrain
{
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);
    /// <summary>Bump to redo every distilled (never authored) skill after a prompt change.</summary>
    private const int DistillVersion = 1;
    private const int MaxDistilled = 4;
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;
    private Task _saving = Task.CompletedTask;

    /// <summary>One write at a time: a detached distillation can land while an author edits.</summary>
    private Task SaveAsync() => _saving = AfterAsync(_saving);

    private async Task AfterAsync(Task before)
    {
        try { await before; }
        catch (Exception ex) { logger.LogDebug(ex, "An earlier skills write failed"); }
        await state.WriteStateAsync();
    }

    public Task<List<Skill>> GetAsync() => Task.FromResult(state.State.Skills.ToList());

    public Task EnsureAsync(string name, string sheet)
    {
        var hash = Self.LivedSheets.Hash($"skills v{DistillVersion}\n{sheet}");
        if (state.State.Authored || state.State.SourceHash == hash || DateTimeOffset.UtcNow - _lastAttempt < Retry) return Task.CompletedTask;
        _lastAttempt = DateTimeOffset.UtcNow;
        // Detached: callers never wait on the LLM.
        _ = DistillAsync(name, sheet, hash);
        return Task.CompletedTask;
    }

    public async Task<List<Skill>> SetAsync(List<Skill> skills)
    {
        state.State.Skills = skills.Select(s => { s.Source = SkillSource.Authored; return s; }).ToList();
        state.State.Authored = true;
        await SaveAsync();
        return state.State.Skills.ToList();
    }

    public async Task<List<Skill>> AddAsync(Skill skill)
    {
        state.State.Skills.RemoveAll(s => s.Id == skill.Id);
        state.State.Skills.Add(skill);
        state.State.Authored = true;
        await SaveAsync();
        return state.State.Skills.ToList();
    }

    private async Task DistillAsync(string name, string sheet, string hash)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            var json = await llm.For(this.GetPrimaryKey()).CompleteJsonAsync(JobComplexity.General, SkillPrompts.Distill,
                $"Name: {name}\n\nWho they are:\n{sheet}", cts.Token, 0.5);
            if (json?["skills"] is not JsonArray list) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            state.State.Skills = list.Select(s => SkillRules.Parse(s, SkillSource.Sheet, now, practice: 30)).OfType<Skill>().Take(MaxDistilled).ToList();
            state.State.SourceHash = hash;
            await SaveAsync();
            logger.LogInformation("Intrinsic skills for {Name}: {Skills}", name, string.Join(" | ", state.State.Skills.Select(s => s.Name)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Skill distillation failed for {Name}", name);
        }
    }
}

[Alias("PartyTown.World.Skills.IIntrinsicSkillsGrain")]
public interface IIntrinsicSkillsGrain : IGrainWithGuidKey
{
    [Alias("GetAsync")] Task<List<Skill>> GetAsync();
    /// <summary>Distil from the sheet in the background when it's new and nobody authored them.</summary>
    [Alias("EnsureAsync")] Task EnsureAsync(string name, string sheet);
    /// <summary>Author edit: replaces them, and the sheet no longer overrides.</summary>
    [Alias("SetAsync")] Task<List<Skill>> SetAsync(List<Skill> skills);
    /// <summary>Promotion: an Acquired Skill becomes part of who they are.</summary>
    [Alias("AddAsync")] Task<List<Skill>> AddAsync(Skill skill);
}

/// <summary>
/// A Participant's Skills in one Party (ADR 0054), key = <c>party:participant</c>: the Acquired ones, what they've been
/// doing toward one (a count per key), and how often they used their Intrinsic ones here.
/// </summary>
public sealed class SkillsGrain(
    [PersistentState(stateName: "skills", storageName: "parties")]
    IPersistentState<SkillsState> state,
    IWorldLlm llm,
    ILogger<SkillsGrain> logger)
    : Grain, ISkillsGrain
{
    private const int KeptRecent = 5;
    private Task _saving = Task.CompletedTask;

    /// <summary>One write at a time: detached learning writes too, and storage refuses overlapping ones.</summary>
    private Task SaveAsync() => _saving = AfterAsync(_saving);

    private async Task AfterAsync(Task before)
    {
        try { await before; }
        catch (Exception ex) { logger.LogDebug(ex, "An earlier skills write failed"); }
        await state.WriteStateAsync();
    }

    public async Task<List<SkillView>> GetAsync()
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var intrinsic = GrainFactory.GetGrain<IIntrinsicSkillsGrain>(participantId);
        var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is { DefaultDriver: DriverKind.LLM } && await Self.LivedSheets.SourceAsync(GrainFactory, who) is { Length: > 0 } sheet)
            await intrinsic.EnsureAsync(who.Name, sheet);
        return Views(await intrinsic.GetAsync());
    }

    public async Task<SkillView?> PractiseAsync(SkillPractice practice)
    {
        if (string.IsNullOrWhiteSpace(practice.Key) || practice.Times <= 0) return null;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var tally = state.State.Tallies.TryGetValue(practice.Key, out var t) ? t : state.State.Tallies[practice.Key] = new SkillTally();
        tally.Count += practice.Times;
        tally.Last = now;
        tally.Source = practice.Source;
        if (!string.IsNullOrWhiteSpace(practice.What))
        {
            tally.Recent.Add(WorldStream.Clip(practice.What.Trim(), 160));
            if (tally.Recent.Count > KeptRecent) tally.Recent.RemoveRange(0, tally.Recent.Count - KeptRecent);
        }

        var (_, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        var intrinsic = await GrainFactory.GetGrain<IIntrinsicSkillsGrain>(participantId).GetAsync();
        if (tally.SkillId is { } id)
        {
            if (state.State.Acquired.FirstOrDefault(s => s.Id == id) is { } skill)
            {
                skill.Practice += practice.Times;
                skill.PractisedAt = now;
                if (SkillRules.Level(skill.Practice) > skill.WrittenFor) _ = RewriteAsync(skill.Id, tally.Recent.ToList());
                await SaveAsync();
                return SkillRules.View(skill, intrinsic: false, DateTimeOffset.UtcNow);
            }
            if (intrinsic.FirstOrDefault(s => s.Id == id) is { } own)
            {
                var mark = state.State.Marks.TryGetValue(id, out var m) ? m : state.State.Marks[id] = new SkillMark();
                mark.Practice += practice.Times;
                mark.PractisedAt = now;
                await SaveAsync();
                return SkillRules.View(Marked(own), intrinsic: true, DateTimeOffset.UtcNow);
            }
            // Forgotten since: the key is free to teach something again.
            tally.SkillId = null;
        }
        if (!tally.Closed && tally.Count >= SkillRules.Learns && now - tally.AskedAt >= SkillRules.AskAgain.TotalMilliseconds)
        {
            tally.AskedAt = now;
            _ = LearnAsync(practice.Key);
        }
        await SaveAsync();
        return null;
    }

    public async Task<SkillView> AddAsync(Skill skill)
    {
        state.State.Acquired.RemoveAll(s => s.Id == skill.Id);
        state.State.Acquired.Add(skill);
        Cap();
        await SaveAsync();
        return SkillRules.View(skill, intrinsic: false, DateTimeOffset.UtcNow);
    }

    public async Task<Skill?> RemoveAsync(Guid skillId)
    {
        var skill = state.State.Acquired.FirstOrDefault(s => s.Id == skillId);
        if (skill is null) return null;
        state.State.Acquired.Remove(skill);
        await SaveAsync();
        return skill;
    }

    /// <summary>The third time: is there know-how here? Joins one they have, writes a new one, or closes the key.</summary>
    private async Task LearnAsync(string key)
    {
        var (partyId, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var who = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
            var intrinsic = await GrainFactory.GetGrain<IIntrinsicSkillsGrain>(participantId).GetAsync();
            if (who is null || !state.State.Tallies.TryGetValue(key, out var tally)) return;
            var known = intrinsic.Concat(state.State.Acquired).ToList();
            var json = await llm.For(participantId).CompleteJsonAsync(JobComplexity.General, SkillPrompts.Learn(SkillRules.Level(tally.Count)),
                SkillPrompts.LearnBrief(who, known.Select(s => s.Name), tally), cts.Token, 0.5, flags: ["none"]);
            if (json is null) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (string.Equals(json["none"]?.ToString(), "true", StringComparison.OrdinalIgnoreCase))
            {
                tally.Closed = true;
                logger.LogInformation("Skill {Name}: nothing to learn in {Key}", who.Name, key);
            }
            else if (json["same"]?.ToString()?.Trim() is { Length: > 0 } same
                     && known.FirstOrDefault(s => s.Name.Equals(same, StringComparison.OrdinalIgnoreCase)) is { } joined)
            {
                tally.SkillId = joined.Id;
                if (state.State.Acquired.Contains(joined))
                {
                    joined.Practice += tally.Count;
                    joined.PractisedAt = now;
                }
                else
                {
                    var mark = state.State.Marks.TryGetValue(joined.Id, out var m) ? m : state.State.Marks[joined.Id] = new SkillMark();
                    mark.Practice += tally.Count;
                    mark.PractisedAt = now;
                }
                logger.LogInformation("Skill {Name}: {Key} is {Skill}", who.Name, key, joined.Name);
            }
            else if (SkillRules.Parse(json, tally.Source, now, tally.Count) is { } learned)
            {
                state.State.Acquired.Add(learned);
                tally.SkillId = learned.Id;
                Cap();
                logger.LogInformation("Skill {Name} learned {Skill} from {Key}", who.Name, learned.Name, key);
            }
            else return;
            await SaveAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Learning from {Key} failed", key);
        }
    }

    /// <summary>A level crossed: the how grows with them.</summary>
    private async Task RewriteAsync(Guid skillId, List<string> recent)
    {
        var (_, participantId) = WorldKeys.ParseLife(this.GetPrimaryKeyString());
        if (state.State.Acquired.FirstOrDefault(s => s.Id == skillId) is not { } skill) return;
        var level = SkillRules.Level(skill.Practice);
        skill.WrittenFor = level;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var json = await llm.For(participantId).CompleteJsonAsync(JobComplexity.General, SkillPrompts.Grow(level),
                SkillPrompts.GrowBrief(skill, recent), cts.Token, 0.5, ["how"]);
            if (json?["how"]?.ToString()?.Trim() is not { Length: >= 12 } how) return;
            skill.How = WorldStream.Clip(how, 400);
            if (SkillRules.Cues(json["cues"]) is { Count: > 0 } cues) skill.Cues = cues;
            await SaveAsync();
            logger.LogInformation("Skill {Skill} now {Level}", skill.Name, level);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rewriting {Skill} failed", skill.Name);
        }
    }

    /// <summary>Past the cap the least practised, longest untouched one goes, and nothing points at it any more.</summary>
    private void Cap()
    {
        while (state.State.Acquired.Count > SkillRules.MaxAcquired)
        {
            var gone = state.State.Acquired.OrderBy(s => s.Practice).ThenBy(s => s.PractisedAt).First();
            state.State.Acquired.Remove(gone);
            foreach (var t in state.State.Tallies.Values.Where(t => t.SkillId == gone.Id)) t.SkillId = null;
        }
    }

    private Skill Marked(Skill own)
    {
        if (!state.State.Marks.TryGetValue(own.Id, out var mark)) return own;
        own.Practice = Math.Max(own.Practice, mark.Practice);
        own.PractisedAt = Math.Max(own.PractisedAt, mark.PractisedAt);
        return own;
    }

    private List<SkillView> Views(IEnumerable<Skill> intrinsic)
    {
        var now = DateTimeOffset.UtcNow;
        return intrinsic.Select(s => SkillRules.View(Marked(s), intrinsic: true, now))
            .Concat(state.State.Acquired.Select(s => SkillRules.View(s, intrinsic: false, now)))
            .ToList();
    }
}

[Alias("PartyTown.World.Skills.ISkillsGrain")]
public interface ISkillsGrain : IGrainWithStringKey
{
    /// <summary>Intrinsic then Acquired; kicks off distilling the intrinsic ones.</summary>
    [Alias("GetAsync")] Task<List<SkillView>> GetAsync();
    /// <summary>Count one time doing something; returns the Skill it practised, or null while it teaches nothing yet.</summary>
    [Alias("PractiseAsync")] Task<SkillView?> PractiseAsync(SkillPractice practice);
    [Alias("AddAsync")] Task<SkillView> AddAsync(Skill skill);
    /// <summary>Forget an Acquired Skill; returns it, or null when there's no such one.</summary>
    [Alias("RemoveAsync")] Task<Skill?> RemoveAsync(Guid skillId);
}

/// <summary>What the World writes about Skills; plain, no imagery, never example lines (ADR 0021).</summary>
public static class SkillPrompts
{
    private const string Fields = """
        - name: a short gerund phrase naming the know-how ("pulling espresso", "fixing bikes", "getting a shy kid talking").
        - how: 1–3 plain sentences in second person: the judgement and steps they have, what they check first, what they notice
          that others miss, what they do when it goes wrong. No example lines, no catchphrases, no imagery, nothing about feelings.
        - cues: 3–8 lowercase single words a conversation or a thing to do would have to touch for this to come to mind:
          tools, materials, situations. No generic words (people, time, work, help) and no app names (board, diary).
        """;

    public const string Distill = $$"""
        You read who someone is and say what they know how to do: things they've done for years and do without thinking.
        2 to 4 of them: their trade's craft if they have one, something from their home life or history, and a way they have
        with people. Read them off who they are; skip anything anybody at all can do.
        For each:
        {{Fields}}
        Output ONLY JSON: {"skills": [{"name": "...", "how": "...", "cues": ["..."]}]}
        """;

    public static string Learn(SkillLevel level) => $$"""
        Someone has now done the same thing a few times. Say whether there's know-how in it that grows with doing it, and if so
        what they've picked up so far. Where they are: {{SkillRules.Describe(level)}}, so {{SkillRules.Depth(level)}}.
        - none: true when there's nothing to get better at, it's the same every time (signing a wall, making a wish, a coin in a
          fountain). Then leave the rest empty.
        - same: when it's really one of the things they already know how to do, that name exactly as listed; else empty.
        Otherwise:
        {{Fields}}
        Output ONLY JSON: {"none": false, "same": "", "name": "...", "how": "...", "cues": ["..."]}
        """;

    public static string LearnBrief(CastMember who, IEnumerable<string> known, SkillTally tally)
    {
        var bio = Self.LivedSheets.Lived(who.Id, who.SystemPrompt) is { } lived ? Self.LivedSheets.Bio(lived) : who.Bio;
        var names = known.ToList();
        return $"""
            {who.Name}{(string.IsNullOrWhiteSpace(bio) ? "." : $": {bio}")}
            Already knows how to: {(names.Count == 0 ? "(nothing listed)" : string.Join("; ", names))}

            What they did, {tally.Count} times by now:
            {string.Join("\n", tally.Recent.Select(r => $"- {r}"))}
            """;
    }

    public static string Grow(SkillLevel level) => $$"""
        Someone has got better at something. Rewrite what they know about it for where they are now: {{SkillRules.Describe(level)}},
        so {{SkillRules.Depth(level)}}. Keep what still holds and add what doing it has taught them.
        - how: 1–3 plain sentences in second person, judgement and steps. No example lines, no imagery, nothing about feelings.
        - cues: 3–8 lowercase single words that bring it to mind. No generic words.
        Output ONLY JSON: {"how": "...", "cues": ["..."]}
        """;

    public static string GrowBrief(Skill skill, IReadOnlyList<string> recent) => $"""
        Know-how: {skill.Name}
        What they knew: {skill.How}
        Times done: {skill.Practice}
        {(recent.Count == 0 ? string.Empty : $"Lately:\n{string.Join("\n", recent.Select(r => $"- {r}"))}")}
        """;
}

[GenerateSerializer, Alias("PartyTown.World.Skills.IntrinsicSkillsState")]
public sealed class IntrinsicSkillsState
{
    [Id(0)] public List<Skill> Skills { get; set; } = new();
    [Id(1)] public string? SourceHash { get; set; }
    [Id(2)] public bool Authored { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Skills.SkillsState")]
public sealed class SkillsState
{
    [Id(0)] public List<Skill> Acquired { get; set; } = new();
    /// <summary>Per practice key: how often, lately what, and the Skill it teaches once known.</summary>
    [Id(1)] public Dictionary<string, SkillTally> Tallies { get; set; } = new();
    /// <summary>Intrinsic Skills practised in this Party.</summary>
    [Id(2)] public Dictionary<Guid, SkillMark> Marks { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.Skills.SkillTally")]
public sealed class SkillTally
{
    [Id(0)] public int Count { get; set; }
    [Id(1)] public long Last { get; set; }
    [Id(2)] public Guid? SkillId { get; set; }
    /// <summary>Looked at, and there's nothing to learn in it.</summary>
    [Id(3)] public bool Closed { get; set; }
    [Id(4)] public List<string> Recent { get; set; } = new();
    [Id(5)] public SkillSource Source { get; set; }
    [Id(6)] public long AskedAt { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Skills.SkillMark")]
public sealed class SkillMark
{
    [Id(0)] public int Practice { get; set; }
    [Id(1)] public long PractisedAt { get; set; }
}
