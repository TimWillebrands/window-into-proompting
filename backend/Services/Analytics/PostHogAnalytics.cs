using PostHog;
using PostHog.AspNetCore;
using PostHog.Config;
using PartyTown.Services.Auth;

namespace PartyTown.Services.Analytics;

/// <summary>PostHog wiring: off unless POSTHOG_PROJECT_TOKEN and POSTHOG_HOST are set.</summary>
public static class PostHogAnalytics
{
    public static void AddPostHogAnalytics(this WebApplicationBuilder builder)
    {
        var token = builder.Configuration["POSTHOG_PROJECT_TOKEN"];
        var host = builder.Configuration["POSTHOG_HOST"];
        var enabled = !string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(host);
        var environment = builder.Environment.EnvironmentName;

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

    /// <summary>After auth, so unhandled exceptions carry the verified Clerk id; client aborts are noise.</summary>
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
                    ["path"] = context.Request.Path.Value ?? "",
                    ["method"] = context.Request.Method,
                };
                if (context.User.ClerkUserId() is { } userId) posthog.CaptureException(ex, userId, properties);
                else posthog.CaptureException(ex, properties);
                throw;
            }
        });
    }
}
