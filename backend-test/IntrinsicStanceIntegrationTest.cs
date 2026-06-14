using BackendTest.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace BackendTest;

/// <summary>
/// Integration tests for Intrinsic Stance (ADR 0016, issue #91): Persona-scope STANCE edges,
/// union read across Acquired + Intrinsic, Promotion with Participant→Persona re-targeting.
/// Requires the dev Postgres+AGE instance (Aspire AppHost).
/// </summary>
public sealed class IntrinsicStanceIntegrationTest(MemoryGraphFixture fixture)
    : IClassFixture<MemoryGraphFixture>
{
    private MemoryRepository MakeRepo()
        => new(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);

    private void RequireDevStack()
    {
        if (!fixture.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Dev Postgres+AGE unavailable: {fixture.UnavailableReason}. " +
                "Start the Aspire AppHost (`dotnet run --project aspire/Proompting.AppHost`) and rerun.");
        }
    }

    // ─── Authoring and listing ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AppendIntrinsic_AppearsInListIntrinsicStances()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();
        var repo = MakeRepo();

        var id = await repo.AppendIntrinsicStanceAsync(
            personaA,
            new StanceTargetSpec(StanceTargetKind.Participant, personaB, null, null),
            valence: 0.7, reasoning: "Vlad impresses you", attribution: null,
            CancellationToken.None);

        var stances = await repo.ListIntrinsicStancesAsync(personaA, CancellationToken.None);

        var record = Assert.Single(stances);
        Assert.Equal(id, record.Id);
        Assert.Equal(0.7, record.Valence, precision: 4);
        Assert.Equal("Vlad impresses you", record.Reasoning);
        Assert.Equal(StanceTargetKind.Participant, record.TargetKind);
        Assert.Equal(personaB, record.TargetPersonaId);
        Assert.True(record.IsCurrent);
        Assert.True(record.IsIntrinsic);
    }

    [Fact]
    public async Task AppendIntrinsicSelf_TargetKindIsSelf()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var repo = MakeRepo();

        await repo.AppendIntrinsicStanceAsync(
            personaA,
            new StanceTargetSpec(StanceTargetKind.Self, null, null, null),
            valence: 0.3, reasoning: "you doubt yourself sometimes", attribution: null,
            CancellationToken.None);

        var stances = await repo.ListIntrinsicStancesAsync(personaA, CancellationToken.None);

        var record = Assert.Single(stances);
        Assert.Equal(StanceTargetKind.Self, record.TargetKind);
        Assert.Equal(personaA, record.TargetPersonaId);
        Assert.True(record.IsIntrinsic);
    }

    // ─── Union read: Intrinsic renders in any Party ────────────────────────────────────────

    [Fact]
    public async Task IntrinsicStance_RendersInAnyPartyWhenTargetPresent()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();
        var partyId1 = Guid.NewGuid();
        var partyId2 = Guid.NewGuid();
        var repo = MakeRepo();

        await repo.AppendIntrinsicStanceAsync(
            personaA,
            new StanceTargetSpec(StanceTargetKind.Participant, personaB, null, null),
            valence: 0.8, reasoning: "you trust Vlad's instincts", attribution: null,
            CancellationToken.None);

        // Party 1: both present
        var lines1 = await repo.RecallStancesAsync(
            partyId1, personaA, new[] { personaA, personaB }, anchorText: "", limit: 10,
            CancellationToken.None);

        // Party 2: both present
        var lines2 = await repo.RecallStancesAsync(
            partyId2, personaA, new[] { personaA, personaB }, anchorText: "", limit: 10,
            CancellationToken.None);

        Assert.Single(lines1);
        Assert.Equal("you trust Vlad's instincts", lines1[0].Reasoning);
        Assert.Single(lines2);
        Assert.Equal("you trust Vlad's instincts", lines2[0].Reasoning);
    }

    [Fact]
    public async Task IntrinsicStance_RendersNothingWhenTargetAbsent()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var repo = MakeRepo();

        await repo.AppendIntrinsicStanceAsync(
            personaA,
            new StanceTargetSpec(StanceTargetKind.Participant, personaB, null, null),
            valence: 0.8, reasoning: "you trust Vlad's instincts", attribution: null,
            CancellationToken.None);

        // personaB is NOT in presentPersonaIds
        var lines = await repo.RecallStancesAsync(
            partyId, personaA, new[] { personaA }, anchorText: "", limit: 10,
            CancellationToken.None);

        Assert.Empty(lines);
    }

    // ─── Union read: Acquired overrides Intrinsic in the same Party ───────────────────────

    [Fact]
    public async Task AcquiredStance_OverridesIntrinsicInSameParty_NotInOther()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();
        var partyId1 = Guid.NewGuid();
        var partyId2 = Guid.NewGuid();
        var repo = MakeRepo();

        // Intrinsic first (older).
        await repo.AppendIntrinsicStanceAsync(
            personaA,
            new StanceTargetSpec(StanceTargetKind.Participant, personaB, null, null),
            valence: 0.3, reasoning: "old intrinsic baseline", attribution: null,
            CancellationToken.None);

        // Acquired in Party 1 (newer — inserted after).
        await repo.AppendStanceAsync(
            partyId1, personaA,
            new StanceTargetSpec(StanceTargetKind.Participant, personaB, null, null),
            valence: 0.9, reasoning: "newer party-1 acquired", attribution: null,
            CancellationToken.None);

        var present = new[] { personaA, personaB };

        // Party 1: the newer Acquired wins.
        var lines1 = await repo.RecallStancesAsync(
            partyId1, personaA, present, anchorText: "", limit: 10,
            CancellationToken.None);

        // Party 2: only Intrinsic (no Acquired written there).
        var lines2 = await repo.RecallStancesAsync(
            partyId2, personaA, present, anchorText: "", limit: 10,
            CancellationToken.None);

        Assert.Single(lines1);
        Assert.Equal("newer party-1 acquired", lines1[0].Reasoning);
        Assert.Single(lines2);
        Assert.Equal("old intrinsic baseline", lines2[0].Reasoning);
    }

    // ─── Promotion ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Promote_ParticipantTarget_ReTargetsPersona_ResolvesInOtherParty()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();
        var partyId1 = Guid.NewGuid();
        var partyId2 = Guid.NewGuid();
        var partyId3 = Guid.NewGuid();
        var repo = MakeRepo();

        // Acquire stance in Party 1: personaA toward personaB (Participant).
        var acquiredId = await repo.AppendStanceAsync(
            partyId1, personaA,
            new StanceTargetSpec(StanceTargetKind.Participant, personaB, null, null),
            valence: 0.6, reasoning: "promoted persona stance", attribution: null,
            CancellationToken.None);

        // Promote to Intrinsic scope.
        var intrinsicId = await repo.PromoteStanceAsync(partyId1, personaA, acquiredId, CancellationToken.None);
        Assert.NotNull(intrinsicId);

        // Intrinsic list now has one entry targeting personaB.
        var intrinsicList = await repo.ListIntrinsicStancesAsync(personaA, CancellationToken.None);
        var intrinsicRecord = Assert.Single(intrinsicList);
        Assert.Equal(intrinsicId.Value, intrinsicRecord.Id);
        Assert.Equal(personaB, intrinsicRecord.TargetPersonaId);
        Assert.Equal("promoted persona stance", intrinsicRecord.Reasoning);
        Assert.True(intrinsicRecord.IsIntrinsic);

        // Party 2: personaA and personaB present → intrinsic renders.
        var lines2 = await repo.RecallStancesAsync(
            partyId2, personaA, new[] { personaA, personaB }, anchorText: "", limit: 10,
            CancellationToken.None);
        Assert.Single(lines2);
        Assert.Equal("promoted persona stance", lines2[0].Reasoning);

        // Party 3: only personaA present, personaB absent → renders nothing.
        var lines3 = await repo.RecallStancesAsync(
            partyId3, personaA, new[] { personaA }, anchorText: "", limit: 10,
            CancellationToken.None);
        Assert.Empty(lines3);
    }

    [Fact]
    public async Task Promote_ConceptTarget_CarriesOverUnchanged()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var conceptName = $"lisp-{Guid.NewGuid():N}";
        var conceptDisplay = $"Lisp-{Guid.NewGuid():N}";
        var repo = MakeRepo();

        var acquiredId = await repo.AppendStanceAsync(
            partyId, personaA,
            new StanceTargetSpec(StanceTargetKind.Concept, null, conceptName, conceptDisplay),
            valence: 0.9, reasoning: "you love Lisp", attribution: null,
            CancellationToken.None);

        var intrinsicId = await repo.PromoteStanceAsync(partyId, personaA, acquiredId, CancellationToken.None);
        Assert.NotNull(intrinsicId);

        var intrinsicList = await repo.ListIntrinsicStancesAsync(personaA, CancellationToken.None);
        var record = Assert.Single(intrinsicList);
        Assert.Equal(StanceTargetKind.Concept, record.TargetKind);
        Assert.Equal(conceptName, record.TargetConceptName);
        Assert.True(record.IsIntrinsic);
    }

    [Fact]
    public async Task Promote_SelfTarget_CarriesOverUnchanged()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var repo = MakeRepo();

        var acquiredId = await repo.AppendStanceAsync(
            partyId, personaA,
            new StanceTargetSpec(StanceTargetKind.Self, null, null, null),
            valence: -0.2, reasoning: "you doubt yourself", attribution: null,
            CancellationToken.None);

        var intrinsicId = await repo.PromoteStanceAsync(partyId, personaA, acquiredId, CancellationToken.None);
        Assert.NotNull(intrinsicId);

        var intrinsicList = await repo.ListIntrinsicStancesAsync(personaA, CancellationToken.None);
        var record = Assert.Single(intrinsicList);
        Assert.Equal(StanceTargetKind.Self, record.TargetKind);
        Assert.Equal(personaA, record.TargetPersonaId);
        Assert.True(record.IsIntrinsic);
    }

    [Fact]
    public async Task Promote_LeavesOriginalAcquiredEdgeIntact()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var personaB = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var repo = MakeRepo();

        var acquiredId = await repo.AppendStanceAsync(
            partyId, personaA,
            new StanceTargetSpec(StanceTargetKind.Participant, personaB, null, null),
            valence: 0.5, reasoning: "original acquired", attribution: null,
            CancellationToken.None);

        await repo.PromoteStanceAsync(partyId, personaA, acquiredId, CancellationToken.None);

        // The Acquired Stance edge must still be present and unchanged.
        var acquiredList = await repo.ListStancesAsync(partyId, personaA, CancellationToken.None);
        var acquired = Assert.Single(acquiredList);
        Assert.Equal(acquiredId, acquired.Id);
        Assert.Equal("original acquired", acquired.Reasoning);
        Assert.False(acquired.IsIntrinsic);
    }

    [Fact]
    public async Task Promote_UnknownStanceId_ReturnsNull()
    {
        RequireDevStack();

        var personaA = Guid.NewGuid();
        var partyId = Guid.NewGuid();
        var repo = MakeRepo();

        var result = await repo.PromoteStanceAsync(partyId, personaA, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    // ─── NullExtractor (re-used from parent test class pattern) ───────────────────────────

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
            => Task.FromResult<IReadOnlyDictionary<Guid, RecollectionDraft>>(
                new Dictionary<Guid, RecollectionDraft>());
    }
}
