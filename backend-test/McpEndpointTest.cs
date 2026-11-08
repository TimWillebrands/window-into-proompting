using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using PartyTown.Mcp;
using PartyTown.Services.Auth;

namespace BackendTest;

/// <summary>The MCP endpoint's sign-in, as an agent meets it (ADR 0046).</summary>
public sealed class McpEndpointTest : IAsyncLifetime
{
    private const string Resource = "https://proompting.party/api/mcp";
    private const string Issuer = "https://clerk.proompting.party";
    private IHost _host = null!;
    private HttpClient _http = null!;
    /// <summary>What the fake Clerk says about the token it's asked about.</summary>
    private JsonObject? _clerkSays;
    private int _clerkAsked;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clerk:SecretKey"] = "sk_test_x",
                ["Mcp:Resource"] = Resource,
                ["Mcp:AuthorizationServer"] = Issuer,
            }))
            .ConfigureServices((ctx, services) =>
            {
                services.AddRouting();
                services.AddSingleton(Mock.Of<IGrainFactory>());
                services.AddClerkAuth(ctx.Configuration);
                services.AddHttpClient<ClerkOAuthTokens>().ConfigurePrimaryHttpMessageHandler(() => new FakeClerk(this));
                services.AddPartyMcp();
            })
            .Configure(app =>
            {
                app.UseProxiedScheme();
                app.UsePathBase("/api");
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(e =>
                {
                    e.MapGet("/worlds/x", () => "ok");
                    e.MapPartyMcp();
                });
            })).StartAsync();
        _http = _host.GetTestClient();
        // As kamal-proxy hands it over: TLS ended, the scheme in a header.
        _http.BaseAddress = new Uri("http://proompting.party");
        _http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task WithoutAToken_ItPointsAtWhereToSignIn()
    {
        var res = await _http.SendAsync(ListTools(null));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var challenge = res.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource_metadata=\"https://proompting.party/api/.well-known/oauth-protected-resource/mcp\"", challenge);

        var meta = JsonNode.Parse(await _http.GetStringAsync("/api/.well-known/oauth-protected-resource/mcp"))!;
        Assert.Equal(Resource, meta["resource"]!.ToString());
        Assert.Equal(Issuer, meta["authorization_servers"]![0]!.ToString());
    }

    [Theory]
    [InlineData("oat_abc123")]
    [InlineData("JWT")]
    public async Task AnAgentsTokenForThisEndpoint_ReachesTheTools(string token)
    {
        _clerkSays = Granted(Resource);

        var res = await _http.SendAsync(ListTools(token == "JWT" ? AccessJwt() : token));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var tools = Result(await res.Content.ReadAsStringAsync())["tools"]!.AsArray().Select(t => t!["name"]!.ToString()).ToList();
        Assert.Contains("whoami", tools);
        Assert.Contains("redesign_home", tools);
        Assert.Equal(1, _clerkAsked);
    }

    [Fact]
    public async Task ATokenGrantedForSomethingElse_IsTurnedAway()
    {
        _clerkSays = Granted("https://elsewhere.example/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(ListTools("oat_abc123"))).StatusCode);
    }

    [Fact]
    public async Task AnAgentsToken_OpensNothingButTheMcpEndpoint()
    {
        _clerkSays = Granted(Resource);
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/worlds/x");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessJwt());

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(req)).StatusCode);
        Assert.Equal(0, _clerkAsked);
    }

    [Fact]
    public void ARevokedOrUserlessGrant_IsNoGrant()
    {
        Assert.Null(ClerkOAuthTokens.Grant(JsonNode.Parse("""{"subject": "user_a", "revoked": true}""")!.AsObject()));
        Assert.Null(ClerkOAuthTokens.Grant(JsonNode.Parse("""{"subject": "user_a", "expired": true}""")!.AsObject()));
        Assert.Null(ClerkOAuthTokens.Grant(JsonNode.Parse("""{"subject": "mch_robot"}""")!.AsObject()));
        var grant = ClerkOAuthTokens.Grant(JsonNode.Parse("""{"subject": "user_a", "client_id": "c", "aud": "x"}""")!.AsObject())!;
        Assert.Equal(("user_a", "c"), (grant.UserId, grant.ClientId));
        Assert.True(grant.Fits("x"));
        Assert.False(grant.Fits("y"));
        Assert.True(grant.Fits(null));
    }

    [Fact]
    public void ASessionToken_IsNoAgentToken()
    {
        var session = Jwt("""{"alg":"RS256","typ":"JWT"}""");

        Assert.False(ClerkOAuthTokens.IsOAuth(session));
        Assert.True(ClerkOAuthTokens.IsOAuth(AccessJwt()));
        Assert.True(ClerkOAuthTokens.IsOAuth("oat_abc"));
    }

    private static JsonObject Granted(string aud)
        => new() { ["subject"] = "user_a", ["client_id"] = "claude-code", ["aud"] = new JsonArray(aud), ["revoked"] = false, ["expired"] = false };

    private static string AccessJwt() => Jwt("""{"alg":"RS256","typ":"at+jwt"}""");

    private static string Jwt(string header)
        => $"{B64(header)}.{B64("""{"sub":"user_a"}""")}.c2ln";

    private static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HttpRequestMessage ListTools(string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/mcp")
        {
            Content = new StringContent("""{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""", Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    /// <summary>The JSON-RPC result, whether it came as plain JSON or one SSE event.</summary>
    private static JsonNode Result(string body)
    {
        var json = body.TrimStart().StartsWith('{')
            ? body
            : string.Join("", body.Split('\n').Where(l => l.StartsWith("data:")).Select(l => l["data:".Length..]));
        var node = JsonNode.Parse(json)!;
        return node["result"] ?? throw new Exception(body);
    }

    private sealed class FakeClerk(McpEndpointTest test) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref test._clerkAsked);
            Assert.Equal(ClerkOAuthTokens.VerifyUrl, request.RequestUri!.ToString());
            return Task.FromResult(test._clerkSays is { } says
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(says.ToJsonString(), Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
