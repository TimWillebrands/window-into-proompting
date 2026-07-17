using PartyTown.Services.Import;

namespace BackendTest;

/// <summary>
/// The deterministic fold (ADR 0017): dedup, persona-key folding, concept merging,
/// weight-floor routing, rerun-replace semantics and the conservation ledger. All pure
/// state-in → state-out, so everything here is assertable without a cluster or bench.
/// </summary>
public class ImportFoldTest
{
    private static readonly DateTimeOffset Anchor = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RanAt = new(2026, 7, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>10 chunks: 0 thought, 1 media, 2 recap, 3-8 message, 9 empty.</summary>
    private static ImportSessionState NewState()
    {
        var chunks = new List<ImportChunk>
        {
            new() { Index = 0, Role = "model", Category = ImportChunkCategories.Thought, Text = "thinking" },
            new() { Index = 1, Role = "user", Category = ImportChunkCategories.Media, Text = "" },
            new() { Index = 2, Role = "user", Category = ImportChunkCategories.Recap, Text = "# History\nstuff" },
        };
        for (var i = 3; i <= 8; i++)
            chunks.Add(new ImportChunk { Index = i, Role = i % 2 == 0 ? "user" : "model", Category = ImportChunkCategories.Message, Text = $"msg {i}" });
        chunks.Add(new ImportChunk { Index = 9, Role = "user", Category = ImportChunkCategories.Empty, Text = "" });

        return new ImportSessionState
        {
            Initialized = true,
            Chunks = chunks,
            Settings = new ImportSettings { WeightFloor = 0.5, Anchor = Anchor, SpacingMinutes = 10 },
        };
    }

    private static ImportScene AddScene(ImportSessionState state, int from, int to)
    {
        var scene = new ImportScene { Id = Guid.NewGuid(), FromChunk = from, ToChunk = to };
        state.Scenes.Add(scene);
        return scene;
    }

    private static MappedItem Episode(string summary, double? weight, string sourceId,
        List<int>? chunks = null, List<string>? participants = null) => new()
    {
        SourceId = sourceId,
        Type = DraftItemTypes.Episode,
        Summary = summary,
        Weight = weight,
        Participants = participants ?? new List<string>(),
        SourceChunks = chunks ?? new List<int>(),
    };

    private static MappedItem Trait(string persona, string summary, string sourceId) => new()
    {
        SourceId = sourceId,
        Type = DraftItemTypes.Trait,
        Persona = persona,
        Summary = summary,
    };

    private static SceneMapResult Map(params MappedItem[] items) => new()
    {
        Items = items.ToList(),
        LlmCalls = 1,
    };

    // ── routing + weight floor ───────────────────────────────────────────────────

    [Fact]
    public void Episode_above_floor_routes_to_event_below_floor_to_history_with_reason()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);

        ImportFold.Apply(state, scene.Id, Map(
            Episode("Lena confronted Denise about the stolen ledger at the clinic.", 0.85, "w0", new List<int> { 3 }),
            Episode("Justin mentioned the weather was mild on the drive over.", 0.2, "w0", new List<int> { 5 })), RanAt);

