using BackendTest.Infrastructure;
using Orleans.Runtime;
using PartyTown.Grains;
using PartyTown.Services.Import;

namespace BackendTest;

/// <summary>
/// ImportSessionGrain lifecycle over a real TestCluster: session creation, scene CRUD
/// validation, fold-through-grain, and — the acceptance criterion — draft survival
/// across grain deactivation (persistence round-trip through the "imports" store).
/// </summary>
public class ImportSessionGrainTest(PartyClusterFixture fixture) : IClassFixture<PartyClusterFixture>
{
    private static ImportSource TestSource()
    {
        var chunks = new List<ImportChunk>
        {
            new() { Index = 0, Role = "user", Category = ImportChunkCategories.Recap, Text = "# History\nLena moved to Rotterdam." },
        };
        for (var i = 1; i <= 6; i++)
            chunks.Add(new ImportChunk { Index = i, Role = i % 2 == 0 ? "user" : "model", Category = ImportChunkCategories.Message, Text = $"message {i}" });
        return new ImportSource
        {
            FileName = "test.json",
            SystemInstruction = "You are Lena.",
            Chunks = chunks,
        };
    }

    private static SceneMapResult TestMap() => new()
    {
        Items = new List<MappedItem>
        {
            new()
            {
                SourceId = "w0",
                Type = DraftItemTypes.Episode,
                Summary = "Lena told Denise the clinic was closing for good.",
                Weight = 0.8,
                Participants = new List<string> { "Lena", "Denise" },
                SourceChunks = new List<int> { 2 },
            },
        },
        LlmCalls = 1,
    };

    [Fact]
    public async Task Draft_survives_grain_deactivation()
    {
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        await grain.InitializeAsync(TestSource());
        var scene = await grain.CreateSceneAsync(new SceneDefinition { FromChunk = 1, ToChunk = 6 });
        var run = await grain.ApplySceneRunAsync(scene.Id, TestMap());
        Assert.Single(run.Items);

        // Force the activation out of memory; the next call reactivates from storage.
        var mgmt = fixture.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);

        var overview = await grain.GetOverviewAsync();
        Assert.Equal(7, overview.ChunkCount);
        Assert.Equal(1, overview.DraftItemCount);
        var scenePersisted = Assert.Single(overview.Scenes);
        Assert.Equal(scene.Id, scenePersisted.Id);
        Assert.Equal(1, scenePersisted.RunCount);

        var draft = await grain.GetDraftAsync();
        Assert.Equal("Lena told Denise the clinic was closing for good.", Assert.Single(draft.Items).Summary);

