using System.Collections.Concurrent;

namespace PartyTown.Services.Memory;

public sealed class ConsolidationService(
    IMemoryRepository repository,
    IStanceConsolidator consolidator,
    ILogger<ConsolidationService> logger)
{
    public const double GaugeThreshold = 3.0;

    private readonly ConcurrentDictionary<(Guid PartyId, Guid PersonaId), byte> inFlight = new();

    public Task<ConsolidationRunResult> RunAsync(
        Guid partyId,
        ConsolidationSubject subject,
        IReadOnlyList<ConsolidationRosterEntry> roster,
        CancellationToken ct)
        => RunAsync(partyId, subject, roster, null, ct);

    private async Task<ConsolidationRunResult> RunAsync(
        Guid partyId,
        ConsolidationSubject subject,
        IReadOnlyList<ConsolidationRosterEntry> roster,
        ConsolidationBatch? prefetched,
        CancellationToken ct)
    {
        var runId = Guid.NewGuid();
        var key = (partyId, subject.PersonaId);
        if (!inFlight.TryAdd(key, 0))
        {
            return new ConsolidationRunResult(runId, subject.PersonaId, 0, 0, null, Skipped: true);
        }

        try
        {
            var batch = prefetched ?? await repository.GetUnconsolidatedRecollectionsAsync(partyId, subject.PersonaId, ct);
            if (batch.Items.Count == 0)
            {
                return new ConsolidationRunResult(runId, subject.PersonaId, 0, 0, batch.Watermark);
            }

            var currentStances = (await repository.ListStancesAsync(partyId, subject.PersonaId, ct))
                .Where(s => s.IsCurrent && s.Origin != StanceOrigin.Retract)
                .ToList();

            var others = roster.Where(r => r.PersonaId != subject.PersonaId).ToList();
            var proposals = await consolidator.ProposeStancesAsync(
                subject, others, batch.Items, currentStances, ct);

            var appended = 0;
            foreach (var proposal in proposals)
            {
                var target = new StanceTargetSpec(
                    proposal.Kind, proposal.TargetPersonaId, proposal.ConceptName, proposal.ConceptDisplay);
                await repository.AppendStanceAsync(
                    partyId, subject.PersonaId, target, proposal.Valence, proposal.Reasoning,
                    new StanceAttribution(StanceOrigin.Consolidation, RunId: runId), ct);
                appended++;
            }

            await repository.MarkConsolidatedAsync(partyId, subject.PersonaId, batch, ct);
            var watermark = batch.Items[^1].Ts;

            logger.LogInformation(
                "Consolidation run {RunId} for persona {PersonaId} in party {PartyId}: walked {Walked} recollections, appended {Appended} stances, watermark → {Watermark:o}",
                runId, subject.PersonaId, partyId, batch.Items.Count, appended, watermark);

            return new ConsolidationRunResult(
                runId, subject.PersonaId, batch.Items.Count, appended, watermark);
        }
        finally
        {
            inFlight.TryRemove(key, out _);
        }
    }

    public async Task<IReadOnlyList<ConsolidationRunResult>> RunWhereGaugeExceededAsync(
        Guid partyId,
        IReadOnlyList<ConsolidationSubject> subjects,
        IReadOnlyList<ConsolidationRosterEntry> roster,
        CancellationToken ct)
    {
        var results = new List<ConsolidationRunResult>();
        foreach (var subject in subjects)
        {
            var batch = await repository.GetUnconsolidatedRecollectionsAsync(partyId, subject.PersonaId, ct);
            var pressure = batch.Items.Sum(i => i.Weight);
            if (pressure < GaugeThreshold)
            {
                continue;
            }

            logger.LogInformation(
                "Consolidation gauge tripped for persona {PersonaId} in party {PartyId}: Σ weight {Pressure:0.0#} ≥ {Threshold}",
                subject.PersonaId, partyId, pressure, GaugeThreshold);
            try
            {
                results.Add(await RunAsync(partyId, subject, roster, batch, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Consolidation gauge run failed for persona {PersonaId} in party {PartyId}",
                    subject.PersonaId, partyId);
            }
        }
        return results;
    }

    public void TriggerGaugedRuns(
        Guid partyId,
        IReadOnlyList<ConsolidationSubject> subjects,
        IReadOnlyList<ConsolidationRosterEntry> roster)
    {
        if (subjects.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await RunWhereGaugeExceededAsync(partyId, subjects, roster, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Gauged consolidation pass failed for party {PartyId}", partyId);
            }
        });
    }
}
