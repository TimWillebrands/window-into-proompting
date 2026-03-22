namespace PartyTown.Logging;

using System.Diagnostics;
using Orleans;

/// <summary>
/// Orleans grain call filter that automatically logs all grain method invocations.
/// Logs method entry, exit (with timing), and any exceptions.
/// </summary>
public sealed class GrainLoggingFilter(ILogger<GrainLoggingFilter> logger) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var grainType = context.TargetContext.GrainInstance?.GetType().Name ?? "Unknown";
        var grainKey = GetGrainKey(context.TargetContext);
        var methodName = context.ImplementationMethod.Name;

        using (logger.BeginGrainScope(grainType, grainKey))
        {
            var sw = Stopwatch.StartNew();

            logger.LogDebug("=> {MethodName}", methodName);

            try
            {
                await context.Invoke();
                sw.Stop();

                logger.LogDebug(
                    "<= {MethodName} ({ElapsedMs}ms)",
                    methodName,
                    sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();

                logger.LogError(
                    ex,
                    "<= {MethodName} FAILED ({ElapsedMs}ms)",
                    methodName,
                    sw.ElapsedMilliseconds);

                throw;
            }
        }
    }

    private static string GetGrainKey(IGrainContext grainContext)
    {
        var grainId = grainContext.GrainId;
        return grainId.Key.ToString() ?? "unknown";
    }
}
