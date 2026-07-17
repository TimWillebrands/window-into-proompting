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