        var high = state.Items.Single(i => i.Weight == 0.85);
        var low = state.Items.Single(i => i.Weight == 0.2);
        Assert.Equal(DraftRouting.Event, high.Routing);
        Assert.Null(high.RoutingReason);
        Assert.Equal(DraftRouting.History, low.Routing);
        Assert.Contains("below floor", low.RoutingReason);
        Assert.False(low.RoutingOverridden);
    }

    [Fact]
    public void Null_or_out_of_range_weight_falls_back_to_default()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);

        ImportFold.Apply(state, scene.Id, Map(
            Episode("Mara revealed the project funding had collapsed entirely.", null, "w0", new List<int> { 3 }),
            Episode("Anne handed Sienna the keys to the archive downstairs.", 3.5, "w1", new List<int> { 4 })), RanAt);

        Assert.All(state.Items, i => Assert.Equal(ImportFold.DefaultEpisodeWeight, i.Weight));
    }

    [Fact]
    public void Rule_items_are_discarded_with_reason_never_silently_dropped()
    {
        var state = NewState();
        var scene = AddScene(state, 2, 2);

        ImportFold.Apply(state, scene.Id, Map(new MappedItem
        {
            SourceId = "chunk[2]#0",
            Type = DraftItemTypes.Rule,
            Summary = "Always answer in markdown with dialogue in quotes.",
        }), RanAt);

        var rule = Assert.Single(state.Items);
        Assert.Equal(DraftRouting.Discarded, rule.Routing);
        Assert.Equal("agent-instruction", rule.RoutingReason);
    }

    [Fact]
    public void Timestamps_follow_anchor_plus_chunk_ordinal_times_spacing()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);

        ImportFold.Apply(state, scene.Id, Map(
            Episode("Lena finally admitted she had hidden the diagnosis from everyone.", 0.9, "w0", new List<int> { 6, 7 })), RanAt);

        Assert.Equal(Anchor + TimeSpan.FromMinutes(60), state.Items.Single().At);
    }

    // ── rerun-replace semantics ──────────────────────────────────────────────────

    [Fact]
    public void Rerun_replaces_the_scenes_slice_without_touching_other_scenes()
    {
        var state = NewState();
        var sceneA = AddScene(state, 3, 5);
        var sceneB = AddScene(state, 6, 8);

        ImportFold.Apply(state, sceneA.Id, Map(
            Episode("Denise stormed out of the kitchen after the argument about money.", 0.7, "a0", new List<int> { 3 })), RanAt);
        ImportFold.Apply(state, sceneB.Id, Map(
            Episode("Justin quietly repaired the broken window before anyone noticed.", 0.6, "b0", new List<int> { 7 })), RanAt);
        var bItemId = state.Items.Single(i => i.SceneId == sceneB.Id).Id;

        var rerun = ImportFold.Apply(state, sceneA.Id, Map(
            Episode("Lena discovered the letters hidden beneath the floorboards upstairs.", 0.8, "a1", new List<int> { 4 })), RanAt);

        Assert.Equal(1, rerun.ReplacedItems);
        Assert.Equal(2, state.Items.Count);
        Assert.Contains(state.Items, i => i.SceneId == sceneA.Id && i.Summary.Contains("floorboards"));
        Assert.Equal(bItemId, state.Items.Single(i => i.SceneId == sceneB.Id).Id);
        Assert.Equal(2, sceneA.RunCount);
    }

    // ── dedup ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Cross_scene_retelling_is_deduped_keeping_the_first_telling()
    {
        var state = NewState();
        var sceneA = AddScene(state, 3, 5);
        var sceneB = AddScene(state, 6, 8);

        ImportFold.Apply(state, sceneA.Id, Map(
            Episode("Lena confessed to Denise that she had falsified the trial results.", 0.9, "a0", new List<int> { 3 },
                new List<string> { "Lena", "Denise" })), RanAt);
        var result = ImportFold.Apply(state, sceneB.Id, Map(
            Episode("Lena confessed to Denise she falsified the trial results months ago.", 0.85, "b0", new List<int> { 6 },
                new List<string> { "Lena", "Denise" })), RanAt);

        Assert.Empty(result.Items);
        var drop = Assert.Single(result.Deduped);
        Assert.Contains("falsified", drop.KeptSummary);
        Assert.Single(state.Items);
    }

    [Fact]
    public void Same_call_items_are_never_deduped_against_each_other()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);

        // the model already split these — near-identical summaries from ONE call both survive
        var result = ImportFold.Apply(state, scene.Id, Map(
            Episode("Lena argued with Denise about the missing clinic funds.", 0.7, "w0", new List<int> { 3 }, new List<string> { "Lena", "Denise" }),
            Episode("Lena argued with Denise about the missing clinic funds again.", 0.6, "w0", new List<int> { 4 }, new List<string> { "Lena", "Denise" })), RanAt);

        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public void Disjoint_participants_block_dedup_even_with_word_overlap()
    {
        var state = NewState();
        var sceneA = AddScene(state, 3, 5);
        var sceneB = AddScene(state, 6, 8);

        ImportFold.Apply(state, sceneA.Id, Map(
            Episode("Justin broke the antique vase during the heated argument.", 0.6, "a0", new List<int> { 3 },
                new List<string> { "Justin" })), RanAt);
        var result = ImportFold.Apply(state, sceneB.Id, Map(
            Episode("Mara broke the antique vase during the heated argument.", 0.6, "b0", new List<int> { 6 },
                new List<string> { "Mara" })), RanAt);

        Assert.Single(result.Items);
        Assert.Equal(2, state.Items.Count(i => i.Type == DraftItemTypes.Episode));
    }

    // ── persona folding + alias canonicalisation ─────────────────────────────────

    [Fact]
    public void Trait_keys_fold_by_token_subset_within_one_run()
    {
        var state = NewState();
        var scene = AddScene(state, 2, 2);

        ImportFold.Apply(state, scene.Id, Map(
            Trait("Dr. Lena Brandt", "Works as a trauma therapist in Rotterdam.", "s0"),
            Trait("Lena", "Keeps her office deliberately cluttered.", "s1")), RanAt);

        Assert.All(state.Items, i => Assert.Equal("Lena Brandt", i.Persona));
    }

    [Fact]
    public void New_trait_key_folds_onto_existing_canonical_without_renaming_other_scenes()
    {
        var state = NewState();
        var sceneA = AddScene(state, 2, 2);
        var sceneB = AddScene(state, 3, 5);

        ImportFold.Apply(state, sceneA.Id, Map(
            Trait("Lena", "Works as a trauma therapist in Rotterdam.", "a0")), RanAt);
        ImportFold.Apply(state, sceneB.Id, Map(
            Trait("Dr. Lena Brandt", "Distrusts hospital administrators on principle.", "b0")), RanAt);

        // sceneB's key folds onto the existing canonical; sceneA's item is untouched
        Assert.All(state.Items, i => Assert.Equal("Lena", i.Persona));
    }

    [Fact]
    public void Duplicate_trait_summaries_per_persona_are_deduped()
    {
        var state = NewState();
        var sceneA = AddScene(state, 2, 2);
        var sceneB = AddScene(state, 3, 5);

        ImportFold.Apply(state, sceneA.Id, Map(
            Trait("Lena", "Keeps her office deliberately cluttered.", "a0")), RanAt);
        var result = ImportFold.Apply(state, sceneB.Id, Map(
            Trait("Lena Brandt", "keeps her office deliberately cluttered.", "b0")), RanAt);

        Assert.Empty(result.Items);
        Assert.Single(result.Deduped);
        Assert.Single(state.Items);
    }

    [Fact]
    public void Participants_are_canonicalised_onto_folded_persona_names()
    {
        var state = NewState();
        var scene = AddScene(state, 2, 5);

        ImportFold.Apply(state, scene.Id, Map(
            Trait("Dr. Lena Brandt", "Works as a trauma therapist in Rotterdam.", "s0"),
            Episode("Lena admitted the trial data had been fabricated from the start.", 0.9, "w0", new List<int> { 3 },
                new List<string> { "Lena" })), RanAt);

        var episode = state.Items.Single(i => i.Type == DraftItemTypes.Episode);
        Assert.Equal(new List<string> { "Lena Brandt" }, episode.Participants);
    }

    [Fact]
    public void Malformed_trait_items_degrade_with_reason_instead_of_vanishing()
    {
        var state = NewState();
        var scene = AddScene(state, 2, 2);

        var result = ImportFold.Apply(state, scene.Id, Map(
            Trait("", "A trait with no persona key attached.", "s0"),
            Trait("She is a kind and thoughtful person who always listens carefully to everyone",
                "Leaked sentence as key.", "s1")), RanAt);

        Assert.Empty(result.Items);
        Assert.Equal(2, result.Degraded.Count);
        Assert.Contains(result.Degraded, d => d.Reason == "trait without persona");
        Assert.Contains(result.Degraded, d => d.Reason == "persona key not name-shaped");
    }

    // ── concept merging ──────────────────────────────────────────────────────────

    [Fact]
    public void Concepts_merge_by_source_stated_aliases_only()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);

        ImportFold.Apply(state, scene.Id, Map(
            Episode("Justin unveiled the Marigold Project to the assembled board.", 0.8, "w0", new List<int> { 3 },
                new List<string> { "Justin" }) with
            {
                Concepts = new List<ConceptDraft>
                {
                    new() { Name = "Marigold Project", Aliases = new List<string> { "Marigold" } },
                },
            },
            Episode("Denise leaked the Marigold budget to the local press.", 0.7, "w1", new List<int> { 6 },
                new List<string> { "Denise" }) with
            {
                Concepts = new List<ConceptDraft> { new() { Name = "Marigold" } },
            }), RanAt);

        var concept = Assert.Single(state.Concepts);
        Assert.Equal("Marigold Project", concept.Name);
        Assert.Contains("Marigold", concept.Aliases);
        Assert.Equal(2, concept.Mentions);
        // both items reference the canonical concept name
        Assert.All(state.Items, i => Assert.Equal(new List<string> { "Marigold Project" }, i.Concepts));
    }

    // ── item edits ───────────────────────────────────────────────────────────────

    [Fact]
    public void Flipping_a_sub_floor_episode_to_event_sets_the_override()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);
        ImportFold.Apply(state, scene.Id, Map(
            Episode("Sienna mentioned in passing that the ferry was late again.", 0.2, "w0", new List<int> { 3 })), RanAt);
        var item = state.Items.Single();
        Assert.Equal(DraftRouting.History, item.Routing);

        ImportFold.Edit(state, item.Id, new DraftItemEdit { Routing = "event" });

        Assert.Equal(DraftRouting.Event, item.Routing);
        Assert.True(item.RoutingOverridden);

        ImportFold.Edit(state, item.Id, new DraftItemEdit { Routing = "auto" });

        Assert.Equal(DraftRouting.History, item.Routing);
        Assert.False(item.RoutingOverridden);
    }

    [Fact]
    public void Weight_edit_re_derives_floor_routing_unless_overridden()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);
        ImportFold.Apply(state, scene.Id, Map(
            Episode("Anne quietly returned the borrowed telescope to the observatory.", 0.3, "w0", new List<int> { 3 })), RanAt);
        var item = state.Items.Single();

        ImportFold.Edit(state, item.Id, new DraftItemEdit { Weight = 0.9 });
        Assert.Equal(DraftRouting.Event, item.Routing);

        ImportFold.Edit(state, item.Id, new DraftItemEdit { Routing = "history" });
        ImportFold.Edit(state, item.Id, new DraftItemEdit { Weight = 0.95 });
        Assert.Equal(DraftRouting.History, item.Routing); // override survives weight edits
    }

    [Fact]
    public void Routing_flips_reject_non_episodes_and_unknown_values()
    {
        var state = NewState();
        var scene = AddScene(state, 2, 2);
        ImportFold.Apply(state, scene.Id, Map(
            Trait("Lena", "Works as a trauma therapist in Rotterdam.", "s0")), RanAt);
        var trait = state.Items.Single();

        Assert.Throws<ArgumentException>(() => ImportFold.Edit(state, trait.Id, new DraftItemEdit { Routing = "event" }));
        Assert.Throws<KeyNotFoundException>(() => ImportFold.Edit(state, Guid.NewGuid(), new DraftItemEdit { Routing = "event" }));
    }

    [Fact]
    public void Raising_the_floor_re_marks_only_non_overridden_episodes()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);
        ImportFold.Apply(state, scene.Id, Map(
            Episode("Lena confronted the hospital board over the buried report.", 0.6, "w0", new List<int> { 3 }),
            Episode("Denise admitted she had tipped off the journalist herself.", 0.6, "w1", new List<int> { 6 })), RanAt);
        var flipped = state.Items.First();
        ImportFold.Edit(state, flipped.Id, new DraftItemEdit { Routing = "event" });

        state.Settings = state.Settings with { WeightFloor = 0.7 };
        ImportFold.ReapplySettings(state);

        Assert.Equal(DraftRouting.Event, flipped.Routing); // human call stands
        Assert.Equal(DraftRouting.History, state.Items.Last().Routing);
        Assert.Contains("below floor", state.Items.Last().RoutingReason);
    }

    // ── conservation ledger ──────────────────────────────────────────────────────

    [Fact]
    public void Ledger_accounts_for_every_chunk_and_reconciles()
    {
        var state = NewState();
        var scene = AddScene(state, 0, 9); // whole strip in one scene

        ImportFold.Apply(state, scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                Episode("Lena confronted Denise about the falsified trial results.", 0.9, "w0", new List<int> { 3 }),
                Episode("Justin mumbled something about the ferry being late.", 0.2, "w0", new List<int> { 4 }),
            },
            Discards = new List<ChunkDiscard> { new() { ChunkIndex = 5, Reason = "OOC meta" } },
            LlmCalls = 2,
        }, RanAt);

        var ledger = ImportFold.BuildLedger(state);

        Assert.True(ledger.Reconciles);
        Assert.Equal(10, ledger.TotalChunks);
        Assert.Equal(10, ledger.ByDisposition.Values.Sum());

        var by = ledger.Chunks.ToDictionary(c => c.ChunkIndex);
        Assert.Equal(ChunkDispositions.Discarded, by[0].Disposition); // thought
        Assert.Equal("thought", by[0].Reason);
        Assert.Equal(ChunkDispositions.Discarded, by[1].Disposition); // media
        Assert.Equal(ChunkDispositions.Folded, by[2].Disposition);    // recap
        Assert.Equal(ChunkDispositions.EventRouted, by[3].Disposition);
        Assert.Equal(ChunkDispositions.Folded, by[4].Disposition);    // fed a sub-floor episode
        Assert.Equal(ChunkDispositions.Discarded, by[5].Disposition);
        Assert.Equal("OOC meta", by[5].Reason);
        Assert.Equal(ChunkDispositions.HistoryOnly, by[6].Disposition);
        Assert.Equal(ChunkDispositions.Discarded, by[9].Disposition); // empty
    }

    [Fact]
    public void Chunks_outside_any_run_scene_are_unprocessed()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 5);
        AddScene(state, 6, 8); // defined but never run

        ImportFold.Apply(state, scene.Id, Map(
            Episode("Mara revealed the clinic would close within the month.", 0.8, "w0", new List<int> { 3 })), RanAt);
        var ledger = ImportFold.BuildLedger(state);

        var by = ledger.Chunks.ToDictionary(c => c.ChunkIndex);
        Assert.Equal(ChunkDispositions.Unprocessed, by[0].Disposition);
        Assert.Equal(ChunkDispositions.Unprocessed, by[6].Disposition); // scene exists, no run
        Assert.Equal(ChunkDispositions.EventRouted, by[3].Disposition);
        Assert.True(ledger.Reconciles);
    }

    [Fact]
    public void Flipping_an_item_moves_its_chunks_between_ledger_buckets()
    {
        var state = NewState();
        var scene = AddScene(state, 3, 8);
        ImportFold.Apply(state, scene.Id, Map(
            Episode("Sienna offhandedly mentioned the ferry schedule had changed.", 0.2, "w0", new List<int> { 3 })), RanAt);

        Assert.Equal(ChunkDispositions.Folded,
            ImportFold.BuildLedger(state).Chunks.Single(c => c.ChunkIndex == 3).Disposition);

        ImportFold.Edit(state, state.Items.Single().Id, new DraftItemEdit { Routing = "event" });

        Assert.Equal(ChunkDispositions.EventRouted,
            ImportFold.BuildLedger(state).Chunks.Single(c => c.ChunkIndex == 3).Disposition);
    }

    [Fact]
    public void Deduped_episode_still_marks_its_chunks_folded_not_history()
    {
        var state = NewState();
        var sceneA = AddScene(state, 3, 5);
        var sceneB = AddScene(state, 6, 8);

        ImportFold.Apply(state, sceneA.Id, Map(
            Episode("Lena confessed she had falsified the entire trial dataset.", 0.9, "a0", new List<int> { 3 },
                new List<string> { "Lena" })), RanAt);
        ImportFold.Apply(state, sceneB.Id, Map(
            Episode("Lena confessed that she falsified the entire trial dataset.", 0.9, "b0", new List<int> { 6 },
                new List<string> { "Lena" })), RanAt);

        // chunk 6 fed an episode that was deduped away — salient, so folded, not history-only
        Assert.Equal(ChunkDispositions.Folded,
            ImportFold.BuildLedger(state).Chunks.Single(c => c.ChunkIndex == 6).Disposition);
    }
}
