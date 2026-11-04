using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Orleans.Streams;
using PartyTown.Grains;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Realtime;

public sealed class PartyRealtimeHub(
    IClusterClient clusterClient,
    IGrainFactory grainFactory,
    ILogger<PartyRealtimeHub> logger) : IPartyRealtimeHub
{
    private static readonly IReadOnlySet<Guid> NoHumans = new HashSet<Guid>();
    private static readonly IReadOnlyCollection<Guid> Nobody = Array.Empty<Guid>();

    private readonly ConcurrentDictionary<Guid, StreamFanoutSession<PartyStreamEvent>> sessions = new();
    // DM membership never changes after creation, and generation deltas would otherwise look it up per token.
    private readonly ConcurrentDictionary<Guid, IReadOnlyCollection<Guid>?> roomMembers = new();

    public Task HandleConnectionAsync(Guid partyId, WebSocket socket, Viewer viewer, CancellationToken cancellationToken)
    {
        using (logger.BeginPartyScope(partyId))
        {
            var session = sessions.GetOrAdd(partyId, id => new StreamFanoutSession<PartyStreamEvent>(
                Streams().GetStream<PartyStreamEvent>(PartyStreamIds.PartyEventsNamespace, PartyStreamIds.PartyEventId(id)),
                evt => RealtimeTranslation.Party(id, evt),
                (who, _) => PartySnapshotAsync(id, who),
                () => new RealtimeFrame("party.pong", new PartyPongData(id, Guid.Empty)),
                () => sessions.TryRemove(id, out _),
                frame => AudienceAsync(id, frame),
                logger));
            return session.HandleConnectionAsync(socket, viewer, cancellationToken);
        }
    }

    private IStreamProvider Streams() => clusterClient.GetStreamProvider(PartyStreamIds.Provider);

    /// <summary>One snapshot frame per Room: messages plus the thought-log streams (skips, race outcomes).</summary>
    private async Task<IReadOnlyList<RealtimeFrame>> PartySnapshotAsync(Guid partyId, Viewer viewer)
    {
        var messages = await grainFactory.GetGrain<IPartyGrain>(partyId).DownloadMessages();
        var frames = new List<RealtimeFrame>();
        foreach (var room in messages.GroupBy(m => m.ChatGroupId))
        {
            if (!AccessRules.CanSeeRoom(viewer, await PrivateMembersAsync(partyId, room.Key))) continue;
            var grain = grainFactory.GetGrain<IChatGroupGrain>(room.Key);
            frames.Add(new RealtimeFrame("party.snapshot", new PartySnapshotData(
                partyId, room.Key, room.ToArray(),
                await grain.GetSkippedTurnsAsync(), await grain.GetRaceEvaluationsAsync())));
        }
        return frames;
    }

    /// <summary>Resolves a frame's facts once, so the per-client check stays synchronous.</summary>
    private async Task<Func<Viewer, bool>> AudienceAsync(Guid partyId, RealtimeFrame frame)
    {
        var members = frame.Data is IRoomFrame room ? await PrivateMembersAsync(partyId, room.ChatGroupId) : null;
        var humans = frame.Data is PartyWorldEventData { Kind: "activity" } ? await HumansAsync(partyId) : NoHumans;
        return viewer => FeedPrivacy.CanSee(viewer, frame, members, humans);
    }

    /// <summary>A Room we can't place is hidden from everyone: unknown must not mean public.</summary>
    private async Task<IReadOnlyCollection<Guid>?> PrivateMembersAsync(Guid partyId, Guid chatGroupId)
    {
        if (chatGroupId == Guid.Empty) return null;
        if (roomMembers.TryGetValue(chatGroupId, out var known)) return known;

        var humans = await HumansAsync(partyId);
        foreach (var group in await grainFactory.GetGrain<IPartyGrain>(partyId).GetChatGroups())
            roomMembers[group.Id] = AccessRules.PrivateMembers(group, humans);
        return roomMembers.TryGetValue(chatGroupId, out var found) ? found : Nobody;
    }

    private async Task<IReadOnlySet<Guid>> HumansAsync(Guid partyId)
        => (await grainFactory.GetGrain<IPartyGrain>(partyId).GetParty()).Participants
            .Where(p => p.Driver == DriverKind.User).Select(p => p.Id).ToHashSet();
}

internal sealed class RealtimeClientConnection(WebSocket socket, Viewer viewer, JsonSerializerOptions jsonOptions)
{
    private readonly SemaphoreSlim sendLock = new(1, 1);

    public WebSocket Socket { get; } = socket;
    public Viewer Viewer { get; } = viewer;
    public bool IsOpen => Socket.State == WebSocketState.Open;

    public async Task SendAsync(PartyRealtimeEnvelope envelope, CancellationToken cancellationToken)
    {
        if (!IsOpen)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, jsonOptions);
        await sendLock.WaitAsync(cancellationToken);

        try
        {
            if (!IsOpen)
            {
                return;
            }

            await Socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    public async Task CloseIfNeededAsync(CancellationToken cancellationToken)
    {
        if (Socket.State is not WebSocketState.Open and not WebSocketState.CloseReceived)
        {
            return;
        }

        await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", cancellationToken);
    }
}
