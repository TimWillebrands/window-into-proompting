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
    ILogger<PartyRealtimeHub> logger) : IPartyRealtimeHub
{
    private readonly ConcurrentDictionary<Guid, PartyRealtimeSession> sessions = new();

    public Task HandleConnectionAsync(Guid partyId, WebSocket socket, CancellationToken cancellationToken)
    {
        using (logger.BeginPartyScope(partyId))
        {
            logger.LogDebug("New WebSocket connection for party");

            var session = sessions.GetOrAdd(partyId, id => new PartyRealtimeSession(
                id,
                clusterClient,
                grainFactory,
                () => sessions.TryRemove(id, out _),
                logger));

            return session.HandleConnectionAsync(socket, cancellationToken);
        }
    }
}

public sealed class PartyRealtimeSession(
    Guid partyId,
    IClusterClient clusterClient,
    IGrainFactory grainFactory,
    Action onEmpty,
    ILogger<PartyRealtimeHub> logger)
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim sync = new(1);
    private readonly HashSet<RealtimeClientConnection> clients = [];
    private StreamSubscriptionHandle<PartyStreamEvent>? partySubscription;
    private long sequence;
    private bool started;

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using (logger.BeginPartyScope(partyId))
        {
            var client = new RealtimeClientConnection(socket, jsonOptions);

            logger.LogInformation("Client connected");
            await AddClientAsync(client, cancellationToken);

            try
            {
                await ReceiveUntilClosedAsync(client, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Connection error");
            }
            finally
            {
                logger.LogInformation("Client disconnected");
                await RemoveClientAsync(client);
            }
        }
    }

    private async Task AddClientAsync(RealtimeClientConnection client, CancellationToken cancellationToken)
    {
        var shouldStart = false;

        await sync.WaitAsync(cancellationToken);
        try
        {
            clients.Add(client);
            if (!started)
            {
                started = true;
                shouldStart = true;
            }
        }
        finally
        {
            sync.Release();
        }

        if (shouldStart)
        {
            await StartSubscriptionsAsync();
        }

        var existingMessages = await grainFactory.GetGrain<IPartyGrain>(partyId).DownloadMessages();
        var snapshotByChatGroup = existingMessages
            .GroupBy(message => message.ChatGroupId)
            .ToArray();

        foreach (var chatGroupSnapshot in snapshotByChatGroup)
        {
            // Pull the auxiliary thought-log streams alongside messages so reload
            // rehydrates skip-obvious + race outcomes (otherwise wiped to [] in the
            // frontend snapshot handler and never restored).
            var chatGroupGrain = grainFactory.GetGrain<IChatGroupGrain>(chatGroupSnapshot.Key);
            var skippedTurns = await chatGroupGrain.GetSkippedTurnsAsync();
            var raceEvaluations = await chatGroupGrain.GetRaceEvaluationsAsync();

            await client.SendAsync(
                CreateEnvelope(
                    "party.snapshot",
                    new PartySnapshotData(
                        partyId,
                        chatGroupSnapshot.Key,
                        chatGroupSnapshot.ToArray(),
                        skippedTurns,
                        raceEvaluations)),
                cancellationToken);
        }
    }

    private async Task RemoveClientAsync(RealtimeClientConnection client)
    {
        var shouldStop = false;

        await sync.WaitAsync();
        try
        {
            clients.Remove(client);
            shouldStop = started && clients.Count == 0;
            if (shouldStop)
            {
                started = false;
            }
        }
        finally
        {
            sync.Release();
        }

        if (!shouldStop)
        {
            return;
        }

        await StopSubscriptionsAsync();
        onEmpty();
    }

    private async Task StartSubscriptionsAsync()
    {
        logger.LogDebug("Subscribing to party stream");

        var streamProvider = clusterClient.GetStreamProvider(PartyStreamIds.Provider);
        var stream = streamProvider.GetStream<PartyStreamEvent>(
            PartyStreamIds.PartyEventsNamespace,
            PartyStreamIds.PartyEventId(partyId));

        partySubscription = await stream.SubscribeAsync((evt, _) => HandlePartyEventAsync(evt, default));
    }

    private async Task StopSubscriptionsAsync()
    {
        StreamSubscriptionHandle<PartyStreamEvent>? partyHandle;

        await sync.WaitAsync();
        try
        {
            partyHandle = partySubscription;
            partySubscription = null;
        }
        finally
        {
            sync.Release();
        }

        if (partyHandle is not null)
        {
            await partyHandle.UnsubscribeAsync();
        }
    }

    private async Task HandlePartyEventAsync(PartyStreamEvent evt, CancellationToken cancellationToken = default)
    {
        switch (evt.Type)
        {
            case "message":
                await BroadcastAsync(
                    CreateEnvelope("party.message.created", new PartyMessageCreatedData(partyId, evt.ChatGroupId, evt.Message)),
                    cancellationToken);
                break;

            case "deleteMessage":
                await BroadcastAsync(
                    CreateEnvelope("party.message.deleted", new PartyMessageDeletedData(partyId, evt.ChatGroupId, evt.MessageId)),
                    cancellationToken);
                break;

            case "deleteMessagesAfter":
                await BroadcastAsync(
                    CreateEnvelope("party.messages.truncated", new PartyMessagesTruncatedData(partyId, evt.ChatGroupId, evt.MessageId)),
                    cancellationToken);
                break;

            case "messageStream" when evt.MessageId.HasValue:
                await BroadcastAsync(
                    CreateEnvelope("party.generation.started", new PartyGenerationStartedData(partyId, evt.ChatGroupId, evt.MessageId.Value)),
                    cancellationToken);
                break;

            case "messageEvent" when evt.MessageId.HasValue && evt.MessageEvent is not null:
                await HandleMessageEventAsync(evt.ChatGroupId, evt.MessageId.Value, evt.MessageEvent, cancellationToken);
                break;

            case "raceEvaluation" when evt.RaceEvaluation is not null:
                await BroadcastAsync(
                    CreateEnvelope("party.race.evaluation",
                        new PartyRaceEvaluationData(partyId, evt.ChatGroupId, evt.RaceEvaluation)),
                    cancellationToken);
                break;

            default:
                await BroadcastAsync(
                    CreateEnvelope("party.event", new PartyUnknownEventData(partyId, evt.ChatGroupId, evt.Type, evt.Message, evt.MessageId)),
                    cancellationToken);
                break;
        }
    }

    private async Task HandleMessageEventAsync(
        Guid chatGroupId,
        int messageId,
        MessageStreamEvent evt,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("HandleMessageEventAsync: event={EventType} dataLen={DataLen} done={Done}", evt.Event, evt.Data?.Length ?? -1, evt.Done);

        await BroadcastAsync(
            CreateEnvelope("party.generation.delta", new PartyGenerationDeltaData(partyId, chatGroupId, messageId, evt.Event, evt.Data, evt.Done)),
            cancellationToken);

        if (evt.Done)
        {
            await BroadcastAsync(
                CreateEnvelope("party.generation.completed", new PartyGenerationCompletedData(partyId, chatGroupId, messageId)),
                cancellationToken);
        }
    }

    private async Task ReceiveUntilClosedAsync(RealtimeClientConnection client, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        while (client.IsOpen)
        {
            WebSocketReceiveResult result;

            try
            {
                result = await client.Socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await client.CloseIfNeededAsync(cancellationToken);
                break;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var content = Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count));
            if (string.Equals(content, "ping", StringComparison.OrdinalIgnoreCase))
            {
                await client.SendAsync(CreateEnvelope("party.pong", new PartyPongData(partyId, Guid.Empty)), cancellationToken);
            }
        }
    }

    private async Task BroadcastAsync(PartyRealtimeEnvelope envelope, CancellationToken cancellationToken)
    {
        RealtimeClientConnection[] activeClients;

        await sync.WaitAsync(cancellationToken);
        try
        {
            activeClients = [.. clients.Where(c => c.IsOpen)];
        }
        finally
        {
            sync.Release();
        }

        await Task.WhenAll(activeClients.Select(client => client.SendAsync(envelope, cancellationToken)));
    }

    private PartyRealtimeEnvelope CreateEnvelope(string type, IPartyRealtimeData data)
        => new()
        {
            Type = type,
            Sequence = Interlocked.Increment(ref sequence),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = data
        };
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
