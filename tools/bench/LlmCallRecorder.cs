using System.Collections.Concurrent;
using System.Diagnostics;
using Orleans.Serialization.Invocation;
using PartyTown.Grains.Generation;

namespace PartyTown.Bench;

/// <summary>
/// Silo-wide incoming grain call filter that records every <see cref="LlmGenerationJob"/>
/// flowing into any <see cref="ILlmEndpointGrain"/>. This is how probes capture the exact
/// composed prompts without touching the services that build them (ADR 0011). The call
/// proceeds untouched — unlike the test suite's FanoutInterceptor, nothing is short-circuited.
/// </summary>
/// <remarks>
/// Two dispatch shapes carry a job, and both must be unwrapped:
///   • <c>CompleteOneShotAsync</c> arrives as a direct call on <see cref="ILlmEndpointGrain"/>
///     with the job as argument 0.
///   • <c>GenerateAsync</c> returns <c>IAsyncEnumerable</c>, so Orleans dispatches it through
///     <c>IAsyncEnumerableGrainExtension.StartEnumeration</c> — the real invocation (and its
///     job argument) is wrapped in argument 1 as a nested <see cref="IInvokable"/>. Matching only
///     the direct shape silently misses every streamed generation, which is the common path.
/// </remarks>
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

        // For streamed GenerateAsync the elapsed time covers StartEnumeration setup only — the
        // tokens are pulled by the caller via later MoveNext calls. One-shot calls are fully timed.
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

        // Direct non-streaming completion on the endpoint grain (CompleteOneShotAsync).
        if (context.InterfaceMethod?.DeclaringType == typeof(ILlmEndpointGrain)
            && req.GetArgumentCount() > 0
            && req.GetArgument(0) is LlmGenerationJob oneShot)
        {
            return (oneShot, context.InterfaceMethod.Name);
        }

        // Streamed GenerateAsync, wrapped in the async-enumerable extension's StartEnumeration.
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
