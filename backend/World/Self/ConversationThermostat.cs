using PartyTown.Grains;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>
/// Conversation thermostat (ADR 0021), key = Room/DM id. System One reads the last lines once per new message:
/// heavy? circling? A sustained heavy streak becomes a cue to lighten, change subject or drift off.
/// </summary>
public sealed class ConversationThermostatGrain(ISystemOneJudge judge, ILogger<ConversationThermostatGrain> logger)
    : Grain, IConversationThermostatGrain
{
    private const int Lines = 10;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    private int _lastMessageId = -1;
    private int _heavyStreak;
    private string? _cue;
    private bool _windingDown;
    private ThermostatRead _read = ThermostatRead.None;

    public async Task<bool> IsWindingDownAsync()
    {
        await GetCueAsync();
        return _windingDown;
    }

    public async Task<ThermostatRead> GetReadAsync()
    {
        await GetCueAsync();
        return _read;
    }

    public async Task<string?> GetCueAsync()
    {
        if (!judge.Enabled) return null;
        var room = GrainFactory.GetGrain<IChatGroupGrain>(this.GetPrimaryKey());
        var recent = (await room.GetRecentMessagesAsync(Lines * 2))
            .Where(m => !string.IsNullOrWhiteSpace(m.Content) && m.Kind is null)
            .TakeLast(Lines)
            .ToList();
        if (recent.Count < 4) return null;
        var last = recent[^1].MessageId;
        if (last == _lastMessageId) return _cue;

        var partyId = await room.GetPartyIdAsync();
        var names = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).ToDictionary(c => c.Id, c => c.Name);
        var transcript = string.Join("\n", recent.Select(m =>
            $"{names.GetValueOrDefault(m.SenderId, "Someone")}: {WorldStream.Clip(m.Content, 300)}"));
        try
        {
            using var cts = new CancellationTokenSource(Budget);
            var answers = await judge.AskAsync(transcript, new Dictionary<string, SystemOneQuestion>
            {
                ["heavy"] = new NoulQuestion("The latest messages are emotionally heavy, confessional or intense, rather than light, playful or everyday."),
                ["circling"] = new NoulQuestion("The latest messages keep circling the same topic, images or confession instead of moving on to anything new."),
                ["fizzling"] = new NoulQuestion("The last few messages are mostly acknowledgments, agreements or repeats; nobody is adding anything new."),
            }, cts.Token);
            var heavy = answers.Noul("heavy", 0);
            var circling = answers.Noul("circling", 0);
            var fizzling = answers.Noul("fizzling", 0);
            _heavyStreak = Thermostat.NextStreak(_heavyStreak, heavy);
            _windingDown = fizzling >= Thermostat.FizzlingThreshold || circling >= Thermostat.CirclingThreshold;
            _cue = Thermostat.Cue(_heavyStreak, circling, fizzling);
            _read = new ThermostatRead(heavy, circling, fizzling, _heavyStreak,
                circling >= Thermostat.CirclingThreshold || fizzling >= Thermostat.FizzlingThreshold);
            _lastMessageId = last;
            logger.LogInformation("Thermostat {Room}: heavy={Heavy:F2} circling={Circling:F2} fizzling={Fizzling:F2} streak={Streak}", this.GetPrimaryKey(), heavy, circling, fizzling, _heavyStreak);
        }
        catch (Exception ex)
        {
            // Don't retry the same message on every call: a slow judge would queue every persona behind 8 s.
            _lastMessageId = last;
            logger.LogDebug(ex, "Thermostat read failed for {Room}", this.GetPrimaryKey());
        }
        return _cue;
    }
}

/// <summary>Pure thermostat rules.</summary>
public static class Thermostat
{
    public const double HeavyThreshold = 0.6;
    public const int SustainedStreak = 2;
    public const double FizzlingThreshold = 0.6;
    public const double CirclingThreshold = 0.75;

    public static int NextStreak(int streak, double heavy) => heavy >= HeavyThreshold ? streak + 1 : 0;

    public static string? Cue(int heavyStreak, double circling, double fizzling = 0)
    {
        if (fizzling >= FizzlingThreshold)
            return "This conversation is winding down: mostly acknowledgments now. Real people let it end here. Say nothing, or bring something genuinely new; don't answer just to answer, even if your name came up.";
        if (heavyStreak < SustainedStreak)
            return circling >= CirclingThreshold
                ? "This chat keeps going round the same thing. People move on: something from your day, a question, a joke, or nothing."
                : null;
        return circling >= 0.5
            ? "This conversation has been heavy and going round in circles for a while. Real people lighten it at some point: a joke, a change of subject, something from their day, or they drift off. Unless something genuinely new just happened, don't go deeper."
            : "This conversation has been intense for a while. It's fine to keep it light for a moment, or step away.";
    }
}

[Alias("PartyTown.World.Self.IConversationThermostatGrain")]
public interface IConversationThermostatGrain : IGrainWithGuidKey
{
    [Alias("GetCueAsync")] Task<string?> GetCueAsync();
    /// <summary>The last System One read; <see cref="ThermostatRead.Stale"/> = circling or fizzling (Places foregrounds leaving).</summary>
    [Alias("GetReadAsync")] Task<ThermostatRead> GetReadAsync();
    /// <summary>True while the last read found the talk fizzling or circling; the name-mention auto-reply is off then.</summary>
    [Alias("IsWindingDownAsync")] Task<bool> IsWindingDownAsync();
}

[GenerateSerializer, Alias("PartyTown.World.Self.ThermostatRead")]
public sealed record ThermostatRead(
    [property: Id(0)] double Heavy,
    [property: Id(1)] double Circling,
    [property: Id(2)] double Fizzling,
    [property: Id(3)] int HeavyStreak,
    [property: Id(4)] bool Stale)
{
    public static readonly ThermostatRead None = new(0, 0, 0, 0, false);
}
