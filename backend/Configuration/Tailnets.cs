using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace PartyTown.Configuration;

public sealed class Tailnets(IConfiguration config)
{
    public const string SectionName = "Tailnets";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, string> _proxies = config.GetSection(SectionName).GetChildren()
        .Where(c => !string.IsNullOrWhiteSpace(c.Value))
        .ToDictionary(c => c.Key, c => c.Value!, StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Names => _proxies.Keys.Order().ToList();

    public HttpClient? ClientFor(string? tailnet)
        => string.IsNullOrWhiteSpace(tailnet) ? null : _clients.GetOrAdd(tailnet, Create);

    private HttpClient Create(string tailnet)
    {
        HttpMessageHandler handler = _proxies.TryGetValue(tailnet, out var proxy)
            ? new SocketsHttpHandler
            {
                Proxy = new WebProxy(proxy),
                UseProxy = true,
                ConnectTimeout = ConnectTimeout,
                UseCookies = false,
                AllowAutoRedirect = false,
            }
            : new NotConfigured(tailnet);
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private sealed class NotConfigured(string tailnet) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException($"Tailnet '{tailnet}' is not configured");
    }
}
