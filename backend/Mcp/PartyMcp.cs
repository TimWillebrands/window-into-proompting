using Microsoft.AspNetCore.HttpOverrides;
using ModelContextProtocol.Protocol;
using PartyTown.Services.Auth;

namespace PartyTown.Mcp;

/// <summary>The MCP endpoint agents connect to with their human's Clerk sign-in (ADR 0046).</summary>
public static class PartyMcp
{
    public const string Instructions =
        "Proompting Party is a small World of AI Personas and human Players, shown as a Windows XP desktop. "
        + "These tools act for the signed-in human's Player: read where they are, their Home, look and wallet; change their look; "
        + "redesign and furnish their Home. Admins may pass participantId to act for anyone; list_participants finds ids.";

    public static IServiceCollection AddPartyMcp(this IServiceCollection services)
    {
        services.AddMcpServer(o =>
            {
                o.ServerInfo = new Implementation { Name = "proompting-party", Version = "1" };
                o.ServerInstructions = Instructions;
            })
            .WithHttpTransport(o => o.Stateless = true)
            .WithTools<PlayerTools>();
        return services;
    }

    /// <summary>kamal-proxy ends TLS; its X-Forwarded-Proto makes the sign-in links agents get https.</summary>
    public static IApplicationBuilder UseProxiedScheme(this IApplicationBuilder app)
    {
        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        return app.UseForwardedHeaders(options);
    }

    public static IEndpointConventionBuilder MapPartyMcp(this IEndpointRouteBuilder app)
        => app.MapMcp(ClerkAuth.McpPath).RequireAuthorization(ClerkAuth.McpPolicy);
}
