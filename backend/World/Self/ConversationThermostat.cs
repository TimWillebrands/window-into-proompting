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
            }, cts.Token);
            var heavy = answers.Noul("heavy", 0);
            var circling = answers.Noul("circling", 0);
            _heavyStreak = Thermostat.NextStreak(_heavyStreak, heavy);
            _cue = Thermostat.Cue(_heavyStreak, circling);
            _lastMessageId = last;
            logger.LogInformation("Thermostat {Room}: heavy={Heavy:F2} circling={Circling:F2} streak={Streak}", this.GetPrimaryKey(), heavy, circling, _heavyStreak);
        }
        catch (Exception ex)
        {
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

    public static int NextStreak(int streak, double heavy) => heavy >= HeavyThreshold ? streak + 1 : 0;

    public static string? Cue(int heavyStreak, double circling)
    {
        if (heavyStreak < SustainedStreak) return null;
        return circling >= 0.5
            ? "This conversation has been heavy and going round in circles for a while. Real people lighten it at some point: a joke, a change of subject, something from their day, or they drift off. Unless something genuinely new just happened, don't go deeper."
            : "This conversation has been intense for a while. It's fine to keep it light for a moment, or step away.";
    }
}

[Alias("PartyTown.World.Self.IConversationThermostatGrain")]
public interface IConversationThermostatGrain : IGrainWithGuidKey
{
    [Alias("GetCueAsync")] Task<string?> GetCueAsync();
}
