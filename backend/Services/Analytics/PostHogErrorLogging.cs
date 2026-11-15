using System.Runtime.CompilerServices;
using PostHog;
using PostHog.AspNetCore;

namespace PartyTown.Services.Analytics;

public static class PostHogExceptions
{
    private static readonly ConditionalWeakTable<Exception, object> Captured = new();

    public static bool Claim(Exception exception) => Captured.TryAdd(exception, Captured);
}

public sealed class PostHogErrorLoggerProvider(IServiceProvider services) : ILoggerProvider
{
    private static readonly string[] Skipped = { "PartyTown.Logging.GrainLoggingFilter", "PostHog" };

    private IPostHogClient? _client;

    public ILogger CreateLogger(string categoryName)
        => Skipped.Any(categoryName.StartsWith) ? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance : new ErrorLogger(categoryName, this);

    public void Dispose() { }

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