        var ledger = await grain.GetLedgerAsync();
        Assert.True(ledger.Reconciles);
        Assert.Equal(7, ledger.TotalChunks);
    }

    [Fact]
    public async Task Uninitialized_session_reports_not_initialized_and_rejects_reads()
    {
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        Assert.False(await grain.IsInitializedAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GetOverviewAsync());
    }

    [Fact]
    public async Task Scene_range_validation_rejects_out_of_bounds_definitions()
    {
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        await grain.InitializeAsync(TestSource());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            grain.CreateSceneAsync(new SceneDefinition { FromChunk = 0, ToChunk = 7 }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            grain.CreateSceneAsync(new SceneDefinition { FromChunk = 5, ToChunk = 2 }));
    }

    [Fact]
    public async Task Deleting_a_scene_removes_its_draft_slice_and_run_record()
    {
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        await grain.InitializeAsync(TestSource());
        var scene = await grain.CreateSceneAsync(new SceneDefinition { FromChunk = 1, ToChunk = 6 });
        await grain.ApplySceneRunAsync(scene.Id, TestMap());

        await grain.DeleteSceneAsync(scene.Id);

        var draft = await grain.GetDraftAsync();
        Assert.Empty(draft.Items);
        var ledger = await grain.GetLedgerAsync();
        Assert.True(ledger.Reconciles);
        Assert.All(ledger.Chunks, c => Assert.Equal(ChunkDispositions.Unprocessed, c.Disposition));
    }

    [Fact]
    public async Task Commit_state_settles_the_scene_and_survives_deactivation()
    {
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        await grain.InitializeAsync(TestSource());
        var scene = await grain.CreateSceneAsync(new SceneDefinition { FromChunk = 1, ToChunk = 6 });

        // Nothing to commit before a run.
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GetSceneCommitInputAsync(scene.Id));

        await grain.ApplySceneRunAsync(scene.Id, TestMap());
        var input = await grain.GetSceneCommitInputAsync(scene.Id);
        Assert.Single(input.Items);
        Assert.Equal(6, input.Chunks.Count);
        Assert.Null(input.Target);

        var target = new ImportCommitTarget
        {
            PartyId = Guid.NewGuid(),
            RoomId = Guid.NewGuid(),
            RoomName = "Imported chat",
            UserParticipantId = Guid.NewGuid(),
        };
        await grain.SetCommitTargetAsync(target);
        await grain.RecordCommittedPersonasAsync(new Dictionary<string, Guid> { ["Lena"] = Guid.NewGuid() });
        await grain.RecordSceneMessagesWrittenAsync(scene.Id);
        await grain.CompleteSceneCommitAsync(scene.Id);

        // Settled: rerun, edits and deletion are refused; a second commit input too.
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GetSceneRunInputAsync(scene.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ApplySceneRunAsync(scene.Id, TestMap()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.DeleteSceneAsync(scene.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.GetSceneCommitInputAsync(scene.Id));
        var item = (await grain.GetDraftAsync()).Items.Single();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            grain.UpdateDraftItemAsync(item.Id, new DraftItemEdit { Routing = "history" }));

        // A different Room cannot be pinned onto the same session.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            grain.SetCommitTargetAsync(target with { RoomId = Guid.NewGuid() }));

        // Ledger still reconciles with committed state in place.
        Assert.True((await grain.GetLedgerAsync()).Reconciles);

        // Commit state survives the activation being dropped.
        var mgmt = fixture.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);

        var overview = await grain.GetOverviewAsync();
        Assert.Equal(target.RoomId, overview.CommitTarget?.RoomId);
        var persisted = Assert.Single(overview.Scenes);
        Assert.True(persisted.Committed);
        Assert.True(persisted.MessagesWritten);
        Assert.NotNull(persisted.CommittedAt);

        // Rollback bookkeeping: everything returns to editable draft.
        await grain.ResetCommitsAsync();
        Assert.Null(await grain.GetCommitTargetAsync());
        var reset = Assert.Single((await grain.GetOverviewAsync()).Scenes);
        Assert.False(reset.Committed);
        Assert.False(reset.MessagesWritten);
        await grain.ApplySceneRunAsync(scene.Id, TestMap()); // rerun allowed again
    }

    [Fact]
    public async Task Scene_run_proposes_library_matches_and_decisions_are_recorded()
    {
        // A library persona the imported cast member is a near-typo of.
        var libraryId = Guid.NewGuid();
        var personaRoot = fixture.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
        await personaRoot.AddPersona(libraryId, "Denise", "You are Denise.", "Steady");

        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        await grain.InitializeAsync(TestSource());
        var scene = await grain.CreateSceneAsync(new SceneDefinition { FromChunk = 1, ToChunk = 6 });
        await grain.ApplySceneRunAsync(scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "s0",
                    Type = DraftItemTypes.Trait,
                    Persona = "Denyse",
                    Summary = "Steady under fire.",
                },
            },
            LlmCalls = 1,
        });

        // The trait owner was proposed into the registry and matched against the library.
        var registry = await grain.GetRegistryAsync();
        var entry = Assert.Single(registry.Cast, c => c.Name == "Denyse");
        Assert.False(entry.Confirmed);
        Assert.Equal(CastMatchStates.Proposed, entry.MatchState);
        Assert.Equal(libraryId, entry.ProposedPersonaId);
        Assert.Equal("Denise", entry.ProposedPersonaName);

        // Deciding executes later at commit; here it just records (and confirms).
        var decided = await grain.DecideCastMatchAsync("Denyse", new CastMatchDecision { Decision = "match" });
        Assert.Equal(CastMatchStates.ConfirmedMatch, decided.MatchState);
        Assert.Equal(libraryId, decided.MatchedPersonaId);
        Assert.True(decided.Confirmed);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            grain.DecideCastMatchAsync("Denyse", new CastMatchDecision { Decision = "maybe" }));
    }

    [Fact]
    public async Task Registry_upsert_absorbs_aliased_proposals_and_feeds_scene_runs()
    {
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        await grain.InitializeAsync(TestSource());
        var scene = await grain.CreateSceneAsync(new SceneDefinition { FromChunk = 1, ToChunk = 6 });
        await grain.ApplySceneRunAsync(scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "w0",
                    Type = DraftItemTypes.Episode,
                    Summary = "Denyse warned the village about the flood.",
                    Weight = 0.8,
                    Participants = new List<string> { "Denyse" },
                    SourceChunks = new List<int> { 2 },
                },
            },
            LlmCalls = 1,
        });

        // The unresolved participant landed as an unconfirmed person-as-concept proposal.
        Assert.Single((await grain.GetRegistryAsync()).Cast, c => c.Name == "Denyse" && !c.Confirmed);

        // The human pins "Denyse" as an alias of a real cast entry — the stray proposal
        // is absorbed, and confirmed registry entries flow into the scene-run input.
        var entry = await grain.UpsertCastEntryAsync("Denise", new RegistryCastEdit
        {
            Aliases = new List<string> { "Denyse" },
        });
        Assert.True(entry.Confirmed);

        var registry = await grain.GetRegistryAsync();
        Assert.DoesNotContain(registry.Cast, c => c.Name == "Denyse");
        var runInput = await grain.GetSceneRunInputAsync(scene.Id);
        var castHint = Assert.Single(runInput.Cast);
        Assert.Equal("Denise", castHint.Name);
        Assert.Equal(new List<string> { "Denyse" }, castHint.Aliases);
    }

    [Fact]
    public async Task Finalized_cards_respect_human_edits_and_commit_snapshots()
    {
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(Guid.NewGuid());
        await grain.InitializeAsync(TestSource());

        var stored = await grain.StoreFinalizedCardsAsync(new List<PersonaCardDraft>
        {
            new() { Persona = "Lena", SystemPrompt = "You are Lena.\n\n- Sardonic.", Bio = "A sardonic clinician", TraitFingerprint = "f1" },
        });
        Assert.Equal("A sardonic clinician", Assert.Single(stored).Bio);

        // Human edit wins: a later finalize may only propose.
        await grain.UpdateCardAsync("Lena", new PersonaCardEdit { Bio = "The clinic's last optimist" });
        var reFinalized = Assert.Single(await grain.StoreFinalizedCardsAsync(new List<PersonaCardDraft>
        {
            new() { Persona = "Lena", SystemPrompt = "You are Lena.\n\n- Rewritten.", Bio = "Machine bio", TraitFingerprint = "f2" },
        }));
        Assert.Equal("The clinic's last optimist", reFinalized.Bio);
        Assert.Equal("Machine bio", reFinalized.ProposedBio);
        Assert.True(reFinalized.HumanEdited);

        // Accepting the proposal promotes it and reopens machine ownership.
        var accepted = await grain.UpdateCardAsync("Lena", new PersonaCardEdit { AcceptProposal = true });
        Assert.Equal("Machine bio", accepted.Bio);
        Assert.Null(accepted.ProposedBio);
        Assert.False(accepted.HumanEdited);

        // Commit snapshot for the human-drift guard.
        await grain.MarkCardsCommittedAsync(new List<string> { "Lena" });
        var draft = await grain.GetDraftAsync();
        var card = Assert.Single(draft.Cards);
        Assert.Equal(card.SystemPrompt, card.CommittedSystemPrompt);
        Assert.Equal("Machine bio", card.CommittedBio);
    }

    [Fact]
    public async Task Delete_clears_state_and_a_fresh_activation_is_uninitialized()
    {
        var id = Guid.NewGuid();
        var grain = fixture.GrainFactory.GetGrain<IImportSessionGrain>(id);
        await grain.InitializeAsync(TestSource());
        await grain.DeleteAsync();

        var again = fixture.GrainFactory.GetGrain<IImportSessionGrain>(id);
        Assert.False(await again.IsInitializedAsync());
    }
}
