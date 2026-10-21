using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.World.Self;

/// <summary>Per-Party cache of <see cref="VoiceWear"/> over recent persona lines across all Rooms and DMs (key = Party id).</summary>
public sealed class WorldVoiceGrain : Grain, IWorldVoiceGrain
{
    private const int PerRoom = 25;
    private const int Window = 80;
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private VoiceWearReport _report = VoiceWearReport.Empty;
    private List<VoiceLine> _lines = new();
    private DateTimeOffset _at = DateTimeOffset.MinValue;

    public async Task<List<VoiceLine>> GetLinesAsync(int limit)
    {
        await GetReportAsync();
        return _lines.TakeLast(Math.Clamp(limit, 1, Window)).ToList();
    }

    public async Task<List<VoiceLine>> GetOwnLinesAsync(Guid speakerId, int limit)
    {
        await GetReportAsync();
        return _lines.Where(l => l.SpeakerId == speakerId).TakeLast(limit).ToList();
    }

    public async Task<VoiceWearReport> GetReportAsync()
    {
        if (DateTimeOffset.UtcNow - _at < Ttl) return _report;
        var party = GrainFactory.GetGrain<IPartyGrain>(this.GetPrimaryKey());
        var cast = await party.GetCastAsync();
        var rooms = await party.GetChatGroups();
        var batches = await Task.WhenAll(rooms.Select(r => GrainFactory.GetGrain<IChatGroupGrain>(r.Id).GetRecentMessagesAsync(PerRoom)));
        var roomNames = rooms.ToDictionary(r => r.Id, r => r.Kind == "dm" ? $"DM {r.Name}" : $"#{r.Name}");
        var names = cast.ToDictionary(c => c.Id, c => c.Name);
        var personas = cast.Where(c => c.DefaultDriver == DriverKind.LLM).Select(c => c.Id).ToHashSet();
        var chat = batches.SelectMany(b => b)
            .Where(m => m.SenderType == "assistant" && m.Kind is null && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new VoiceLine(m.SendAt ?? 0, roomNames.GetValueOrDefault(m.ChatGroupId, "?"),
                names.GetValueOrDefault(m.SenderId, "?"), m.Content!, m.SenderId));
        // The Board is where much of the talk happens; its posts and replies wear words out too.
        var board = (await GrainFactory.GetGrain<IBoardGrain>(this.GetPrimaryKey()).GetThreadsAsync())
            .SelectMany(t => t.Replies.Select(r => new VoiceLine(r.CreatedAt, $"Board \"{t.Title}\"", r.AuthorName, r.Body, r.AuthorId))
                .Prepend(new VoiceLine(t.CreatedAt, $"Board \"{t.Title}\"", t.AuthorName, t.Body, t.AuthorId)))
            .Where(l => personas.Contains(l.SpeakerId));
        _lines = chat.Concat(board).OrderBy(l => l.Ts).TakeLast(Window).ToList();
        _report = VoiceWear.Analyze(_lines.Select(l => new Utterance(l.SpeakerId, l.Text)).ToList(), cast.Select(c => c.Name));
        _at = DateTimeOffset.UtcNow;
        return _report;
    }
}

[Alias("PartyTown.World.Self.IWorldVoiceGrain")]
public interface IWorldVoiceGrain : IGrainWithGuidKey
{
    [Alias("GetReportAsync")] Task<VoiceWearReport> GetReportAsync();
    [Alias("GetLinesAsync")] Task<List<VoiceLine>> GetLinesAsync(int limit);
    [Alias("GetOwnLinesAsync")] Task<List<VoiceLine>> GetOwnLinesAsync(Guid speakerId, int limit);
}

/// <summary>A recent persona line across Rooms and DMs, for inspection and the lifelikeness bench.</summary>
[GenerateSerializer, Alias("PartyTown.World.Self.VoiceLine")]
public sealed record VoiceLine([property: Id(0)] long Ts, [property: Id(1)] string Room, [property: Id(2)] string Speaker, [property: Id(3)] string Text, [property: Id(4)] Guid SpeakerId);
