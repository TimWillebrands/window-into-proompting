using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Import;

namespace BackendTest;

/// <summary>
/// The deterministic commit plan (ADR 0017 slice 3): history-routed chunks → Room
/// messages, event-routed episodes → AGE seeds, cast minting, and the suggested-vs-final
/// correction diff. Pure input → plan, so every commit rule is assertable here; the
/// committed-scene guards on the fold live in this file too (they are commit semantics).
/// </summary>
public class ImportCommitPlannerTest
{
    private static readonly DateTimeOffset Anchor = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SessionId = new("11111111-1111-1111-1111-111111111111");

    private static readonly ImportCommitTarget Target = new()
    {
        PartyId = new Guid("22222222-2222-2222-2222-222222222222"),
        RoomId = new Guid("33333333-3333-3333-3333-333333333333"),
        RoomName = "Imported chat",
        UserParticipantId = new Guid("44444444-4444-4444-4444-444444444444"),
    };

    /// <summary>Scene over chunks 2..8: 2 recap, 3-8 messages alternating model/user.</summary>
    private static SceneCommitInput NewInput(
        List<ImportDraftItem>? items = null,
        List<ImportDraftItem>? traits = null,
        Dictionary<string, Guid>? committedPersonas = null,
        int runCount = 1,
        string? note = null,
        List<ChunkRouting>? routingOverrides = null,
        List<RegistryCastEntry>? cast = null,
        Dictionary<string, PersonaCardDraft>? cards = null)
    {
        var scene = new ImportScene { Id = Guid.NewGuid(), FromChunk = 2, ToChunk = 8, RunCount = runCount, Note = note };
        var chunks = new List<ImportChunk>
        {
            new() { Index = 2, Role = "user", Category = ImportChunkCategories.Recap, Text = "# History\nstuff" },
        };
        for (var i = 3; i <= 8; i++)
            chunks.Add(new ImportChunk { Index = i, Role = i % 2 == 0 ? "user" : "model", Category = ImportChunkCategories.Message, Text = $"msg {i}" });

        var routings = routingOverrides ?? chunks.Select(c => new ChunkRouting
        {
            ChunkIndex = c.Index,
            Category = c.Category,
            Disposition = c.Category == ImportChunkCategories.Recap
                ? ChunkDispositions.Folded
                : ChunkDispositions.HistoryOnly,
            SceneId = scene.Id,
        }).ToList();

        return new SceneCommitInput
        {
            SessionId = SessionId,
            FileName = "export.json",
            Settings = new ImportSettings { WeightFloor = 0.5, Anchor = Anchor, SpacingMinutes = 10 },
            Scene = scene,
            Items = items ?? new List<ImportDraftItem>(),
            TraitItems = traits ?? new List<ImportDraftItem>(),
            Chunks = chunks,
            ChunkRoutings = routings,
            Target = Target,
            CommittedPersonas = committedPersonas ?? new Dictionary<string, Guid>(),
            Cast = cast ?? new List<RegistryCastEntry>(),
            Cards = cards ?? new Dictionary<string, PersonaCardDraft>(),
        };
    }

    /// <summary>A reviewed finalize card — commit refuses to mint without one.</summary>
    private static PersonaCardDraft Card(
        string persona, string? bio = "One line about them", string? committedPrompt = null, string? committedBio = null) => new()
    {
        Persona = persona,
        SystemPrompt = $"You are {persona}.\n\n- Compressed trait.",
        Bio = bio,
        CommittedSystemPrompt = committedPrompt,
        CommittedBio = committedBio,
    };

    private static Dictionary<string, PersonaCardDraft> Cards(params PersonaCardDraft[] cards)
        => cards.ToDictionary(c => c.Persona, c => c);

