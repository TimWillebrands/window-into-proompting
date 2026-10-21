using PartyTown.Grains;

namespace PartyTown.World.Self;

/// <summary>Per-Party cache of <see cref="VoiceWear"/> over recent persona lines across all Rooms and DMs (key = Party id).</summary>
public sealed class WorldVoiceGrain : Grain, IWorldVoiceGrain
{
    private const int PerRoom = 25;
    private const int Window = 80;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(90);

    private VoiceWearReport _report = VoiceWearReport.Empty;
    private DateTimeOffset _at = DateTimeOffset.MinValue;

    public async Task<VoiceWearReport> GetReportAsync()
    {
        if (DateTimeOffset.UtcNow - _at < Ttl) return _report;
        var party = GrainFactory.GetGrain<IPartyGrain>(this.GetPrimaryKey());
        var cast = await party.GetCastAsync();
        var rooms = await party.GetChatGroups();
        var batches = await Task.WhenAll(rooms.Select(r => GrainFactory.GetGrain<IChatGroupGrain>(r.Id).GetRecentMessagesAsync(PerRoom)));
        var lines = batches.SelectMany(b => b)
            .Where(m => m.SenderType == "assistant" && m.Kind is null && !string.IsNullOrWhiteSpace(m.Content))
            .OrderBy(m => m.SendAt ?? 0)
            .TakeLast(Window)
            .Select(m => new Utterance(m.SenderId, m.Content!))
            .ToList();
        _report = VoiceWear.Analyze(lines, cast.Select(c => c.Name));
        _at = DateTimeOffset.UtcNow;
        return _report;
    }
}

[Alias("PartyTown.World.Self.IWorldVoiceGrain")]
public interface IWorldVoiceGrain : IGrainWithGuidKey
{
    [Alias("GetReportAsync")] Task<VoiceWearReport> GetReportAsync();
}
