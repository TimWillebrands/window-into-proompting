using PartyTown.Grains;

namespace PartyTown.World.Self;

/// <summary>Per-Party cache of <see cref="VoiceWear"/> over recent persona lines across all Rooms and DMs (key = Party id).</summary>
public sealed class WorldVoiceGrain : Grain, IWorldVoiceGrain
{
    private const int PerRoom = 25;
    private const int Window = 80;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    private VoiceWearReport _report = VoiceWearReport.Empty;
    private List<VoiceLine> _lines = new();
    private DateTimeOffset _at = DateTimeOffset.MinValue;

    public async Task<List<VoiceLine>> GetLinesAsync(int limit)
    {
        await GetReportAsync();
        return _lines.TakeLast(Math.Clamp(limit, 1, Window)).ToList();
    }

    public async Task<VoiceWearReport> GetReportAsync()
    {
        if (DateTimeOffset.UtcNow - _at < Ttl) return _report;
        var party = GrainFactory.GetGrain<IPartyGrain>(this.GetPrimaryKey());
        var cast = await party.GetCastAsync();
        var rooms = await party.GetChatGroups();
        var batches = await Task.WhenAll(rooms.Select(r => GrainFactory.GetGrain<IChatGroupGrain>(r.Id).GetRecentMessagesAsync(PerRoom)));
        var roomNames = rooms.ToDictionary(r => r.Id, r => r.Name);
        var names = cast.ToDictionary(c => c.Id, c => c.Name);
        var recent = batches.SelectMany(b => b)
            .Where(m => m.SenderType == "assistant" && m.Kind is null && !string.IsNullOrWhiteSpace(m.Content))
            .OrderBy(m => m.SendAt ?? 0)
            .TakeLast(Window)
            .ToList();
        _lines = recent.Select(m => new VoiceLine(
                m.SendAt ?? 0, roomNames.GetValueOrDefault(m.ChatGroupId, "?"),
                names.GetValueOrDefault(m.SenderId, "?"), m.Content!))
            .ToList();
        _report = VoiceWear.Analyze(recent.Select(m => new Utterance(m.SenderId, m.Content!)).ToList(), cast.Select(c => c.Name));
        _at = DateTimeOffset.UtcNow;
        return _report;
    }
}

[Alias("PartyTown.World.Self.IWorldVoiceGrain")]
public interface IWorldVoiceGrain : IGrainWithGuidKey
{
    [Alias("GetReportAsync")] Task<VoiceWearReport> GetReportAsync();
    [Alias("GetLinesAsync")] Task<List<VoiceLine>> GetLinesAsync(int limit);
}

/// <summary>A recent persona line across Rooms and DMs, for inspection and the lifelikeness bench.</summary>
[GenerateSerializer, Alias("PartyTown.World.Self.VoiceLine")]
public sealed record VoiceLine([property: Id(0)] long Ts, [property: Id(1)] string Room, [property: Id(2)] string Speaker, [property: Id(3)] string Text);
