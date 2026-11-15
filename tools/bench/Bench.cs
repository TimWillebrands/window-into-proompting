using Microsoft.Extensions.Logging;
using PartyTown.Grains.Generation;
using PartyTown.Services.Memory;

namespace PartyTown.Bench;

public sealed class Bench(IGrainFactory grains, ILoggerFactory loggerFactory, IMemoryRepository memory, ProbeArtifact artifact, CancellationToken cancellation = default)
{
    public IGrainFactory Grains { get; } = grains;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public ProbeArtifact Artifact { get; } = artifact;

    public IMemoryRepository Memory { get; } = memory;

    public CancellationToken Cancellation { get; } = cancellation;

    public ILlmRouterGrain Router => Grains.GetGrain<ILlmRouterGrain>(0);

    public void Observe(string label, object? data) => Artifact.Observe(label, data);

    public ILogger Logger(string category) => LoggerFactory.CreateLogger(category);
}
