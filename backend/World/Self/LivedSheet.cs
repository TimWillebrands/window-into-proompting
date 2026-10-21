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

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try
        {
            var text = await llm.CompleteAsync(JobComplexity.CharacterThoughts, LivedSheets.DistillPrompt,
                $"Name: {name}\n\nTheir description:\n{source}", cts.Token, 0.5);
            text = text?.Trim();
            if (string.IsNullOrWhiteSpace(text) || text.Length < 200) return null;
            state.State.SourceHash = hash;
            state.State.Text = text;
            await state.WriteStateAsync();
            LivedSheets.Remember(this.GetPrimaryKey(), hash, text);
            logger.LogInformation("Lived sheet for {Name}: {Chars} chars (from {Source})", name, text.Length, source.Length);
            return text;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lived sheet distillation failed for {Name}", name);
            return null;
        }
    }
}

/// <summary>In-process cache the prompt builders read without awaiting a grain; filled by <see cref="LivedSheetGrain"/>.</summary>
public static class LivedSheets
{
    private static readonly ConcurrentDictionary<Guid, (string Hash, string Text)> Cache = new();

    public const string DistillPrompt = """
        You rewrite a persona description as the lived background of a real person. The result is read by that
        person as "who you are", so write in second person ("You are…"), plain prose with these headings:
        ## Who you are / ## People in your life / ## Tastes and opinions / ## Contradictions / ## How you talk /
        ## Going on lately.

        Keep every fact: name, age, job, places, family, history, relationships, beliefs, flaws.
        Remove everything that makes someone perform instead of live:
        - modes, states, triggers, IF/THEN rules
        - example dialogue, quoted lines, catchphrases, signature phrases
        - style instructions ("uses metaphors from…", "always uses emoji X", "speaks in riddles"); describe at most
          a loose habit in one plain sentence
        - epithets and archetype labels ("The Syncretic Architect")
        - grand themes and symbolism
        Where the description is thin, add ordinary specifics that fit (daily rhythm, a few offscreen people,
        small tastes). 300–500 words. Output only the rewritten description.
        """;

    public static string Hash(string source)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..16];

    public static void Remember(Guid personaId, string hash, string text) => Cache[personaId] = (hash, text);

    /// <summary>The distilled sheet, or null when there's none for this exact source yet.</summary>
    public static string? Lived(Guid personaId, string? source)
        => !string.IsNullOrWhiteSpace(source) && Cache.TryGetValue(personaId, out var lived) && lived.Hash == Hash(source) ? lived.Text : null;

    /// <summary>A short bio from the distilled sheet's opening, for prompts that only take a one-liner.</summary>
    public static string? Bio(string lived)
        => Controllers.PersonaController.ExtractBioFromMarkdown(lived) is { Length: > 0 } bio ? bio : null;

    /// <summary>The distilled sheet if it was made from this exact source; otherwise the source itself.</summary>
    public static string? For(Guid personaId, string? source)
        => string.IsNullOrWhiteSpace(source) ? source
            : Cache.TryGetValue(personaId, out var lived) && lived.Hash == Hash(source) ? lived.Text
            : source;
}

[Alias("PartyTown.World.Self.ILivedSheetGrain")]
public interface ILivedSheetGrain : IGrainWithGuidKey
{
    /// <summary>Distil (once per source version) and cache; null while not available yet.</summary>
    [Alias("EnsureAsync")] Task<string?> EnsureAsync(string name, string source);
}

[GenerateSerializer, Alias("PartyTown.World.Self.LivedSheetState")]
public sealed class LivedSheetState
{
    [Id(0)] public string? SourceHash { get; set; }
    [Id(1)] public string? Text { get; set; }
}
