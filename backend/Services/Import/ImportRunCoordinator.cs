using System.Collections.Concurrent;
using Orleans.Streams;
using PartyTown.Grains;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Import;

/// <summary>One scene run in flight, as exposed to reconnecting clients.</summary>
[GenerateSerializer, Alias("PartyTown.Services.Import.ImportActiveRun")]
public sealed record ImportActiveRun
{
    [Id(0)] public Guid SceneId { get; init; }
    [Id(1)] public int CallsDone { get; init; }
    [Id(2)] public int TotalCalls { get; init; }
}

/// <summary>
/// Runs scene extraction maps in the background, detached from the HTTP request, so a
/// page refresh mid-run never aborts the LLM work. Publishes run lifecycle + pre-fold
/// item previews onto the import event stream for <c>PartyRealtimeHub</c> to fan out.
/// A second run request for a scene already in flight joins the existing run.
/// </summary>
public interface IImportRunCoordinator
{
    Task<SceneRunResult> RunAsync(Guid sessionId, Guid sceneId);
    IReadOnlyList<ImportActiveRun> GetActiveRuns(Guid sessionId);

    /// <summary>Cancels every run of a session; used when the session is abandoned.</summary>
    void CancelSession(Guid sessionId);
}

/// <inheritdoc cref="IImportRunCoordinator"/>
public sealed class ImportRunCoordinator(
    IGrainFactory grains,
    IClusterClient clusterClient,
    ISceneMapService sceneMap,
    ILogger<ImportRunCoordinator> logger) : IImportRunCoordinator
{
    private readonly ConcurrentDictionary<(Guid SessionId, Guid SceneId), Lazy<RunHandle>> runs = new();

    public Task<SceneRunResult> RunAsync(Guid sessionId, Guid sceneId)
        => runs.GetOrAdd((sessionId, sceneId), key => new Lazy<RunHandle>(() => Start(key.SessionId, key.SceneId))).Value.Task;

    public IReadOnlyList<ImportActiveRun> GetActiveRuns(Guid sessionId)
        => runs.Where(pair => pair.Key.SessionId == sessionId && pair.Value.IsValueCreated)
            .Select(pair => new ImportActiveRun
            {
                SceneId = pair.Key.SceneId,
                CallsDone = pair.Value.Value.CallsDone,
                TotalCalls = pair.Value.Value.TotalCalls,
            })
            .ToList();

    public void CancelSession(Guid sessionId)
    {
        foreach (var pair in runs.Where(p => p.Key.SessionId == sessionId && p.Value.IsValueCreated))
            pair.Value.Value.Cancellation.Cancel();
    }

    private RunHandle Start(Guid sessionId, Guid sceneId)
    {
        var handle = new RunHandle();
        handle.Task = Task.Run(() => ExecuteAsync(sessionId, sceneId, handle));
        // Refused/failed runs surface via the awaiting request when one is attached; a
        // client that refreshed away observes them over the stream instead.
        handle.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return handle;
    }

    private async Task<SceneRunResult> ExecuteAsync(Guid sessionId, Guid sceneId, RunHandle handle)
    {
        var ct = handle.Cancellation.Token;
        var stream = clusterClient
            .GetStreamProvider(PartyStreamIds.Provider)
            .GetStream<ImportStreamEvent>(PartyStreamIds.ImportEventsNamespace, PartyStreamIds.ImportEventId(sessionId));

        try
        {
            var grain = grains.GetGrain<IImportSessionGrain>(sessionId);
            var input = await grain.GetSceneRunInputAsync(sceneId);

            await stream.OnNextAsync(new ImportStreamEvent { Type = ImportStreamEvent.RunStarted, SceneId = sceneId });

            var map = await sceneMap.RunAsync(input, ct, progress =>
            {
                handle.CallsDone = progress.CallsDone;
                handle.TotalCalls = progress.TotalCalls;
                return stream.OnNextAsync(new ImportStreamEvent
                {
                    Type = ImportStreamEvent.RunProgress,
                    SceneId = sceneId,
                    CallsDone = progress.CallsDone,
                    TotalCalls = progress.TotalCalls,
                    Stage = progress.Stage,
                    Items = progress.NewItems.Select(i => new ImportRunItemPreview
                    {
                        Type = i.Type,
                        Persona = i.Persona,
                        Summary = i.Summary ?? string.Empty,
                        Weight = i.Weight,
                    }).ToList(),
                });
            });

            var result = await grain.ApplySceneRunAsync(sceneId, map);
            await stream.OnNextAsync(new ImportStreamEvent
            {
                Type = ImportStreamEvent.RunCompleted,
                SceneId = sceneId,
                CallsDone = handle.CallsDone,
                TotalCalls = handle.TotalCalls,
                Result = result,
            });
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Import session {SessionId}: scene {SceneId} run failed", sessionId, sceneId);
            await stream.OnNextAsync(new ImportStreamEvent
            {
                Type = ImportStreamEvent.RunFailed,
                SceneId = sceneId,
                Error = ex is OperationCanceledException ? "run cancelled" : ex.Message,
            });
            throw;
        }
        finally
        {
            runs.TryRemove((sessionId, sceneId), out _);
            handle.Cancellation.Dispose();
        }
    }

    private sealed class RunHandle
    {
        public Task<SceneRunResult> Task { get; set; } = null!;
        public CancellationTokenSource Cancellation { get; } = new();
        public volatile int CallsDone;
        public volatile int TotalCalls;
    }
}
