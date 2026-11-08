using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Clerk.BackendAPI.Helpers.Jwks;
using Microsoft.Extensions.Options;

namespace PartyTown.Services.Auth;

/// <summary>The MCP endpoint as agents see it (ADR 0046).</summary>
public sealed class McpOptions
{
    /// <summary>Public URL of the endpoint, the OAuth resource; tokens must name it in aud. Null derives it per request (dev).</summary>
    public string? Resource { get; set; }
    /// <summary>Clerk's Frontend API URL, the authorization server agents sign in at.</summary>
    public string? AuthorizationServer { get; set; }
    /// <summary>The World the tools act in; there's one, the root.</summary>
    public Guid World { get; set; } = Guid.Empty;
}

/// <summary>An agent's OAuth access token, as Clerk vouches for it.</summary>
public sealed record OAuthGrant(string UserId, string ClientId, IReadOnlyList<string> Audience)
{
    /// <summary>Granted for this endpoint: aud names it, or no resource is pinned (dev).</summary>
    public bool Fits(string? resource) => resource is null || Audience.Contains(resource);
}

/// <summary>Asks Clerk about OAuth access tokens, opaque or JWT; asking catches revocation, a signature can't.</summary>
public sealed class ClerkOAuthTokens(HttpClient http, IOptions<ClerkOptions> clerk, ILogger<ClerkOAuthTokens> logger)
{
    public const string VerifyUrl = "https://api.clerk.com/v1/oauth_applications/access_tokens/verify";

    /// <summary>oat_ opaque tokens, or JWTs typed as access tokens (RFC 9068); Clerk session JWTs aren't.</summary>
    public static bool IsOAuth(string token)
        => TokenTypeHelper.GetTokenType(token) == TokenType.OAuthToken || JwtType(token) is "at+jwt" or "application/at+jwt";

    public async Task<OAuthGrant?> VerifyAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, VerifyUrl) { Content = JsonContent.Create(new { access_token = token }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", clerk.Value.SecretKey);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("Clerk refused an OAuth token: {Status}", (int)response.StatusCode);
            return null;
        }
        return Grant(await response.Content.ReadFromJsonAsync<JsonObject>(ct));
    }

    /// <summary>Clerk's verify answer as a grant; null when revoked, expired or without a user.</summary>
    public static OAuthGrant? Grant(JsonObject? body)
    {
        if (body is null || Flag(body["revoked"]) || Flag(body["expired"])) return null;
        if (body["subject"]?.ToString() is not { Length: > 0 } user || !user.StartsWith("user_")) return null;
        var aud = body["aud"] switch
        {
            JsonArray a => a.Select(x => x?.ToString()).OfType<string>().ToList(),
            JsonValue v => new List<string> { v.ToString() },
            _ => new List<string>(),
        };
        return new OAuthGrant(user, body["client_id"]?.ToString() ?? string.Empty, aud);
    }

    private static bool Flag(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? JwtType(string token)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0) return null;
        try
        {
            var head = token[..dot].Replace('-', '+').Replace('_', '/');
            head = head.PadRight(head.Length + (4 - head.Length % 4) % 4, '=');
            return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(head)))?["typ"]?.ToString().ToLowerInvariant();
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