    private static ImportDraftItem Episode(
        Guid sceneId, string summary, double weight, string routing,
        List<int> chunks, List<string>? participants = null, List<string>? concepts = null)
    {
        var item = new ImportDraftItem
        {
            Id = Guid.NewGuid(),
            SceneId = sceneId,
            Type = DraftItemTypes.Episode,
            Summary = summary,
            Weight = weight,
            Routing = routing,
            Participants = participants ?? new List<string>(),
            Concepts = concepts ?? new List<string>(),
            SourceChunks = chunks,
            At = Anchor + TimeSpan.FromMinutes(10 * chunks.Min()),
        };
        item.SuggestedSummary = summary;
        item.SuggestedWeight = weight;
        item.SuggestedRouting = routing;
        return item;
    }

    private static ImportDraftItem TraitOf(string persona, string summary)
    {
        var item = new ImportDraftItem
        {
            Id = Guid.NewGuid(),
            Type = DraftItemTypes.Trait,
            Persona = persona,
            Summary = summary,
            Routing = DraftRouting.PersonaCard,
        };
        item.SuggestedSummary = summary;
        item.SuggestedRouting = DraftRouting.PersonaCard;
        return item;
    }

    // ── messages ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Messages_come_from_message_chunks_only_in_order_at_scheme_timestamps()
    {
        var plan = ImportCommitPlanner.Plan(NewInput());

        // Recap chunk 2 is folded into canon, not chat history; 3..8 are messages.
        Assert.Equal(6, plan.Messages.Count);
        Assert.Equal(
            Enumerable.Range(3, 6).Select(i => (Anchor + TimeSpan.FromMinutes(10 * i)).ToUnixTimeMilliseconds()),
            plan.Messages.Select(m => m.SendAt));
        Assert.All(plan.Messages, m => Assert.Equal(Target.RoomId, m.ChatGroupId));

        // Role mapping: user → the import's User participant, model → Narrator.
        var userMsg = plan.Messages.First(m => m.Content == "msg 4");
        Assert.Equal("user", userMsg.SenderType);
        Assert.Equal(Target.UserParticipantId, userMsg.SenderId);
        var modelMsg = plan.Messages.First(m => m.Content == "msg 3");
        Assert.Equal("assistant", modelMsg.SenderType);
        Assert.Equal(Narrator.PersonaId, modelMsg.SenderId);
    }

    [Fact]
    public void Discarded_chunks_produce_no_message_but_history_and_event_routed_do()
    {
        var input = NewInput();
        var routings = input.ChunkRoutings.Select(r => r.ChunkIndex switch
        {
            3 => r with { Disposition = ChunkDispositions.Discarded, Reason = "ooc" },
            4 => r with { Disposition = ChunkDispositions.EventRouted },
            _ => r,
        }).ToList();

        var plan = ImportCommitPlanner.Plan(input with { ChunkRoutings = routings });

        Assert.DoesNotContain(plan.Messages, m => m.Content == "msg 3");
        Assert.Contains(plan.Messages, m => m.Content == "msg 4");
        Assert.Equal(5, plan.Messages.Count);
    }

    // ── events ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Subfloor_items_produce_no_event_seed_but_their_chunks_stay_messages()
    {
        var input = NewInput();
        var sub = Episode(input.Scene.Id, "Small talk about the drive over.", 0.2, DraftRouting.History, new List<int> { 5 });
        var plan = ImportCommitPlanner.Plan(input with { Items = new List<ImportDraftItem> { sub } });

        Assert.Empty(plan.Events);
        Assert.Contains(plan.Messages, m => m.Content == "msg 5");
    }

