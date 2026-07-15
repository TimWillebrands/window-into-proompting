using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PartyTown.Data;
using PartyTown.Grains.Generation;
using PartyTown.Services.Memory;

namespace PartyTown.Bench;

/// <summary>
/// The context handed to every probe (ADR 0011). Exposes the live grain factory and the real
/// <see cref="ILlmRouterGrain"/> so a probe drives the production code path, plus an
/// <see cref="ProbeArtifact"/> to record observations into. The probe builds its cast and
/// history with normal code, invokes the slice under design, and observes — it never asserts.
/// </summary>
public sealed class Bench(IGrainFactory grains, ILoggerFactory loggerFactory, IMemoryRepository memory, ProbeArtifact artifact, CancellationToken cancellation = default, IDbContextFactory<AppDbContext>? memoryDb = null)
{
    public IGrainFactory Grains { get; } = grains;
    public ILoggerFactory LoggerFactory { get; } = loggerFactory;
    public ProbeArtifact Artifact { get; } = artifact;

    /// <summary>The registered memory repository — the real <c>MemoryRepository</c> over the bench's
    /// ephemeral AGE when the running probe declared <c>RequiresMemory</c> and Docker was reachable,
    /// otherwise the no-op <see cref="StubMemoryRepository"/>. Only <c>RequiresMemory</c> probes
    /// should touch it.</summary>
    public IMemoryRepository Memory { get; } = memory;

    /// <summary>Direct Cypher access to the bench's ephemeral AGE — non-null only when the running
    /// probe declared <c>RequiresMemory</c> and Docker was reachable. For probes that must SEED the
    /// graph outside the production write path (e.g. import seeding at artifact timestamps, where
    /// <see cref="IMemoryRepository.CaptureMomentAsync"/>'s LLM extraction and now-stamping don't
    /// apply). Reads should still go through <see cref="Memory"/> so the probe exercises the real
    /// hot path.</summary>
    public IDbContextFactory<AppDbContext>? MemoryDb { get; } = memoryDb;

    /// <summary>Fires at the probe deadline — pass into every async call so a timed-out probe
    /// actually stops instead of racing the artifact write.</summary>
    public CancellationToken Cancellation { get; } = cancellation;

    /// <summary>The router that routes generation jobs to the configured endpoint grains.</summary>
    public ILlmRouterGrain Router => Grains.GetGrain<ILlmRouterGrain>(0);

    /// <summary>Record a timestamped observation into the artifact.</summary>
    public void Observe(string label, object? data) => Artifact.Observe(label, data);

    public ILogger Logger(string category) => LoggerFactory.CreateLogger(category);
}
