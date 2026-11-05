using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BackendTest.Fakes;

/// <summary>Minimal SOCKS5 proxy standing in for the Tailscale sidecar; answers HTTP itself instead of dialling out.</summary>
public sealed class FakeSocksProxy : IAsyncDisposable
{
    public const byte Succeeded = 0, HostUnreachable = 4;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    /// <summary>Reply to CONNECT; null never answers, like a dial to a sleeping peer.</summary>
    public byte? Reply { get; init; } = Succeeded;

    public string Body { get; init; } = "{}";
    public string ContentType { get; init; } = "application/json";

    /// <summary>Hosts as received: a name here proves DNS was left to the sidecar.</summary>
    public List<string> Connects { get; } = new();

    public string Url => $"socks5://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public FakeSocksProxy()
    {
        _listener.Start();
        _loop = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true) _ = ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token));
        }
        catch (OperationCanceledException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var s = client.GetStream();
        var ct = _stop.Token;
        try
        {
            var greeting = await ReadAsync(s, 2, ct);
            await ReadAsync(s, greeting[1], ct);
            await s.WriteAsync(new byte[] { 5, 0 }, ct);

            var head = await ReadAsync(s, 4, ct);
            var host = head[3] switch
            {
                1 => new IPAddress(await ReadAsync(s, 4, ct)).ToString(),
                3 => Encoding.ASCII.GetString(await ReadAsync(s, (await ReadAsync(s, 1, ct))[0], ct)),
                _ => new IPAddress(await ReadAsync(s, 16, ct)).ToString(),
            };
            await ReadAsync(s, 2, ct);
            lock (Connects) Connects.Add(host);

            if (Reply is not { } reply)
            {
                await Task.Delay(Timeout.Infinite, ct);
                return;
            }
            await s.WriteAsync(new byte[] { 5, reply, 0, 1, 0, 0, 0, 0, 0, 0 }, ct);
            if (reply != Succeeded) return;

            await ReadHeadersAsync(s, ct);
            var body = Encoding.UTF8.GetBytes(Body);
            var response = $"HTTP/1.1 200 OK\r\nContent-Type: {ContentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
            await s.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
            await s.WriteAsync(body, ct);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
    }

    private static async Task<byte[]> ReadAsync(NetworkStream s, int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        await s.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    private static async Task ReadHeadersAsync(NetworkStream s, CancellationToken ct)
    {
        var seen = new List<byte>();
        var one = new byte[1];
        while (seen.Count < 4 || !seen.TakeLast(4).SequenceEqual("\r\n\r\n"u8.ToArray()))
        {
            await s.ReadExactlyAsync(one, ct);
            seen.Add(one[0]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        await _loop;
    }
}
