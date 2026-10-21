using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Orleans.Streams;

namespace PartyTown.Services.Realtime;

/// <summary>
/// Fans one Orleans stream out to N WebSocket clients: subscribes on the first client, unsubscribes on the
/// last, answers pings, numbers frames. Channel specifics come in as translate + snapshot.
/// </summary>
internal sealed class StreamFanoutSession<TEvent>(
    IAsyncStream<TEvent> stream,
    Func<TEvent, IReadOnlyList<RealtimeFrame>> translate,
    Func<CancellationToken, Task<IReadOnlyList<RealtimeFrame>>> snapshot,
    Func<RealtimeFrame> pong,
    Action onEmpty,
    ILogger logger)
{
    private readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim sync = new(1);
    private readonly HashSet<RealtimeClientConnection> clients = new();
    private StreamSubscriptionHandle<TEvent>? subscription;
    private long sequence;
    private bool started;

    public async Task HandleConnectionAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var client = new RealtimeClientConnection(socket, jsonOptions);
        logger.LogInformation("Realtime client connected");
        await AddClientAsync(client, cancellationToken);
        try
        {
            await ReceiveUntilClosedAsync(client, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // A tab closing or reloading mid-frame is a normal goodbye, not an error.
            logger.LogDebug(ex, "Realtime client went away");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Realtime connection error");
        }
        finally
        {
            logger.LogInformation("Realtime client disconnected");
            await RemoveClientAsync(client);
        }
    }

    private async Task AddClientAsync(RealtimeClientConnection client, CancellationToken ct)
    {
        bool shouldStart;
        await sync.WaitAsync(ct);
        try
        {
            clients.Add(client);
            shouldStart = !started;
            started = true;
        }
        finally
        {
            sync.Release();
        }

        if (shouldStart)
            subscription = await stream.SubscribeAsync((evt, _) => BroadcastAsync(translate(evt)));

        foreach (var frame in await snapshot(ct))
            await client.SendAsync(Envelope(frame), ct);
    }

    private async Task RemoveClientAsync(RealtimeClientConnection client)
    {
        StreamSubscriptionHandle<TEvent>? handle = null;
        await sync.WaitAsync();
        try
        {
            clients.Remove(client);
            if (!started || clients.Count > 0) return;
            started = false;
            (handle, subscription) = (subscription, null);
        }
        finally
        {
            sync.Release();
        }

        if (handle is not null)
            await handle.UnsubscribeAsync();
        onEmpty();
    }

    private async Task ReceiveUntilClosedAsync(RealtimeClientConnection client, CancellationToken ct)
    {
        var buffer = new byte[1024];
        while (client.IsOpen)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await client.Socket.ReceiveAsync(buffer, ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await client.CloseIfNeededAsync(ct);
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text
                && string.Equals(Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count)), "ping", StringComparison.OrdinalIgnoreCase))
                await client.SendAsync(Envelope(pong()), ct);
        }
    }

    private async Task BroadcastAsync(IReadOnlyList<RealtimeFrame> frames)
    {
        if (frames.Count == 0) return;
        RealtimeClientConnection[] open;
        await sync.WaitAsync();
        try
        {
            open = clients.Where(c => c.IsOpen).ToArray();
        }
        finally
        {
            sync.Release();
        }

        foreach (var frame in frames)
        {
            var envelope = Envelope(frame);
            await Task.WhenAll(open.Select(c => c.SendAsync(envelope, CancellationToken.None)));
        }
    }

    private PartyRealtimeEnvelope Envelope(RealtimeFrame frame) => new()
    {
        Type = frame.Type,
        Sequence = Interlocked.Increment(ref sequence),
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Data = frame.Data,
    };
}