    [Fact]
    public void Event_seed_carries_timestamp_weight_concepts_and_resolved_participants()
    {
        var input = NewInput(
            traits: new List<ImportDraftItem> { TraitOf("Dr. Lena Brandt", "Sardonic under pressure.") },
            cards: Cards(Card("Dr. Lena Brandt")));
        var ev = Episode(input.Scene.Id, "Lena confronted the innkeeper about the ledger.", 0.85,
            DraftRouting.Event, new List<int> { 6 },
            participants: new List<string> { "Lena", "The Innkeeper" },
            concepts: new List<string> { "Shower Protocol" });
        var plan = ImportCommitPlanner.Plan(input with { Items = new List<ImportDraftItem> { ev } });

        var seed = Assert.Single(plan.Events);
        Assert.Equal(ev.Id, seed.EventId);
        Assert.Equal(ev.At, seed.At);
        Assert.Equal(0.85, seed.Weight);
        Assert.Equal(6, seed.AnchorOrdinal);
        var concept = Assert.Single(seed.Concepts);
        Assert.Equal("shower protocol", concept.Name);
        Assert.Equal("Shower Protocol", concept.Display);

        // "Lena" token-folds into the dossier'd "Dr. Lena Brandt"; the innkeeper has no
        // traits and no registry entry, so no persona is invented for them.
        var lenaId = plan.CastByName["Dr. Lena Brandt"];
        Assert.Equal(new List<Guid> { lenaId }, seed.RecollectorPersonaIds);
        Assert.Equal(new List<string> { "The Innkeeper" }, plan.UnmatchedParticipants);
    }

    // ── cast minting ─────────────────────────────────────────────────────────────

    [Fact]
    public void Minting_is_deterministic_skips_committed_personas_and_uses_reviewed_cards()
    {
        var lenaTrait = TraitOf("Dr. Lena Brandt", "Sardonic under pressure.");
        var justinTrait = TraitOf("Justin", "Chronically late.");
        var committedJustin = new Dictionary<string, Guid> { ["Justin"] = Guid.NewGuid() };
        var lenaCard = Card("Dr. Lena Brandt", bio: "A sardonic clinician");

        var input = NewInput(
            traits: new List<ImportDraftItem> { lenaTrait, justinTrait },
            committedPersonas: committedJustin,
            cards: Cards(lenaCard));
        // This scene touches both: Lena via her traits, Justin via an event participant.
        input = input with
        {
            Items = new List<ImportDraftItem>
            {
                lenaTrait,
                Episode(input.Scene.Id, "Justin arrived late again.", 0.7, DraftRouting.Event,
                    new List<int> { 7 }, participants: new List<string> { "Justin" }),
            },
        };

        var plan = ImportCommitPlanner.Plan(input);

        // The mint's card is the reviewed finalize output, Bio included — never rebuilt.
        var mint = Assert.Single(plan.PersonasToMint);
        Assert.Equal("Dr. Lena Brandt", mint.Name);
        Assert.Equal(lenaCard.SystemPrompt, mint.SystemPrompt);
        Assert.Equal("A sardonic clinician", mint.Bio);
        Assert.Equal(committedJustin["Justin"], plan.CastByName["Justin"]);

        // Same input → same persona id (retries and re-commits converge).
        var again = ImportCommitPlanner.Plan(input);
        Assert.Equal(mint.PersonaId, Assert.Single(again.PersonasToMint).PersonaId);

        // The import's human rides along as a User-driven participant.
        Assert.Contains(plan.Participants, p => p.Id == Target.UserParticipantId && p.Driver == DriverKind.User);
        Assert.Contains(plan.Participants, p => p.Id == mint.PersonaId && p.Driver == DriverKind.LLM);
    }

    [Fact]
    public void Minting_without_a_reviewed_card_is_refused()
    {
        var lenaTrait = TraitOf("Dr. Lena Brandt", "Sardonic under pressure.");
        var input = NewInput(traits: new List<ImportDraftItem> { lenaTrait });
        input = input with { Items = new List<ImportDraftItem> { lenaTrait } };

        var ex = Assert.Throws<InvalidOperationException>(() => ImportCommitPlanner.Plan(input));
        Assert.Contains("no reviewed card", ex.Message);
    }

    // ── match-or-mint (registry decisions executed, never prompted) ──────────────

    private static RegistryCastEntry Entry(
        string name, string matchState = CastMatchStates.Unmatched, Guid? proposedId = null,
        Guid? matchedId = null, string routing = CastRoutingModes.Persona,
        bool confirmed = true, params string[] aliases) => new()
    {
        Name = name,
        Aliases = aliases.ToList(),
        Routing = routing,
        Confirmed = confirmed,
        MatchState = matchState,
        ProposedPersonaId = proposedId,
        ProposedPersonaName = proposedId is null ? null : "Library " + name,
        MatchedPersonaId = matchedId,
    };

