using System.Collections.Concurrent;

namespace PartyTown.Services.Memory;

/// <summary>
/// Consolidation v1 orchestrator (ADR 0016): per-Participant, one LLM call, auto-append.
/// Walks the Recollections past the watermark, asks <see cref="IStanceConsolidator"/> what
/// crystallised, appends the proposals as STANCE edges attributed to a run id, and advances
/// the watermark to the last Recollection walked. Runs in rest — triggered by the curator
/// button or the gauge — never on the response path.
/// </summary>
/// <remarks>
/// Callers hand in the subject + roster (built from the Party's cast); the memory layer
/// stays grain-free and the whole loop is integration-testable against the AGE fixture
/// with a fake consolidator.
/// </remarks>
public sealed class ConsolidationService(
    IMemoryRepository repository,
    IStanceConsolidator consolidator,
    ILogger<ConsolidationService> logger)
{
    /// <summary>
    /// Gauge threshold (ADR 0016): Σ weight of unconsolidated Recollections at which a
    /// Participant has "enough unprocessed experience to sleep on". Roughly five notable
    /// (≈0.6) moments.
    /// </summary>
    public const double GaugeThreshold = 3.0;

    // One run per Participant at a time: the curator button racing the gauge (or itself)
    // would double-append every proposal. Losers bow out with Skipped instead of queueing —
    // the in-flight run is already consuming the same batch.
    private readonly ConcurrentDictionary<(Guid PartyId, Guid PersonaId), byte> inFlight = new();

    public async Task<ConsolidationRunResult> RunAsync(
        Guid partyId,
        ConsolidationSubject subject,
        IReadOnlyList<ConsolidationRosterEntry> roster,
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
            var batch = await repository.GetUnconsolidatedRecollectionsAsync(partyId, subject.PersonaId, ct);
            if (batch.Items.Count == 0)
            {
                // Idempotency over consumed Recollections: nothing past the watermark means
                // no LLM call and no writes.
                return new ConsolidationRunResult(runId, subject.PersonaId, 0, 0, batch.Watermark);
            }

            var currentStances = (await repository.ListStancesAsync(partyId, subject.PersonaId, ct))
                .Where(s => s.IsCurrent && s.Origin != StanceOrigin.Retract)
                .ToList();

            // Callers may pass the whole cast as the roster; the subject themselves is not
            // an "other" (self is its own target kind).
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

            // Advance to the last Recollection walked — not "now" — so anything captured
            // while the run was in flight stays unconsolidated for the next run. Advancing
            // on zero proposals is deliberate: the batch was slept on either way.
            var watermark = batch.Items[^1].Ts;
            await repository.AdvanceConsolidationWatermarkAsync(partyId, subject.PersonaId, watermark, ct);

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

    /// <summary>
    /// Gauge pass over a set of Participants: run Consolidation for each whose Σ weight of
    /// unconsolidated Recollections has reached <see cref="GaugeThreshold"/>. Awaitable for
    /// tests; production capture fires it via <see cref="TriggerGaugedRuns"/>.
    /// </summary>
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
            results.Add(await RunAsync(partyId, subject, roster, ct));
        }
        return results;
    }

    /// <summary>
    /// Fire-and-forget gauge pass for the capture path: the remember request returns
    /// immediately and any tripped runs happen in rest. Detached from the request's
    /// cancellation on purpose — a finished capture's consolidation shouldn't die with
    /// the HTTP connection.
    /// </summary>
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
