using System.Collections.Concurrent;
using System.Diagnostics;
using Orleans.Serialization.Invocation;
using PartyTown.Grains.Generation;

namespace PartyTown.Bench;

public sealed class LlmCallRecorder : IIncomingGrainCallFilter
{
    public sealed record Recorded(
        int Seq,
        string Method,
        string EndpointGrain,
        JobComplexity Complexity,
        IReadOnlyList<LlmChatMessage> Messages,
        string? ResponseFormat,
        DateTimeOffset At,
        double InvokeMs);

    private static readonly ConcurrentQueue<Recorded> _calls = new();
    private static int _seq;

    public static IReadOnlyCollection<Recorded> Calls => _calls;
    public static void Reset() { _calls.Clear(); Interlocked.Exchange(ref _seq, 0); }

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var (job, method) = ExtractJob(context);
        if (job is null)
        {
            await context.Invoke();
            return;
        }

        var sw = Stopwatch.StartNew();
        await context.Invoke();
        sw.Stop();

        _calls.Enqueue(new Recorded(
            Interlocked.Increment(ref _seq),
            method,
            context.Grain.GetType().Name,
            job.JobComplexity,
            job.Messages,
            job.ResponseFormat,
            DateTimeOffset.UtcNow,
            sw.Elapsed.TotalMilliseconds));
    }

    private static (LlmGenerationJob? Job, string Method) ExtractJob(IIncomingGrainCallContext context)
    {
        var req = context.Request;

        if (context.InterfaceMethod?.DeclaringType == typeof(ILlmEndpointGrain)
            && req.GetArgumentCount() > 0
            && req.GetArgument(0) is LlmGenerationJob oneShot)
        {
            return (oneShot, context.InterfaceMethod.Name);
        }

        if (context.Grain is ILlmEndpointGrain
            && context.InterfaceMethod?.Name == "StartEnumeration"
            && req.GetArgumentCount() >= 2
            && req.GetArgument(1) is IInvokable inner
            && inner.GetArgumentCount() > 0
            && inner.GetArgument(0) is LlmGenerationJob streamed)
        {
            return (streamed, inner.GetMethod()?.Name ?? "GenerateAsync");
        }

        return (null, string.Empty);
    }
}