    [Fact]
    public void Commit_blocks_on_undecided_match_proposal()
    {
        var trait = TraitOf("Denise", "Steady under fire.");
        var input = NewInput(
            traits: new List<ImportDraftItem> { trait },
            cast: new List<RegistryCastEntry>
            {
                Entry("Denise", CastMatchStates.Proposed, proposedId: Guid.NewGuid()),
            },
            cards: Cards(Card("Denise")));
        input = input with { Items = new List<ImportDraftItem> { trait } };

        var ex = Assert.Throws<InvalidOperationException>(() => ImportCommitPlanner.Plan(input));
        Assert.Contains("Denise", ex.Message);
        Assert.Contains("undecided", ex.Message);
    }

    [Fact]
    public void Confirmed_match_reuses_the_library_persona_and_confirmed_mint_creates_one()
    {
        var libraryDenise = Guid.NewGuid();
        var deniseTrait = TraitOf("Denise", "Steady under fire.");
        var lenaTrait = TraitOf("Lena", "Sardonic.");
        var input = NewInput(
            traits: new List<ImportDraftItem> { deniseTrait, lenaTrait },
            cast: new List<RegistryCastEntry>
            {
                Entry("Denise", CastMatchStates.ConfirmedMatch, proposedId: libraryDenise, matchedId: libraryDenise),
                Entry("Lena", CastMatchStates.ConfirmedMint, proposedId: Guid.NewGuid()),
            },
            cards: Cards(Card("Denise"), Card("Lena")));
        input = input with { Items = new List<ImportDraftItem> { deniseTrait, lenaTrait } };

        var plan = ImportCommitPlanner.Plan(input);

        // Match: no mint, the library id is reused; first commit pushes the reviewed card.
        Assert.Equal(libraryDenise, plan.CastByName["Denise"]);
        Assert.DoesNotContain(plan.PersonasToMint, m => m.Name == "Denise");
        Assert.Contains(plan.PersonasToUpdate, u => u.PersonaId == libraryDenise && u.Name == "Denise");
        Assert.Contains(plan.Participants, p => p.Id == libraryDenise);

        // Mint despite a proposal: new persona, and the override lands in the ledger.
        var mint = Assert.Single(plan.PersonasToMint);
        Assert.Equal("Lena", mint.Name);
        // Accepting the proposed match (Denise) is not a flip — only Lena's override is.
        var flip = Assert.Single(plan.Corrections, c => c.Kind == CorrectionKinds.MatchFlipped);
        Assert.Contains("Library Lena", flip.Suggested);
        Assert.Contains(CastMatchStates.ConfirmedMint, flip.Final);
    }

    [Fact]
    public void Registry_alias_resolves_participants_and_concept_routing_links_events()
    {
        var deniseTrait = TraitOf("Denise", "Steady under fire.");
        var input = NewInput(
            traits: new List<ImportDraftItem> { deniseTrait },
            cast: new List<RegistryCastEntry>
            {
                Entry("Denise", CastMatchStates.ConfirmedMint, aliases: "Denyse"),
                Entry("Arend", routing: CastRoutingModes.Concept),
            },
            cards: Cards(Card("Denise")));
        var ev = Episode(input.Scene.Id, "Denyse warned Arend about the flood.", 0.8,
            DraftRouting.Event, new List<int> { 5 },
            participants: new List<string> { "Denyse", "Arend", "Old mentor" });
        input = input with { Items = new List<ImportDraftItem> { deniseTrait, ev } };

        var plan = ImportCommitPlanner.Plan(input);

        // Alias "Denyse" resolves to Denise — recollector, not unmatched.
        var seed = Assert.Single(plan.Events);
        Assert.Equal(new List<Guid> { plan.CastByName["Denise"] }, seed.RecollectorPersonaIds);

        // Person-as-concept: Arend links the event to a Concept instead of minting.
        Assert.Contains(seed.Concepts, c => c.Name == "arend" && c.Display == "Arend");
        Assert.DoesNotContain(plan.PersonasToMint, m => m.Name == "Arend");

        // Nobody claimed "Old mentor" — counted, never minted.
        Assert.Equal(new List<string> { "Old mentor" }, plan.UnmatchedParticipants);
    }

