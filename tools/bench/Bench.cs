using Microsoft.Extensions.Logging;
using PartyTown.Grains.Generation;

namespace PartyTown.Bench;

/// <summary>
/// The context handed to every probe (ADR 0011). Exposes the live grain factory and the real
/// <see cref="ILlmRouterGrain"/> so a probe drives the production code path, plus an
/// <see cref="ProbeArtifact"/> to record observations into. The probe builds its cast and
/// history with normal code, invokes the slice under design, and observes — it never asserts.
/// </summary>
public sealed class Bench(IGrainFactory grains, ILoggerFactory loggerFactory, ProbeArtifact artifact, CancellationToken cancellation = default)
{
    public IGrainFactory Grains { get; } = grains;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public ProbeArtifact Artifact { get; } = artifact;

    /// <summary>Fires at the probe deadline — pass into every async call so a timed-out probe
    /// actually stops instead of racing the artifact write.</summary>
    public CancellationToken Cancellation { get; } = cancellation;

    /// <summary>The router that routes generation jobs to the configured endpoint grains.</summary>
    public ILlmRouterGrain Router => Grains.GetGrain<ILlmRouterGrain>(0);

    /// <summary>Record a timestamped observation into the artifact.</summary>
    public void Observe(string label, object? data) => Artifact.Observe(label, data);

    public ILogger Logger(string category) => LoggerFactory.CreateLogger(category);
}
