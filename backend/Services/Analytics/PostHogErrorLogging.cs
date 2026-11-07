using System.Runtime.CompilerServices;
using PostHog;
using PostHog.AspNetCore;

namespace PartyTown.Services.Analytics;

/// <summary>Each exception reaches PostHog error tracking once, however many layers log it.</summary>
public static class PostHogExceptions
{
    private static readonly ConditionalWeakTable<Exception, object> Captured = new();

    /// <summary>True the first time for an exception, so only that caller captures it.</summary>
    public static bool Claim(Exception exception) => Captured.TryAdd(exception, Captured);
}

/// <summary>Error logs with an exception go to PostHog error tracking: grains, timers and hosted services, not only requests.</summary>
public sealed class PostHogErrorLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    // The grain filter logs every failed call, handled ones included; PostHog's own logs would loop.
    private static readonly string[] Skipped = { "PartyTown.Logging.GrainLoggingFilter", "PostHog" };

    private IPostHogClient? _client;

    public ILogger CreateLogger(string categoryName)
        => Skipped.Any(categoryName.StartsWith) ? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance : new ErrorLogger(categoryName, this);

    public void Dispose() { }

    // Resolved on first use: the client logs through this same factory.
    private IPostHogClient? Client => _client ??= services.GetService<IPostHogClient>();

    private sealed class ErrorLogger(string category, PostHogErrorLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is null || !IsEnabled(logLevel) || !PostHogExceptions.Claim(exception)) return;
            provider.Client?.CaptureException(exception, new Dictionary<string, object>
            {
                ["logger"] = category,
                ["log_message"] = formatter(state, exception),
                ["log_level"] = logLevel.ToString(),
            });
        }
    }
}