    [Fact]
    public void Later_commit_updates_a_changed_card_without_duplicating_the_persona()
    {
        var lenaId = Guid.NewGuid();
        var lenaTrait = TraitOf("Lena", "Sardonic.");
        var changed = Card("Lena", committedPrompt: "You are Lena.\n\n- Old compressed trait.", committedBio: "Old bio");
        var input = NewInput(
            traits: new List<ImportDraftItem> { lenaTrait },
            committedPersonas: new Dictionary<string, Guid> { ["Lena"] = lenaId },
            cards: Cards(changed));
        input = input with { Items = new List<ImportDraftItem> { lenaTrait } };

        var plan = ImportCommitPlanner.Plan(input);
        Assert.Empty(plan.PersonasToMint);
        var update = Assert.Single(plan.PersonasToUpdate);
        Assert.Equal(lenaId, update.PersonaId);
        Assert.Equal(changed.SystemPrompt, update.SystemPrompt);

        // An unchanged card produces no persona write at all.
        var current = Card("Lena");
        current.CommittedSystemPrompt = current.SystemPrompt;
        current.CommittedBio = current.Bio;
        var quiet = ImportCommitPlanner.Plan(input with { Cards = Cards(current) });
        Assert.Empty(quiet.PersonasToMint);
        Assert.Empty(quiet.PersonasToUpdate);
    }

    [Fact]
    public void Personas_needing_cards_lists_touched_cast_with_their_full_trait_lists()
    {
        var lenaTrait = TraitOf("Dr. Lena Brandt", "Sardonic under pressure.");
        var lenaTrait2 = TraitOf("Dr. Lena Brandt", "Keeps a ledger of favours.");
        var input = NewInput(traits: new List<ImportDraftItem> { lenaTrait, lenaTrait2 });
        input = input with
        {
            // No commit target needed — finalize runs before the first commit pins one.
            Target = null,
            Items = new List<ImportDraftItem> { lenaTrait },
        };

        var required = Assert.Single(ImportCommitPlanner.PersonasNeedingCards(input));
        Assert.Equal("Dr. Lena Brandt", required.Name);
        Assert.Equal(2, required.Traits.Count);
        Assert.Equal(ImportCommitPlanner.TraitFingerprint(required.Traits), required.TraitFingerprint);
    }

    // ── correction ledger ────────────────────────────────────────────────────────

    [Fact]
    public void Unedited_first_run_scene_produces_no_corrections()
    {
        var input = NewInput();
        var ev = Episode(input.Scene.Id, "Something happened.", 0.8, DraftRouting.Event, new List<int> { 3 });
        var plan = ImportCommitPlanner.Plan(input with { Items = new List<ImportDraftItem> { ev } });
        Assert.Empty(plan.Corrections);
    }

