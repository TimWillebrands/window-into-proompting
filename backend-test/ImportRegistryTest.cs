using PartyTown.Model;
using PartyTown.Services.Import;

namespace BackendTest;

/// <summary>
/// Registry mechanics (ADR 0017 slice 4): the library matcher (exact + Levenshtein),
/// registry-driven canonicalisation in the fold, and the fold's cast/concept proposals.
/// All pure — no cluster needed.
/// </summary>
public class ImportRegistryTest
{
    // ── matcher ──────────────────────────────────────────────────────────────────

    private static PersonaMetadata Lib(string name) => new(Guid.NewGuid(), name);

    [Fact]
    public void Matcher_prefers_exact_over_fuzzy_and_ignores_far_names()
    {
        var exact = Lib("Denise");
        var near = Lib("Denisa");
        var library = new[] { Lib("Arend"), near, exact };

        var hit = ImportCastMatcher.ProposeMatch("Denise", Array.Empty<string>(), library);
        Assert.Equal(exact.Id, hit!.Id);

        // Typo within tolerance still finds the library persona.
        var fuzzy = ImportCastMatcher.ProposeMatch("Denyse", Array.Empty<string>(), new[] { exact });
        Assert.Equal(exact.Id, fuzzy!.Id);

        // Far names and short-name near-misses propose nothing.
        Assert.Null(ImportCastMatcher.ProposeMatch("Justin", Array.Empty<string>(), new[] { exact }));
        Assert.Null(ImportCastMatcher.ProposeMatch("Al", Array.Empty<string>(), new[] { Lib("Ada") }));
    }

    [Fact]
    public void Matcher_scans_aliases_and_normalizes_honorifics()
    {
        var lena = Lib("Lena Brandt");
        var hit = ImportCastMatcher.ProposeMatch(
            "The Clinician", new[] { "Dr. Lena Brandt" }, new[] { lena });
        Assert.Equal(lena.Id, hit!.Id);
    }

    // ── fold: registry canonicalisation ──────────────────────────────────────────

    private static ImportSessionState StateWithScene(out ImportScene scene)
    {
        var state = new ImportSessionState
        {
            Initialized = true,
            Settings = new ImportSettings
            {
                WeightFloor = 0.5,
                Anchor = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero),
                SpacingMinutes = 10,
            },
            Chunks = Enumerable.Range(0, 4).Select(i => new ImportChunk
            {
                Index = i,
                Role = i % 2 == 0 ? "user" : "model",
                Category = ImportChunkCategories.Message,
                Text = $"msg {i}",
            }).ToList(),
        };
        scene = new ImportScene { Id = Guid.NewGuid(), FromChunk = 0, ToChunk = 3 };
        state.Scenes.Add(scene);
        return state;
    }

    [Fact]
    public void Confirmed_registry_aliases_canonicalise_trait_keys_and_participants()
    {
        var state = StateWithScene(out var scene);
        state.Cast.Add(new RegistryCastEntry
        {
            Name = "Denise",
            Aliases = new List<string> { "Denyse" },
            Confirmed = true,
        });

        var run = ImportFold.Apply(state, scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "s0", Type = DraftItemTypes.Trait,
                    Persona = "Denyse", Summary = "Steady under fire.",
                },
                new()
                {
                    SourceId = "w0", Type = DraftItemTypes.Episode,
                    Summary = "The flood warning reached the village.", Weight = 0.8,
                    Participants = new List<string> { "Denyse" },
                    SourceChunks = new List<int> { 1 },
                },
            },
            LlmCalls = 1,
        }, DateTimeOffset.UtcNow);

        Assert.Equal("Denise", run.Items.Single(i => i.Type == DraftItemTypes.Trait).Persona);
        Assert.Equal(new List<string> { "Denise" }, run.Items.Single(i => i.Type == DraftItemTypes.Episode).Participants);
    }

    [Fact]
    public void Unconfirmed_registry_entries_do_not_canonicalise()
    {
        var state = StateWithScene(out var scene);
        state.Cast.Add(new RegistryCastEntry
        {
            Name = "Denise",
            Aliases = new List<string> { "Denyse" },
            Confirmed = false,
        });

        var run = ImportFold.Apply(state, scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "s0", Type = DraftItemTypes.Trait,
                    Persona = "Denyse", Summary = "Steady under fire.",
                },
            },
            LlmCalls = 1,
        }, DateTimeOffset.UtcNow);

        Assert.Equal("Denyse", run.Items.Single().Persona);
    }

    // ── fold: scene-run proposals ────────────────────────────────────────────────

    [Fact]
    public void Scene_run_proposes_new_cast_and_participants_as_unconfirmed_entries()
    {
        var state = StateWithScene(out var scene);

        ImportFold.Apply(state, scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "s0", Type = DraftItemTypes.Trait,
                    Persona = "Dr. Lena Brandt", Summary = "Sardonic under pressure.",
                },
                new()
                {
                    SourceId = "w0", Type = DraftItemTypes.Episode,
                    Summary = "Lena warned Arend about the flood.", Weight = 0.8,
                    Participants = new List<string> { "Lena", "Arend" },
                    SourceChunks = new List<int> { 1 },
                },
            },
            LlmCalls = 1,
        }, DateTimeOffset.UtcNow);

        // Trait owner → persona-routed proposal; "Lena" resolves onto her by token
        // subset, so only the genuinely unclaimed "Arend" proposes as person-as-concept.
        var lena = Assert.Single(state.Cast, c => c.Name == "Lena Brandt");
        Assert.Equal(CastRoutingModes.Persona, lena.Routing);
        Assert.False(lena.Confirmed);
        Assert.Equal(CastMatchStates.Unmatched, lena.MatchState);

        var arend = Assert.Single(state.Cast, c => c.Name == "Arend");
        Assert.Equal(CastRoutingModes.Concept, arend.Routing);
        Assert.False(arend.Confirmed);

        // Rerunning the scene does not duplicate the proposals.
        ImportFold.Apply(state, scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "s0", Type = DraftItemTypes.Trait,
                    Persona = "Dr. Lena Brandt", Summary = "Sardonic under pressure.",
                },
            },
            LlmCalls = 1,
        }, DateTimeOffset.UtcNow);
        Assert.Single(state.Cast, c => c.Name == "Lena Brandt");
    }

    [Fact]
    public void Discovered_concepts_start_unconfirmed()
    {
        var state = StateWithScene(out var scene);
        ImportFold.Apply(state, scene.Id, new SceneMapResult
        {
            Items = new List<MappedItem>
            {
                new()
                {
                    SourceId = "w0", Type = DraftItemTypes.Episode,
                    Summary = "The Shower Protocol was invoked at last.", Weight = 0.9,
                    Concepts = new List<ConceptDraft> { new() { Name = "Shower Protocol" } },
                    SourceChunks = new List<int> { 1 },
                },
            },
            LlmCalls = 1,
        }, DateTimeOffset.UtcNow);

        var concept = Assert.Single(state.Concepts);
        Assert.Equal("Shower Protocol", concept.Name);
        Assert.False(concept.Confirmed);
    }
}
