using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace PartyTown.Configuration;

/// <summary>Reaches a Tailnet through its SOCKS5 sidecar, named in config as <c>Tailnets:name=socks5://host:port</c> (ADR 0033).</summary>
public sealed class Tailnets(IConfiguration config)
{
    public const string SectionName = "Tailnets";

    /// <summary>The OpenAI client tries 4 times, so a sleeping box stalls a call 4x this before it's benched.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, string> _proxies = config.GetSection(SectionName).GetChildren()
        .Where(c => !string.IsNullOrWhiteSpace(c.Value))
        .ToDictionary(c => c.Key, c => c.Value!, StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> Names => _proxies.Keys.Order().ToList();

    /// <summary>Null means connect directly. Shared and long-lived: don't dispose it.</summary>
    public HttpClient? ClientFor(string? tailnet)
        => string.IsNullOrWhiteSpace(tailnet) ? null : _clients.GetOrAdd(tailnet, Create);

    // Not IHttpClientFactory: its default resilience handler would cut generations off at 30s.
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

    /// <summary>Never falls back to direct: a provider on a missing Tailnet is unreachable, so the router benches it.</summary>
    private sealed class NotConfigured(string tailnet) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException($"Tailnet '{tailnet}' is not configured");
    }
}