    [Fact]
    public void Floor_flips_edits_and_regeneration_notes_become_corrections()
    {
        var input = NewInput(runCount: 2, note: "this scene is a dream sequence");
        var sceneId = input.Scene.Id;

        var promoted = Episode(sceneId, "Quiet but load-bearing beat.", 0.3, DraftRouting.Event, new List<int> { 3 });
        promoted.SuggestedRouting = DraftRouting.History; // extractor said sub-floor, human flipped up

        var demoted = Episode(sceneId, "Loud but irrelevant beat.", 0.8, DraftRouting.History, new List<int> { 4 });
        demoted.SuggestedRouting = DraftRouting.Event;

        var reworded = Episode(sceneId, "Lena confronted Denise about the ledger.", 0.7, DraftRouting.Event, new List<int> { 5 });
        reworded.SuggestedSummary = "Lena talked to Denise.";
        reworded.SuggestedWeight = 0.5; // also reweighted

        var plan = ImportCommitPlanner.Plan(input with
        {
            Items = new List<ImportDraftItem> { promoted, demoted, reworded },
        });

        Assert.Equal(CorrectionKinds.Promoted, Assert.Single(plan.Corrections, c => c.ItemId == promoted.Id).Kind);
        Assert.Equal(CorrectionKinds.Demoted, Assert.Single(plan.Corrections, c => c.ItemId == demoted.Id).Kind);
        Assert.Equal(
            new[] { CorrectionKinds.Renamed, CorrectionKinds.Reweighted }.OrderBy(k => k),
            plan.Corrections.Where(c => c.ItemId == reworded.Id).Select(c => c.Kind).OrderBy(k => k));

        var regen = Assert.Single(plan.Corrections, c => c.Kind == CorrectionKinds.RegeneratedWithNote);
        Assert.Null(regen.ItemId);
        Assert.Equal("this scene is a dream sequence", regen.Note);
        Assert.Equal(Enumerable.Range(2, 7), regen.ChunkRefs);

        // Suggested vs final snapshots ride along for the ledger.
        var flip = Assert.Single(plan.Corrections, c => c.ItemId == promoted.Id);
        Assert.Contains("history", flip.Suggested);
        Assert.Contains("event", flip.Final);

        // Correction ids are deterministic → a retried commit re-inserts, never duplicates.
        var again = ImportCommitPlanner.Plan(input with
        {
            Items = new List<ImportDraftItem> { promoted, demoted, reworded },
        });
        Assert.Equal(
            plan.Corrections.Select(c => c.Id).OrderBy(id => id),
            again.Corrections.Select(c => c.Id).OrderBy(id => id));
    }

    // ── committed-scene guards on the fold (commit semantics) ────────────────────

    [Fact]
    public void Fold_refuses_rerun_and_edits_for_committed_scenes_and_freezes_their_settings()
    {
        var state = new ImportSessionState
        {
            Initialized = true,
            Chunks = new List<ImportChunk>
            {
                new() { Index = 0, Role = "model", Category = ImportChunkCategories.Message, Text = "msg" },
            },
            Settings = new ImportSettings { WeightFloor = 0.5, Anchor = Anchor, SpacingMinutes = 10 },
        };
        var scene = new ImportScene { Id = Guid.NewGuid(), FromChunk = 0, ToChunk = 0 };
        state.Scenes.Add(scene);

        ImportFold.Apply(state, scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "w0", Type = DraftItemTypes.Episode,
                    Summary = "Lena admitted the clinic was closing.", Weight = 0.8,
                    SourceChunks = new List<int> { 0 },
                },
            },
            LlmCalls = 1,
        }, DateTimeOffset.UtcNow);
        var item = Assert.Single(state.Items);

        // Fold froze the extractor's suggestion for the correction ledger.
        Assert.Equal(item.Summary, item.SuggestedSummary);
        Assert.Equal(item.Weight, item.SuggestedWeight);
        Assert.Equal(DraftRouting.Event, item.SuggestedRouting);

        scene.Committed = true;
        scene.CommittedAt = DateTimeOffset.UtcNow;

        Assert.Throws<InvalidOperationException>(() =>
            ImportFold.Apply(state, scene.Id, new SceneMapResult(), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() =>
            ImportFold.Edit(state, item.Id, new DraftItemEdit { Routing = "history" }));

        // Settings changes leave committed items alone — their values are already written.
        var atBefore = item.At;
        state.Settings = state.Settings with { Anchor = Anchor.AddDays(-100), WeightFloor = 0.95 };
        ImportFold.ReapplySettings(state);
        Assert.Equal(atBefore, item.At);
        Assert.Equal(DraftRouting.Event, item.Routing);
    }
}
