using BackendTest.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace BackendTest;

/// <summary>
/// Consolidation v1 (ADR 0016, #89) end-to-end against the dev Postgres+AGE stack:
/// recollections → run → auto-appended Stances with run attribution → watermark advance →
/// idempotent re-run, plus the gauge trigger and the retract round-trip. The LLM proposer is
/// stubbed by <see cref="FakeStanceConsolidator"/> so only orchestration + persistence are
/// under test.
/// </summary>
public sealed class ConsolidationIntegrationTest(MemoryGraphFixture fixture)
    : IClassFixture<MemoryGraphFixture>
{
    [Fact]
    public async Task Run_AppendsAttributedStances_AdvancesWatermark_RerunIsIdempotent()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var vladId = Guid.NewGuid();
        var subject = new ConsolidationSubject(hana.Id, "Hana", "Stubborn Lisp evangelist.");
        var roster = new[] { new ConsolidationRosterEntry(vladId, "Vlad") };

        var repo = MakeRepo();
        await CaptureForAsync(partyId, roomId, hana, "you watched Vlad mock your demo", 0.8);
        await CaptureForAsync(partyId, roomId, hana, "you heard Vlad apologise, badly", 0.6);

        // The fake crystallises one belief toward Vlad and one toward a concept.
        var consolidator = new FakeStanceConsolidator(_ => new[]
        {
            new StanceProposal(StanceTargetKind.Participant, vladId, null, null,
                -0.6, "Vlad tears things down before he understands them"),
            new StanceProposal(StanceTargetKind.Concept, null, "apologies", "apologies",
                -0.2, "apologies feel cheap to you lately"),
        });
        var service = MakeService(repo, consolidator);

        var run = await service.RunAsync(partyId, subject, roster, CancellationToken.None);

        Assert.False(run.Skipped);
        Assert.Equal(2, run.RecollectionsWalked);
        Assert.Equal(2, run.StancesAppended);
        Assert.NotNull(run.Watermark);
        var batch = Assert.Single(consolidator.SeenBatches);
        Assert.Equal(2, batch.Count);
        // Oldest-first: the walk feeds the proposer in capture order.
        Assert.Contains("mock", batch[0].Snippet);

        // Auto-append landed with full attribution: origin + the run id that walked them.
        var stances = await repo.ListStancesAsync(partyId, hana.Id, CancellationToken.None);
        Assert.Equal(2, stances.Count);
        Assert.All(stances, s =>
        {
            Assert.Equal(StanceOrigin.Consolidation, s.Origin);
            Assert.Equal(run.RunId, s.RunId);
            Assert.True(s.IsCurrent);
        });

        // The appended belief renders in `# Where you stand` when Vlad is present.
        var lines = await repo.RecallStancesAsync(
            partyId, hana.Id, new[] { hana.Id, vladId }, "anything", 5, CancellationToken.None);
        Assert.Contains(lines, l => l.Reasoning.Contains("tears things down"));

        // Idempotent over consumed Recollections: nothing new past the watermark, no LLM call.
        var rerun = await service.RunAsync(partyId, subject, roster, CancellationToken.None);
        Assert.Equal(0, rerun.RecollectionsWalked);
        Assert.Equal(0, rerun.StancesAppended);
        Assert.Equal(1, consolidator.Calls);

        // New experience after the watermark is the only thing the next run walks.
        await CaptureForAsync(partyId, roomId, hana, "you saw Vlad defend your demo to a stranger", 0.7);
        var third = await service.RunAsync(partyId, subject, roster, CancellationToken.None);
        Assert.Equal(1, third.RecollectionsWalked);
        Assert.Contains("defend", Assert.Single(consolidator.SeenBatches[^1]).Snippet);
    }

    [Fact]
    public async Task Run_ZeroProposals_StillAdvancesWatermark()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var subject = new ConsolidationSubject(hana.Id, "Hana", null);

        var repo = MakeRepo();
        await CaptureForAsync(partyId, roomId, hana, "you overheard small talk", 0.3);

        var consolidator = new FakeStanceConsolidator(_ => Array.Empty<StanceProposal>());
        var service = MakeService(repo, consolidator);

        var run = await service.RunAsync(
            partyId, subject, Array.Empty<ConsolidationRosterEntry>(), CancellationToken.None);
        Assert.Equal(1, run.RecollectionsWalked);
        Assert.Equal(0, run.StancesAppended);

        // Slept on, nothing crystallised — but the experience is consumed all the same.
        var rerun = await service.RunAsync(
            partyId, subject, Array.Empty<ConsolidationRosterEntry>(), CancellationToken.None);
        Assert.Equal(0, rerun.RecollectionsWalked);
        Assert.Equal(1, consolidator.Calls);
    }

    [Fact]
    public async Task Gauge_RunsOnlyPastWeightThreshold()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var subjects = new[] { new ConsolidationSubject(hana.Id, "Hana", null) };
        var roster = Array.Empty<ConsolidationRosterEntry>();

        var repo = MakeRepo();
        var consolidator = new FakeStanceConsolidator(_ => Array.Empty<StanceProposal>());
        var service = MakeService(repo, consolidator);

        // 4 × 0.7 = 2.8 < 3.0 — not enough unprocessed experience to sleep on.
        for (var i = 0; i < 4; i++)
        {
            await CaptureForAsync(partyId, roomId, hana, $"below-threshold-{i}", 0.7);
        }
        var below = await service.RunWhereGaugeExceededAsync(partyId, subjects, roster, CancellationToken.None);
        Assert.Empty(below);
        Assert.Equal(0, consolidator.Calls);

        // One more notable moment tips Σ weight to 3.5 ≥ 3.0: the run fires without a curator.
        await CaptureForAsync(partyId, roomId, hana, "the-tipping-moment", 0.7);
        var tripped = await service.RunWhereGaugeExceededAsync(partyId, subjects, roster, CancellationToken.None);
        var run = Assert.Single(tripped);
        Assert.Equal(5, run.RecollectionsWalked);
        Assert.Equal(1, consolidator.Calls);

        // Consumed: the gauge reads zero again until new experience accumulates.
        var again = await service.RunWhereGaugeExceededAsync(partyId, subjects, roster, CancellationToken.None);
        Assert.Empty(again);
    }

    [Fact]
    public async Task Retract_AppendsNeutralizingEdge_MasksPromptLine_PreservesHistory()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var author = Guid.NewGuid();
        var vlad = Guid.NewGuid();
        var present = new[] { author, vlad };

        var repo = MakeRepo();
        var junkId = await repo.AppendStanceAsync(
            partyId, author, new StanceTargetSpec(StanceTargetKind.Participant, vlad, null, null),
            valence: -0.9, reasoning: "Vlad is a fraud", attribution: null, CancellationToken.None);

        // Sanity: the junk belief renders before the retract.
        var before = await repo.RecallStancesAsync(partyId, author, present, "n/a", 5, CancellationToken.None);
        Assert.Contains(before, l => l.Reasoning.Contains("fraud"));

        var retractId = await repo.RetractStanceAsync(partyId, author, junkId, CancellationToken.None);
        Assert.NotNull(retractId);

        // Latest-wins reflects the retract immediately: the target renders nothing.
        var after = await repo.RecallStancesAsync(partyId, author, present, "n/a", 5, CancellationToken.None);
        Assert.Empty(after);

        // History preserved, fully attributed: the retract edge is current and points at the
        // junk edge; the junk edge survives unflagged.
        var all = await repo.ListStancesAsync(partyId, author, CancellationToken.None);
        Assert.Equal(2, all.Count);
        var retract = Assert.Single(all, s => s.Id == retractId);
        Assert.Equal(StanceOrigin.Retract, retract.Origin);
        Assert.Equal(junkId, retract.RetractsId);
        Assert.True(retract.IsCurrent);
        Assert.Contains("Retracted:", retract.Reasoning);
        var junk = Assert.Single(all, s => s.Id == junkId);
        Assert.False(junk.IsCurrent);

        // Unknown edge / wrong author: a silent null, nothing written.
        Assert.Null(await repo.RetractStanceAsync(partyId, author, Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await repo.RetractStanceAsync(partyId, Guid.NewGuid(), junkId, CancellationToken.None));
    }

    [Fact]
    public async Task Retract_ConsolidationStance_CorrectionMasksIt()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var author = Guid.NewGuid();
        var conceptName = $"zorblax{Guid.NewGuid():N}";

        var repo = MakeRepo();
        var runId = Guid.NewGuid();
        var stanceId = await repo.AppendStanceAsync(
            partyId, author, new StanceTargetSpec(StanceTargetKind.Concept, null, conceptName, conceptName),
            valence: 0.8, reasoning: "junk belief from a bad model day",
            new StanceAttribution(StanceOrigin.Consolidation, RunId: runId), CancellationToken.None);

        var listed = Assert.Single(await repo.ListStancesAsync(partyId, author, CancellationToken.None));
        Assert.Equal(StanceOrigin.Consolidation, listed.Origin);
        Assert.Equal(runId, listed.RunId);

        var retractId = await repo.RetractStanceAsync(partyId, author, stanceId, CancellationToken.None);
        Assert.NotNull(retractId);

        var lines = await repo.RecallStancesAsync(
            partyId, author, new[] { author }, $"talking about {conceptName} now", 5, CancellationToken.None);
        Assert.Empty(lines);
    }

    // ─── plumbing ──────────────────────────────────────────────────────────────────────────

    private void RequireDevStack()
    {
        if (!fixture.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Dev Postgres+AGE unavailable: {fixture.UnavailableReason}. " +
                "Start the Aspire AppHost (`dotnet run --project aspire/Proompting.AppHost`) and rerun.");
        }
    }

    private MemoryRepository MakeRepo()
        => new(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);

    private static ConsolidationService MakeService(MemoryRepository repo, IStanceConsolidator consolidator)
        => new(repo, consolidator, NullLogger<ConsolidationService>.Instance);

    /// <summary>
    /// Capture one Event whose sole Recollection lands on <paramref name="participant"/> —
    /// the minimal way to grow an unconsolidated pile through the real write path.
    /// </summary>
    private async Task CaptureForAsync(
        Guid partyId, Guid roomId, ParticipantView participant,
        string snippet, double weight)
    {
        var messageId = unchecked((int)((DateTimeOffset.UtcNow.Ticks + Environment.TickCount64) & 0x7FFFFFFF));
        var source = new ChatMessage
        {
            MessageId = messageId,
            Content = snippet,
            SenderId = participant.Id,
            SenderType = "assistant",
            ChatGroupId = roomId,
        };
        var fake = new FakeExtractor(
            new EventExtraction($"Moment for {snippet}.", new List<ConceptTag>(), new List<Guid>()),
            new Dictionary<string, RecollectionDraft> { [participant.Name] = new(snippet, weight) });
        var capturingRepo = new MemoryRepository(fixture.Factory, fake, NullLogger<MemoryRepository>.Instance);
        var result = await capturingRepo.CaptureMomentAsync(
            partyId, roomId, messageId,
            presentParticipants: new[] { participant },
            recentContext: new[] { source },
            ct: CancellationToken.None);
        Assert.True(result.EventCreated);
        Assert.Equal(new[] { participant.Id }, result.RecollectionPersonaIds);
    }

    /// <summary>Deterministic proposer: records what each run walked, proposes via the factory.</summary>
    private sealed class FakeStanceConsolidator(
        Func<IReadOnlyList<UnconsolidatedRecollection>, IReadOnlyList<StanceProposal>> propose)
        : IStanceConsolidator
    {
        public int Calls { get; private set; }
        public List<IReadOnlyList<UnconsolidatedRecollection>> SeenBatches { get; } = new();

        public Task<IReadOnlyList<StanceProposal>> ProposeStancesAsync(
            ConsolidationSubject subject,
            IReadOnlyList<ConsolidationRosterEntry> roster,
            IReadOnlyList<UnconsolidatedRecollection> recollections,
            IReadOnlyList<StanceRecord> currentStances,
            CancellationToken cancellationToken)
        {
            Calls++;
            SeenBatches.Add(recollections);
            return Task.FromResult(propose(recollections));
        }
    }

    private sealed class FakeExtractor(
        EventExtraction? extraction,
        IReadOnlyDictionary<string, RecollectionDraft> recollectionByPersona) : IMemoryExtractor
    {
        public Task<EventExtraction?> ExtractEventAsync(
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            IReadOnlyList<ParticipantView> presentParticipants,
            IReadOnlyList<string> existingConcepts,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken) => Task.FromResult(extraction);

        public Task<IReadOnlyDictionary<Guid, RecollectionDraft>> ExtractRecollectionsAsync(
            IReadOnlyList<RecollectionTarget> targets,
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken)
        {
            var result = new Dictionary<Guid, RecollectionDraft>();
            foreach (var t in targets)
            {
                if (recollectionByPersona.TryGetValue(t.Name, out var d))
                {
                    result[t.PersonaId] = d;
                }
            }
            return Task.FromResult<IReadOnlyDictionary<Guid, RecollectionDraft>>(result);
        }
    }

    private sealed class NullExtractor : IMemoryExtractor
    {
        public static readonly NullExtractor Instance = new();

        public Task<EventExtraction?> ExtractEventAsync(
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            IReadOnlyList<ParticipantView> presentParticipants,
            IReadOnlyList<string> existingConcepts,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken) => Task.FromResult<EventExtraction?>(null);

        public Task<IReadOnlyDictionary<Guid, RecollectionDraft>> ExtractRecollectionsAsync(
            IReadOnlyList<RecollectionTarget> targets,
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, RecollectionDraft>>(new Dictionary<Guid, RecollectionDraft>());
    }
}
