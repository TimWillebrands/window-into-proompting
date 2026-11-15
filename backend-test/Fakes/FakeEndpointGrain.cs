using System.Runtime.CompilerServices;
using Moq;
using Orleans.TestKit;
using PartyTown.Grains.Generation;

namespace BackendTest.Fakes;

public sealed class FakeEndpointGrain
{
    private int _activeGenerations;
    private readonly List<LlmModel> _models;
    private readonly List<LlmGenerationEvent> _scriptedChunks;

    private bool _choked;
    private int _failFirstN;
    private int _failAfterChunks = -1;
    private int _callCount;

    public Guid Id { get; }
    public int TotalJobsReceived { get; private set; }
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

    public FakeEndpointGrain Choke() { _choked = true; return this; }

    public FakeEndpointGrain FailFirstNAttempts(int n) { _failFirstN = n; return this; }

    public FakeEndpointGrain FailAfterChunks(int n) { _failAfterChunks = n; return this; }

    public string? Unreachable { get; set; }

    public Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken _)
        => Task.FromResult<IReadOnlyList<LlmModel>>(_models);

    public ValueTask<int> PressureAsync()
        => ValueTask.FromResult(Volatile.Read(ref _activeGenerations));

    public async IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationJob _,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var callIndex = Interlocked.Increment(ref _callCount);

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
        mock.Setup(g => g.PingAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(Unreachable));
        Reference = mock.Object;
        return Reference;
    }
}
