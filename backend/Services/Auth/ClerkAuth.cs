using System.Security.Claims;
using System.Text.Encodings.Web;
using Clerk.BackendAPI.Helpers.Jwks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

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
    IOptions<ClerkOptions> clerk) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
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
        if (TokenTypeHelper.GetTokenType(token) != TokenType.SessionToken) return AuthenticateResult.Fail("Not a session token.");
        try
        {
            var verified = await VerifyToken.VerifyTokenAsync(token, new VerifyTokenOptions(
                secretKey: clerk.Value.SecretKey,
                authorizedParties: clerk.Value.AuthorizedParties));
            var userId = verified.FindFirst("sub")?.Value ?? verified.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return AuthenticateResult.Fail("Token has no subject.");

            var claims = new List<Claim> { new(ClerkAuth.UserIdClaim, userId) };
            if (clerk.Value.AdminUserIds.Contains(userId)) claims.Add(new Claim(ClaimTypes.Role, ClerkAuth.AdminRole));
            return AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme));
        }
        catch (TokenVerificationException ex)
        {
            // Expiry is routine: tokens live a minute and a stale cookie just means anonymous.
            Logger.LogDebug(ex, "Clerk token rejected");
            return AuthenticateResult.Fail("Invalid session token.");
        }
    }
}

public static class ClerkAuth
{
    public const string UserIdClaim = "clerk_user_id";
    public const string AdminRole = "admin";
    public const string AdminPolicy = "Admin";

    /// <summary>Closed by default: every endpoint needs sign-in unless marked AllowAnonymous.</summary>
    public static IServiceCollection AddClerkAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ClerkOptions>()
            .Bind(config.GetSection("Clerk"))
            .Configure(o => o.SecretKey = config["CLERK_SECRET_KEY"]);
        services.AddAuthentication(ClerkAuthHandler.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ClerkAuthHandler>(ClerkAuthHandler.Scheme, null);
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, p => p.RequireRole(AdminRole));
        services.AddSingleton<Actors>();
        return services;
    }

    public static string? ClerkUserId(this ClaimsPrincipal user) => user.FindFirst(UserIdClaim)?.Value;
}

public sealed class ForbiddenActorException(string message) : Exception(message);

/// <summary>Signed in, but hasn't made a character in this World yet.</summary>
public sealed class NoPlayerException() : Exception("Create your character first.");

/// <summary>Turns an identity refusal from deep in an action into a 403 or 409.</summary>
public sealed class ForbiddenActorFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        int? status = context.Exception switch
        {
            ForbiddenActorException => StatusCodes.Status403Forbidden,
            NoPlayerException => StatusCodes.Status409Conflict,
            _ => null,
        };
        if (status is null) return;
        context.Result = new ObjectResult(new ProblemDetails { Status = status, Title = context.Exception.Message }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}
