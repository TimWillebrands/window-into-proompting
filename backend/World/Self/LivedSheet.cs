using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using PartyTown.Grains.Generation;

namespace PartyTown.World.Self;

/// <summary>
/// A persona's sheet distilled into lived background (ADR 0021), key = persona id. Modes, scripted lines,
/// catchphrases and style instructions are what make personas perform; the authored original stays untouched
/// in the library, prompts read the distilled version once it exists.
/// </summary>
public sealed class LivedSheetGrain(
    [PersistentState(stateName: "livedSheet", storageName: "parties")]
    IPersistentState<LivedSheetState> state,
    IWorldLlm llm,
    ILogger<LivedSheetGrain> logger)
    : Grain, ILivedSheetGrain
{
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(30);
    private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

    public async Task<string?> EnsureAsync(string name, string source)
    {
        var hash = LivedSheets.Hash(source);
        if (state.State.SourceHash == hash && !string.IsNullOrWhiteSpace(state.State.Text))
        {
            LivedSheets.Remember(this.GetPrimaryKey(), hash, state.State.Text);
            return state.State.Text;
        }
        if (DateTimeOffset.UtcNow - _lastAttempt < Retry) return null;
        _lastAttempt = DateTimeOffset.UtcNow;
        // Detached: a stored sheet is awaited, a new one never is.
        _ = DistillAsync(name, source, hash);
        return null;
    }

    private async Task DistillAsync(string name, string source, string hash)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var text = await llm.For(this.GetPrimaryKey()).CompleteAsync(JobComplexity.CharacterVoice, LivedSheets.DistillPrompt,
                $"Name: {name}\n\nTheir description:\n{source}", cts.Token, 0.5);
            text = text?.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length < 200) return;
            state.State.SourceHash = hash;
            state.State.Text = text;
            await state.WriteStateAsync();
            LivedSheets.Remember(this.GetPrimaryKey(), hash, text);
            logger.LogInformation("Lived sheet for {Name}: {Chars} chars (from {Source})", name, text.Length, source.Length);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lived sheet distillation failed for {Name}", name);
        }
    }
}

/// <summary>In-process cache the prompt builders read without awaiting a grain; filled by <see cref="LivedSheetGrain"/>.</summary>
public static class LivedSheets
{
    private static readonly ConcurrentDictionary<Guid, (string Hash, string Text)> Cache = new();

    public const string DistillPrompt = """
        You rewrite a persona description as the lasting background of a real person. That person reads it as "who you
        are", in a world that already writes their present: each day and mood, their plans and wants, what happens to
        them, who they come to know. So it holds only what stays true for years. Write in English, second person ("You
        are…"), plain prose. Start with a # heading that is just their name, then exactly these headings in this
        order: ## Who you are / ## People in your life / ## With other people / ## Tastes and opinions /
        ## Contradictions / ## How you talk. A ## Card or ## Going on lately section in the description is input only.

        Keep the lasting facts: name, age, job, places, family, history, relationships, beliefs, flaws. A time-bound
        fact isn't lasting: drop the instance and keep the pattern under it, if there is one. Time-bound means true
        only for now: lately, this week, open threads, projects under way, pending decisions, deadlines, running
        counts, how things stand with someone at the moment, someone being new. Date the past by how old they were,
        never by year or "N years ago". Give no one's age but their own. Lacks that last stay: what they never had,
        what they're bad at, the friction that keeps coming back. Never add an event, plan, deadline or open question
        of your own.

        Remove everything that makes someone perform instead of live:
        - modes, states, triggers, IF/THEN rules
        - example dialogue, catchphrases, signature phrases, quotation marks of any kind
        - style instructions ("uses metaphors from…", "always uses emoji X", "speaks in riddles")
        - epithets and archetype labels ("The Syncretic Architect")
        - grand themes and symbolism
        - notes about scenes, events or how a character is played
        Reword anything literary in your own plain words and leave its imagery behind. Looks get at most one sentence.
        No essays about their inner psychology: show it through ordinary facts (a habit, what they avoid).

        ## Who you are opens with three plain sentences, because other systems read them alone: age and what they do
        for a living, or plainly that they don't; neighbourhood, city and who they live with; whether money is tight,
        getting by or comfortable, and why. Then 2–4 more: the shape of their week and the history that still matters.
        ## People in your life is at most five people, one short line each, as "- Name, relation: …": what they are to
        them and what they're like, never where things stand or when they talk. ## Tastes and opinions is three or
        four sentences. ## Contradictions is 2–3 tensions, one sentence each. Where the description is thin, add
        ordinary lasting specifics that fit: work, money, where they live (Amsterdam if it names no city), routines, a
        few named offscreen people, small tastes.

        ## With other people says what this person actually does, inferred from the description when it isn't there.
        One paragraph of at most five sentences, from strangers through acquaintances to friends: what gets them
        talking, who they notice, what they do for people, what they'd ask of someone, how fast they warm, what cools
        them. Behaviour, not adjectives; tendencies, not rules. Then 2–3 one-line bullets starting "You" that hand a
        stranger a way in: something they know, fix, lend, cook or keep track of that only their trade or history
        would give them. Facts, not offers; no local tips anyone could have. Stay true to their temperament: nobody
        turns outgoing, and nobody turns aloof unless the description says so.

        ## How you talk is the exception to "plain": it's what makes this person recognisable in text next to others.
        Give 4–6 concrete, observable writing habits, one sentence each, that set them apart from an average person:
        typical message length, capitals and punctuation, how they open, ask, disagree and joke, whose vocabulary they
        use (their work, age, region, languages), and things they'd never say or do in writing. Habits, not imagery:
        no metaphor themes, no example lines, no pet word, signature phrase or sign-off (if the description has one,
        drop it; a word they always reach for becomes a tic). Name the vocabulary, never its words. Another language
        shows as the odd word, never whole messages, and only languages their background gives them. Steer clear of
        the default profile ("full sentences, careful punctuation, no exclamation marks, rare dry humour") unless the
        description clearly calls for it: pick what this particular person does instead.

        350–550 words, and no more: over that, cut colour (furnishings, brands, lists of tastes) before work, money,
        home, household or people. Output only the rewritten description. Last check: any line that could be out of
        date next month goes; the world owns it.
        """;

