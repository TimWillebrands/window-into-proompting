using PostHog;
using PostHog.AspNetCore;
using PostHog.Config;
using PartyTown.Services.Auth;

namespace PartyTown.Services.Analytics;

/// <summary>PostHog wiring: off unless POSTHOG_PROJECT_TOKEN and POSTHOG_HOST are set.</summary>
public static class PostHogAnalytics
{
    private const string SessionHeader = "X-POSTHOG-SESSION-ID";
    private const string ConsentCookie = "analytics_consent";

    public static void AddPostHogAnalytics(this WebApplicationBuilder builder)
    {
        var token = builder.Configuration["POSTHOG_PROJECT_TOKEN"];
        var host = builder.Configuration["POSTHOG_HOST"];
        var enabled = !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(host);
        var environment = builder.Environment.EnvironmentName.ToLowerInvariant();

        builder.AddPostHog(posthog => posthog.PostConfigure(options =>
        {
            options.Disabled = !enabled;
            if (!enabled) return;
            options.ProjectToken = token;
            options.HostUrl = new Uri(host!);
            options.SuperProperties["environment"] = environment;
        }));

        // A disabled client warns on every call.
        if (!enabled) builder.Logging.AddFilter("PostHog", LogLevel.Error);
    }

    /// <summary>A signed-in action: with consent the person is the verified Clerk id, without it only an unlinkable count.</summary>
    public static void Track(this IPostHogClient posthog, HttpContext context, string name, Dictionary<string, object> properties)
    {
        if (Consented(context)) posthog.Capture(context.User.ClerkUserId()!, name, WithSession(context, properties));
        else posthog.Capture(name, Anonymized(properties));
    }

    /// <summary>After auth, so a consenting user's exceptions carry their Clerk id; anyone else's stay personless.</summary>
    public static void UsePostHogAnalytics(this WebApplication app)
    {
        var posthog = app.Services.GetRequiredService<IPostHogClient>();
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested)
            {
                var properties = new Dictionary<string, object>
                {
                    // The template, as the real path holds ids.
                    ["path"] = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched",
                    ["method"] = context.Request.Method,
                };
                if (Consented(context) && context.User.ClerkUserId() is { } userId)
                    posthog.CaptureException(ex, userId, WithSession(context, properties));
                else
                    posthog.CaptureException(ex, properties);
                throw;
            }
        });
    }

    /// <summary>Any id but the shared World's can lead back to a person, so it goes.</summary>
    private static Dictionary<string, object> Anonymized(Dictionary<string, object> properties)
    {
        var kept = properties
            .Where(p => p.Key == "party_id" || !p.Key.EndsWith("_id", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => p.Value);
        kept["anonymous"] = true;
        return kept;
    }

    private static bool Consented(HttpContext context) => context.Request.Cookies[ConsentCookie] == "1";

    /// <summary>The header is client input, so only a well-formed id gets through.</summary>
    private static Dictionary<string, object> WithSession(HttpContext context, Dictionary<string, object> properties)
    {
        if (Guid.TryParse(context.Request.Headers[SessionHeader].ToString(), out var session))
            properties["$session_id"] = session.ToString();
        return properties;
    }
}
