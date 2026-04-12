using System.Runtime.CompilerServices;
using Moq;
using Orleans.TestKit;
using PartyTown.Grains.Generation;

namespace BackendTest.Fakes;

/// <summary>
/// Stateful fake <see cref="IOllamaEndpointGrain"/> for use across multiple test classes.
///
/// Simulates pressure tracking and streaming behaviour of a real endpoint grain without
/// involving HTTP or a real LLM backend. Supports two usage modes:
///
///   • Auto-mode (via <see cref="GenerateAsync"/>): pressure increments on entry and
///     decrements on exit, matching the real grain's contract. Use <see cref="TotalJobsReceived"/>
///     to assert routing decisions.
///
///   • Manual-mode (via <see cref="HoldSlot"/> / <see cref="ReleaseSlot"/>): deterministic
///     pressure control for distribution tests that don't want to race real async completion.
///
/// Register on a TestKit silo via <see cref="RegisterOn"/>, then use <see cref="Reference"/>
/// to compare routing outcomes (Castle proxies break GetPrimaryKey — compare by reference).
///
/// Fault injection modes (mutually exclusive, applied via fluent setters before RegisterOn):
///   • <see cref="Choke"/>: every GenerateAsync call throws immediately.
///   • <see cref="FailFirstNAttempts"/>: first N calls throw, subsequent calls succeed.
///   • <see cref="FailAfterChunks"/>: stream N chunks then throw mid-stream.
/// </summary>
public sealed class FakeEndpointGrain
{
    private int _activeGenerations;
    private readonly List<LlmModel> _models;
    private readonly List<LlmGenerationEvent> _scriptedChunks;

    // ── fault injection ──────────────────────────────────────────────────────
    private bool _choked;
    private int _failFirstN;
    private int _failAfterChunks = -1; // -1 = no mid-stream error
    private int _callCount;

    public Guid Id { get; }
    /// <summary>Number of GenerateAsync calls that completed successfully (not faulted before yielding).</summary>
    public int TotalJobsReceived { get; private set; }
    /// <summary>Total GenerateAsync invocations including fault-injected failures.</summary>
    public int TotalAttempts => Volatile.Read(ref _callCount);
    public IOllamaEndpointGrain Reference { get; private set; } = null!;

    public FakeEndpointGrain(
        Guid id,
        JobComplexity supportedComplexities = JobComplexity.General,
        IEnumerable<LlmGenerationEvent>? scriptedChunks = null)
    {
        Id = id;
        _models =
        [
            new LlmModel
            {
                Name = $"test-model-{id:N}",
                EndpointProviderGrainId = id,
                ProviderType = "ollama",
                SupportedComplexities = supportedComplexities,
            }
        ];
        _scriptedChunks = scriptedChunks?.ToList() ??
        [
            new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, "hello"),
            new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, " world"),
        ];
    }

    // ── fault injection setters ───────────────────────────────────────────────

    /// <summary>Every GenerateAsync call throws <see cref="InvalidOperationException"/>.</summary>
    public FakeEndpointGrain Choke() { _choked = true; return this; }

    /// <summary>First <paramref name="n"/> GenerateAsync calls throw; subsequent calls succeed.</summary>
    public FakeEndpointGrain FailFirstNAttempts(int n) { _failFirstN = n; return this; }

    /// <summary>Yields <paramref name="n"/> chunks then throws mid-stream.</summary>
    public FakeEndpointGrain FailAfterChunks(int n) { _failAfterChunks = n; return this; }

    // ── grain interface ───────────────────────────────────────────────────────

    public Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken _)
        => Task.FromResult<IReadOnlyList<LlmModel>>(_models);

    public ValueTask<int> PressureAsync()
        => ValueTask.FromResult(Volatile.Read(ref _activeGenerations));

    public async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationJob _,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var callIndex = Interlocked.Increment(ref _callCount);

        // whole-call faults: throw before yielding anything
        if (_choked)
            throw new InvalidOperationException($"FakeEndpointGrain {Id:N} is choked");

        if (callIndex <= _failFirstN)
            throw new InvalidOperationException(
                $"FakeEndpointGrain {Id:N}: scripted failure on attempt {callIndex}/{_failFirstN}");

        Interlocked.Increment(ref _activeGenerations);
        TotalJobsReceived++;
        try
        {
            var chunksEmitted = 0;
            foreach (var chunk in _scriptedChunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();

                if (_failAfterChunks >= 0 && chunksEmitted >= _failAfterChunks)
                    throw new InvalidOperationException(
                        $"FakeEndpointGrain {Id:N}: scripted mid-stream error after {chunksEmitted} chunks");

                yield return chunk;
                chunksEmitted++;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeGenerations);
        }
    }

    public int AssignedSlots { get; private set; }

    public void HoldSlot()
    {
        AssignedSlots++;
        Interlocked.Increment(ref _activeGenerations);
    }

    public void ReleaseSlot() => Interlocked.Decrement(ref _activeGenerations);

    public IOllamaEndpointGrain RegisterOn(TestKitSilo silo)
    {
        var mock = silo.AddProbe<IOllamaEndpointGrain>(Id);
        mock.Setup(g => g.GetModelsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => GetModelsAsync(ct));
        mock.Setup(g => g.PressureAsync())
            .Returns(() => PressureAsync());
        mock.Setup(g => g.GenerateAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>()))
            .Returns((LlmGenerationJob j, CancellationToken ct) => GenerateAsync(j, ct));
        Reference = mock.Object;
        return Reference;
    }
}
