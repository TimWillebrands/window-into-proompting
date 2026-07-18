using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Orleans.Streams;
using PartyTown.Services.Import;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Realtime;

/// <summary>
/// Realtime fan-out for one import session: subscribes to the session's Orleans import
/// event stream while any client is connected and forwards scene-run lifecycle events.
/// On connect the client receives an "import.run.snapshot" of runs already in flight so
/// a mid-run page refresh reattaches to live progress.
/// </summary>
public sealed class ImportRealtimeSession(
    Guid sessionId,
    IClusterClient clusterClient,
    IImportRunCoordinator runCoordinator,
    Action onEmpty,
    ILogger<PartyRealtimeHub> logger)
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim sync = new(1);
    private readonly HashSet<RealtimeClientConnection> clients = [];
    private StreamSubscriptionHandle<ImportStreamEvent>? subscription;
    private long sequence;
    private bool started;

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var client = new RealtimeClientConnection(socket, jsonOptions);

        logger.LogInformation("Import session {SessionId}: realtime client connected", sessionId);
        await AddClientAsync(client, cancellationToken);

        try
        {
            await ReceiveUntilClosedAsync(client, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import session {SessionId}: connection error", sessionId);
        }
        finally
        {
            logger.LogInformation("Import session {SessionId}: realtime client disconnected", sessionId);
            await RemoveClientAsync(client);
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
            var stream = clusterClient
                .GetStreamProvider(PartyStreamIds.Provider)
                .GetStream<ImportStreamEvent>(PartyStreamIds.ImportEventsNamespace, PartyStreamIds.ImportEventId(sessionId));
            subscription = await stream.SubscribeAsync((evt, _) => HandleStreamEventAsync(evt));
        }

        await client.SendAsync(
            CreateEnvelope("import.run.snapshot", new ImportRunSnapshotData(sessionId, runCoordinator.GetActiveRuns(sessionId))),
            cancellationToken);
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

        var handle = subscription;
        subscription = null;
        if (handle is not null)
        {
            await handle.UnsubscribeAsync();
        }
        onEmpty();
    }

    private Task HandleStreamEventAsync(ImportStreamEvent evt)
    {
        var previews = evt.Items
            .Select(i => new ImportRunItemPreview { Type = i.Type, Persona = i.Persona, Summary = i.Summary, Weight = i.Weight })
            .ToList();

        return evt.Type switch
        {
            ImportStreamEvent.RunStarted => BroadcastAsync(
                CreateEnvelope("import.run.started", new ImportRunStartedData(sessionId, evt.SceneId))),
            ImportStreamEvent.RunProgress => BroadcastAsync(
                CreateEnvelope("import.run.progress",
                    new ImportRunProgressData(sessionId, evt.SceneId, evt.CallsDone, evt.TotalCalls, evt.Stage, previews))),
            ImportStreamEvent.RunCompleted => BroadcastAsync(
                CreateEnvelope("import.run.completed", new ImportRunCompletedData(sessionId, evt.SceneId, evt.Result))),
            ImportStreamEvent.RunFailed => BroadcastAsync(
                CreateEnvelope("import.run.failed", new ImportRunFailedData(sessionId, evt.SceneId, evt.Error ?? "unknown error"))),
            _ => Task.CompletedTask,
        };
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
                await client.SendAsync(CreateEnvelope("import.pong", new ImportPongData(sessionId)), cancellationToken);
            }
        }
    }

    private async Task BroadcastAsync(PartyRealtimeEnvelope envelope, CancellationToken cancellationToken = default)
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
