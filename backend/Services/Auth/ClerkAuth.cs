using System.Security.Claims;
using System.Text.Encodings.Web;
using Clerk.BackendAPI.Helpers.Jwks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace PartyTown.Services.Auth;

public sealed class ClerkOptions
{
    public string? SecretKey { get; set; }
    public string[] AuthorizedParties { get; set; } = Array.Empty<string>();
    public string[] AdminUserIds { get; set; } = Array.Empty<string>();
}

/// <summary>Verifies Clerk session tokens from the bearer header or the __session cookie.</summary>
public sealed class ClerkAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ClerkOptions> clerk,
    IOptions<McpOptions> mcp,
    ClerkOAuthTokens oauth) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Clerk";
    public const string SessionCookie = "__session";

    /// <summary>ASP.NET's cookie parser, because the SDK's splits on commas and throws on valueless cookies.</summary>
    public static string? ReadToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return header["Bearer ".Length..].Trim();
        return request.Cookies[SessionCookie] is { Length: > 0 } cookie ? cookie : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (ReadToken(Request) is not { } token) return AuthenticateResult.NoResult();
        if (ClerkOAuthTokens.IsOAuth(token)) return await OAuthAsync(token);
        if (TokenTypeHelper.GetTokenType(token) != TokenType.SessionToken) return AuthenticateResult.Fail("Not a session token.");
        try
        {
            var verified = await VerifyToken.VerifyTokenAsync(token, new VerifyTokenOptions(
                secretKey: clerk.Value.SecretKey,
                authorizedParties: clerk.Value.AuthorizedParties));
            var userId = verified.FindFirst("sub")?.Value ?? verified.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return string.IsNullOrEmpty(userId) ? AuthenticateResult.Fail("Token has no subject.") : Signed(userId);
        }
        catch (TokenVerificationException ex)
        {
            // Expiry is routine: tokens live a minute and a stale cookie just means anonymous.
            Logger.LogDebug(ex, "Clerk token rejected");
            return AuthenticateResult.Fail("Invalid session token.");
        }
    }

    /// <summary>An agent's OAuth access token (ADR 0046): only the MCP endpoint takes one, and only one granted for it.</summary>
    private async Task<AuthenticateResult> OAuthAsync(string token)
    {
        if (!Request.Path.StartsWithSegments(ClerkAuth.McpPath)) return AuthenticateResult.Fail("OAuth tokens only reach the MCP endpoint.");
        var grant = await oauth.VerifyAsync(token, Context.RequestAborted);
        if (grant is null) return AuthenticateResult.Fail("Invalid OAuth token.");
        return grant.Fits(mcp.Value.Resource) ? Signed(grant.UserId, grant.ClientId) : AuthenticateResult.Fail("OAuth token isn't for this endpoint.");
    }

    private AuthenticateResult Signed(string userId, string? oauthClient = null)
    {
        var claims = new List<Claim> { new(ClerkAuth.UserIdClaim, userId) };
        if (oauthClient is not null) claims.Add(new Claim(ClerkAuth.OAuthClientClaim, oauthClient));
        if (clerk.Value.AdminUserIds.Contains(userId)) claims.Add(new Claim(ClaimTypes.Role, ClerkAuth.AdminRole));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme));
    }
}

public static class ClerkAuth
{
    public const string UserIdClaim = "clerk_user_id";
    public const string AdminRole = "admin";
    public const string AdminPolicy = "Admin";
    /// <summary>The agent app an OAuth token was granted to.</summary>
    public const string OAuthClientClaim = "oauth_client_id";
    /// <summary>Where agents connect, below the /api path base.</summary>
    public const string McpPath = "/mcp";
    /// <summary>McpAuth forwards sign-in to Clerk; a refusal answers like an MCP server, pointing at where to sign in.</summary>
    public const string McpPolicy = "Mcp";

    /// <summary>Closed by default: every endpoint needs sign-in unless marked AllowAnonymous.</summary>
    public static IServiceCollection AddClerkAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ClerkOptions>()
            .Bind(config.GetSection("Clerk"))
            .Configure(o => o.SecretKey = config["CLERK_SECRET_KEY"]);
        var mcp = config.GetSection("Mcp").Get<McpOptions>() ?? new McpOptions();
        services.Configure<McpOptions>(config.GetSection("Mcp"));
        services.AddHttpClient<ClerkOAuthTokens>(c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddAuthentication(ClerkAuthHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ClerkAuthHandler>(ClerkAuthHandler.Scheme, null)
            .AddMcp(o =>
            {
                o.ForwardAuthenticate = ClerkAuthHandler.Scheme;
                o.ResourceMetadata = new ProtectedResourceMetadata { Resource = mcp.Resource, ResourceName = "Proompting Party" };
                if (mcp.AuthorizationServer is { } server) o.ResourceMetadata.AuthorizationServers.Add(server);
                o.ResourceMetadata.ScopesSupported.Add("profile");
                o.ResourceMetadata.ScopesSupported.Add("email");
            });
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, p => p.RequireRole(AdminRole))
            .AddPolicy(McpPolicy, p => p.AddAuthenticationSchemes(McpAuthenticationDefaults.AuthenticationScheme).RequireAuthenticatedUser());
        services.AddSingleton<Actors>();
        return services;
    }

    public static string? ClerkUserId(this ClaimsPrincipal user) => user.FindFirst(UserIdClaim)?.Value;
}

public sealed class ForbiddenActorException(string message) : Exception(message);

/// <summary>Signed in, but hasn't made a character in this World yet.</summary>
public sealed class NoPlayerException() : Exception("Create your character first.");

/// <summary>The caller's Player is on auto, so the agent acts for them.</summary>
public sealed class OnAutoException() : Exception("Your character is on auto.");

/// <summary>Turns an identity refusal from deep in an action into a 403 or 409.</summary>
public sealed class ForbiddenActorFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        int? status = context.Exception switch
        {
            ForbiddenActorException => StatusCodes.Status403Forbidden,
            NoPlayerException or OnAutoException => StatusCodes.Status409Conflict,
            _ => null,
        };
        if (status is null) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = context.Exception.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
