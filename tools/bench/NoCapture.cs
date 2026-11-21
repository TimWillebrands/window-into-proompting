using PartyTown.Observability;

namespace PartyTown.Bench;

public sealed class NoCapture : IAiObservabilityCapture
{
    public Task CaptureGenerationAsync(LlmGeneration generation, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
