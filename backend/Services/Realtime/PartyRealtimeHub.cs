using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Orleans.Streams;
using PartyTown.Grains;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Realtime;

public sealed class PartyRealtimeHub(
    IClusterClient clusterClient,
    IGrainFactory grainFactory,
    PartyTown.Services.Import.IImportRunCoordinator importRuns,
    ILogger<PartyRealtimeHub> logger) : IPartyRealtimeHub
{
    private readonly ConcurrentDictionary<Guid, StreamFanoutSession<PartyStreamEvent>> sessions = new();
    private readonly ConcurrentDictionary<Guid, StreamFanoutSession<ImportStreamEvent>> importSessions = new();

    public Task HandleConnectionAsync(Guid partyId, WebSocket socket, CancellationToken cancellationToken)
    {
        using (logger.BeginPartyScope(partyId))
        {
            var session = sessions.GetOrAdd(partyId, id => new StreamFanoutSession<PartyStreamEvent>(
                Streams().GetStream<PartyStreamEvent>(PartyStreamIds.PartyEventsNamespace, PartyStreamIds.PartyEventId(id)),
                evt => RealtimeTranslation.Party(id, evt),
                _ => PartySnapshotAsync(id),
                () => new RealtimeFrame("party.pong", new PartyPongData(id, Guid.Empty)),
                () => sessions.TryRemove(id, out _),
                logger));
            return session.HandleConnectionAsync(socket, cancellationToken);
        }
    }

    public Task HandleImportConnectionAsync(Guid sessionId, WebSocket socket, CancellationToken cancellationToken)
    {
        var session = importSessions.GetOrAdd(sessionId, id => new StreamFanoutSession<ImportStreamEvent>(
            Streams().GetStream<ImportStreamEvent>(PartyStreamIds.ImportEventsNamespace, PartyStreamIds.ImportEventId(id)),
            evt => RealtimeTranslation.Import(id, evt),
            _ => Task.FromResult<IReadOnlyList<RealtimeFrame>>(new[]
            {
                new RealtimeFrame("import.run.snapshot", new ImportRunSnapshotData(id, importRuns.GetActiveRuns(id))),
            }),
            () => new RealtimeFrame("import.pong", new ImportPongData(id)),
            () => importSessions.TryRemove(id, out _),
            logger));
        return session.HandleConnectionAsync(socket, cancellationToken);
    }

    private IStreamProvider Streams() => clusterClient.GetStreamProvider(PartyStreamIds.Provider);

    /// <summary>One snapshot frame per Room: messages plus the thought-log streams (skips, race outcomes).</summary>
    private async Task<IReadOnlyList<RealtimeFrame>> PartySnapshotAsync(Guid partyId)
    {
        var messages = await grainFactory.GetGrain<IPartyGrain>(partyId).DownloadMessages();
        var frames = new List<RealtimeFrame>();
        foreach (var room in messages.GroupBy(m => m.ChatGroupId))
        {
            var grain = grainFactory.GetGrain<IChatGroupGrain>(room.Key);
            frames.Add(new RealtimeFrame("party.snapshot", new PartySnapshotData(
                partyId, room.Key, room.ToArray(),
                await grain.GetSkippedTurnsAsync(), await grain.GetRaceEvaluationsAsync())));
        }
        return frames;
    }
}

internal sealed class RealtimeClientConnection(WebSocket socket, JsonSerializerOptions jsonOptions)
{
    private readonly SemaphoreSlim sendLock = new(1, 1);

    public WebSocket Socket { get; } = socket;
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