    /// <summary>Bump to redo every distillation after a prompt change.</summary>
    private const int Version = 5;

    public static string Hash(string source)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v{Version}\n{source}")))[..16];

    public static void Remember(Guid personaId, string hash, string text) => Cache[personaId] = (hash, text);

    /// <summary>The distilled sheet, or null when there's none for this exact source yet.</summary>
    public static string? Lived(Guid personaId, string? source)
        => !string.IsNullOrWhiteSpace(source) && Cache.TryGetValue(personaId, out var lived) && lived.Hash == Hash(source) ? lived.Text : null;

    /// <summary>A short bio from the distilled sheet's opening, for prompts that only take a one-liner.</summary>
    public static string? Bio(string lived)
        => Controllers.PersonaController.ExtractBioFromMarkdown(lived) is { Length: > 0 } bio ? bio : null;

    /// <summary>The distilled sheet, loaded into the cache when it's stored; null while it's still being written.</summary>
    public static async Task<string?> WarmAsync(IGrainFactory grains, Model.CastMember who)
    {
        if (string.IsNullOrWhiteSpace(who.SystemPrompt)) return null;
        return Lived(who.Id, who.SystemPrompt) ?? await grains.GetGrain<ILivedSheetGrain>(who.Id).EnsureAsync(who.Name, who.SystemPrompt);
    }

    /// <summary>What intrinsic distillers read: the lived sheet, or the bio for someone without a sheet; null while the lived one is pending.</summary>
    public static async Task<string?> SourceAsync(IGrainFactory grains, Model.CastMember who)
        => string.IsNullOrWhiteSpace(who.SystemPrompt) ? who.Bio is { Length: > 0 } bio ? bio : null : await WarmAsync(grains, who);

    /// <summary>The distilled sheet if it was made from this exact source; otherwise the source itself.</summary>
    public static string? For(Guid personaId, string? source)
        => string.IsNullOrWhiteSpace(source) ? source
            : Cache.TryGetValue(personaId, out var lived) && lived.Hash == Hash(source) ? lived.Text
            : source;
}

[Alias("PartyTown.World.Self.ILivedSheetGrain")]
public interface ILivedSheetGrain : IGrainWithGuidKey
{
    /// <summary>The stored sheet for this source (cached), or null while a detached distillation writes it.</summary>
    [Alias("EnsureAsync")] Task<string?> EnsureAsync(string name, string source);
}

[GenerateSerializer, Alias("PartyTown.World.Self.LivedSheetState")]
public sealed class LivedSheetState
{
    [Id(0)] public string? SourceHash { get; set; }
    [Id(1)] public string? Text { get; set; }
}
