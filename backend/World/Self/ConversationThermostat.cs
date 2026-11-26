using Orleans.Concurrency;
using PartyTown.Grains;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

public sealed class ConversationThermostatGrain(ISystemOneJudge judge, ILogger<ConversationThermostatGrain> logger, TimeProvider? time = null)
    : Grain, IConversationThermostatGrain
{
    public static readonly TimeSpan ReadLasts = ChatAttention.SessionGap;

    private const int Lines = 10;
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);

    private int _lastMessageId = -1;
    private int _heavyStreak;
    private string? _cue;
    private bool _windingDown;
    private ThermostatRead _read = ThermostatRead.None;
    private long _readLineAt;

    public Task<ThermostatRead> PeekAsync()
        => Task.FromResult(Thermostat.Fresh(_read, _readLineAt, (time ?? TimeProvider.System).GetUtcNow(), ReadLasts));

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
        var room = GrainFactory.GetGrain<IChatGroupGrain>(this.GetPrimaryKey());
        var recent = (await room.GetRecentMessagesAsync(Lines * 2))
            .Where(m => !string.IsNullOrWhiteSpace(m.Content) && m.Kind is null)
            .TakeLast(Lines)
            .ToList();
        if (recent.Count < 4)
        {
            Forget();
            return null;
        }
        var last = recent[^1].MessageId;
        if (last == _lastMessageId) return _cue;
        _readLineAt = recent[^1].SendAt ?? 0;

        var partyId = await room.GetPartyIdAsync();
        var names = (await GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync()).ToDictionary(c => c.Id, c => c.Name);
        await ReadAsync(recent.Select(m => $"{names.GetValueOrDefault(m.SenderId, "Someone")}: {WorldStream.Clip(m.Content, 300)}"), last);
        return _cue;
    }

    private void Forget()
    {
        _lastMessageId = -1;
        _heavyStreak = 0;
        _cue = null;
        _windingDown = false;
        _read = ThermostatRead.None;
        _readLineAt = 0;
    }

    public async Task<ThermostatRead> GetThreadReadAsync(Guid partyId)
    {
        if (await GrainFactory.GetGrain<IBoardGrain>(partyId).GetThreadAsync(this.GetPrimaryKey()) is not { } thread) return _read;
        var lines = new[] { $"{thread.AuthorName}: {WorldStream.Clip(thread.Body, 300)}" }
            .Concat(thread.Replies.Select(r => $"{r.AuthorName}: {WorldStream.Clip(r.Body, 300)}"))
            .TakeLast(Lines).ToList();
        if (lines.Count < 4 || thread.Replies.Count == _lastMessageId) return _read;
        await ReadAsync(lines, thread.Replies.Count);
        return _read;
    }

    private async Task ReadAsync(IEnumerable<string> lines, int last)
    {
        var transcript = string.Join("\n", lines);
        try
        {
            using var cts = new CancellationTokenSource(Budget);
            var answers = await judge.AskAsync(transcript, new Dictionary<string, SystemOneQuestion>
            {
                ["heavy"] = new NoulQuestion("The latest messages are emotionally heavy, confessional or intense, rather than light, playful or everyday."),
                ["circling"] = new NoulQuestion("The latest messages keep circling the same topic, images or confession instead of moving on to anything new."),
                ["fizzling"] = new NoulQuestion("The last few messages are mostly acknowledgments, agreements or repeats; nobody is adding anything new."),
                ["reconfirming"] = new NoulQuestion("The latest messages mostly restate or re-confirm plans or promises that were already agreed earlier (a day, a time, a place, who brings what) instead of adding anything new."),
            }, cts.Token);
            var heavy = answers.Noul("heavy", 0);
            var circling = answers.Noul("circling", 0);
            var fizzling = answers.Noul("fizzling", 0);
            var reconfirming = answers.Noul("reconfirming", 0);
            _heavyStreak = Thermostat.NextStreak(_heavyStreak, heavy);
            _windingDown = Thermostat.WindingDown(circling, fizzling, reconfirming);
            _cue = Thermostat.Cue(_heavyStreak, circling, fizzling, reconfirming);
            _read = new ThermostatRead(heavy, circling, fizzling, _heavyStreak, _windingDown, reconfirming);
            _lastMessageId = last;
            logger.LogInformation("Thermostat {Room}: heavy={Heavy:F2} circling={Circling:F2} fizzling={Fizzling:F2} reconfirming={Reconfirming:F2} streak={Streak}", this.GetPrimaryKey(), heavy, circling, fizzling, reconfirming, _heavyStreak);
        }
        catch (Exception ex)
        {
            _lastMessageId = last;
            logger.LogDebug(ex, "Thermostat read failed for {Room}", this.GetPrimaryKey());
        }
    }
}

public static class Thermostat
{
    public const double HeavyThreshold = 0.6;
    public const int SustainedStreak = 2;
    public const double FizzlingThreshold = 0.6;
    public const double CirclingThreshold = 0.75;
    public const double ReconfirmingThreshold = 0.6;

    public static bool WindingDown(double circling, double fizzling, double reconfirming = 0)
        => fizzling >= FizzlingThreshold || circling >= CirclingThreshold || reconfirming >= ReconfirmingThreshold;

    public static int NextStreak(int streak, double heavy) => heavy >= HeavyThreshold ? streak + 1 : 0;

    public static ThermostatRead Fresh(ThermostatRead read, long newestLineAt, DateTimeOffset now, TimeSpan lasts)
        => newestLineAt > 0 && now.ToUnixTimeMilliseconds() - newestLineAt <= lasts.TotalMilliseconds ? read : ThermostatRead.None;

    public static bool RunItsCourse(ThermostatRead read) => read.Fizzling >= FizzlingThreshold || read.Reconfirming >= ReconfirmingThreshold;

    public static string? Cue(int heavyStreak, double circling, double fizzling = 0, double reconfirming = 0)
    {
        if (fizzling >= FizzlingThreshold)
            return "This conversation is winding down: mostly acknowledgments now. Real people let it end here. Say nothing, or bring something genuinely new; don't answer just to answer, even if your name came up.";
        if (reconfirming >= ReconfirmingThreshold)
            return "The plans are already settled; everyone knows when and where. People don't confirm them again and again. Talk about something else, or let it end here.";
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
    [Alias("GetReadAsync")] Task<ThermostatRead> GetReadAsync();
    [AlwaysInterleave, Alias("PeekAsync")] Task<ThermostatRead> PeekAsync();
    [Alias("IsWindingDownAsync")] Task<bool> IsWindingDownAsync();
    [Alias("GetThreadReadAsync")] Task<ThermostatRead> GetThreadReadAsync(Guid partyId);
}

[GenerateSerializer, Alias("PartyTown.World.Self.ThermostatRead")]
public sealed record ThermostatRead(
    [property: Id(0)] double Heavy,
    [property: Id(1)] double Circling,
    [property: Id(2)] double Fizzling,
    [property: Id(3)] int HeavyStreak,
    [property: Id(4)] bool Stale,
    [property: Id(5)] double Reconfirming = 0)
{
    public static readonly ThermostatRead None = new(0, 0, 0, 0, false);
}
