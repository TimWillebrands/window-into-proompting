using System.Globalization;
using BackendTest.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace BackendTest;

/// <summary>
/// Integration test exercising <see cref="MemoryRepository.CaptureMomentAsync"/> end-to-end
/// against a live Postgres+AGE instance — the developer stack run by the Aspire AppHost.
/// LLM is stubbed by <see cref="FakeMemoryExtractor"/> so the test is deterministic; only
/// the persistence half is under test in slice 1.
/// </summary>
public sealed class MemoryRepositoryIntegrationTest(MemoryGraphFixture fixture)
    : IClassFixture<MemoryGraphFixture>
{
    [Fact]
    public async Task CaptureMoment_PersistsEvent_Concepts_AndRecollections()
    {
        if (!fixture.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Dev Postgres+AGE unavailable: {fixture.UnavailableReason}. " +
                "Start the Aspire AppHost (`dotnet run --project aspire/Proompting.AppHost`) and rerun.");
        }

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var messageId = unchecked((int)(DateTimeOffset.UtcNow.Ticks & 0x7FFFFFFF));
        var personaVlad = new ParticipantView(Guid.NewGuid(), "Vlad", Driver: DriverKind.LLM);
        var personaHana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var userBob = new ParticipantView(Guid.NewGuid(), "Bob", Driver: DriverKind.User);

        var conceptDisplay = $"Lisp-{Guid.NewGuid():N}";
        var conceptName = conceptDisplay.ToLowerInvariant();

        var sourceMessage = new ChatMessage
        {
            MessageId = messageId,
            Content = $"{conceptDisplay} is elegant once you get the hang of it.",
            SenderId = personaHana.Id,
            SenderType = "assistant",
            ChatGroupId = roomId,
        };

        var recentContext = new List<ChatMessage> { sourceMessage };

        var description = $"Hana defended {conceptDisplay} after Vlad's interruption.";

        var fake = new FakeMemoryExtractor(
            extraction: new EventExtraction(
                description,
                Concepts: new List<ConceptTag> { new(conceptName, conceptDisplay) },
                ParticipantIds: new List<Guid> { personaVlad.Id }),
            recollectionByPersona: new Dictionary<string, RecollectionDraft>
            {
                ["Vlad"] = new("you watched Hana double down on Lisp after you cut in", 0.3),
                ["Hana"] = new("you defended Lisp from Vlad's eye-roll", 0.8),
            });

        var repo = new MemoryRepository(fixture.Factory, fake, NullLogger<MemoryRepository>.Instance);

        var result = await repo.CaptureMomentAsync(
            partyId, roomId, messageId,
            presentParticipants: new[] { personaVlad, personaHana, userBob },
            recentContext: recentContext,
            ct: CancellationToken.None);

        Assert.True(result.EventCreated);
        Assert.Equal(2, result.RecollectionsCreated);
        Assert.Equal(1, result.ConceptsTouched);

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using (var load = conn.CreateCommand())
        {
            load.CommandText = "LOAD 'age';";
            await load.ExecuteNonQueryAsync();
        }

        var anchoredCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {party_id: '{{partyId}}', room_id: '{{roomId}}', anchor_message_id: {{messageId}}})
              RETURN count(e)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, anchoredCount);

        var conceptCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {party_id: '{{partyId}}', room_id: '{{roomId}}', anchor_message_id: {{messageId}}})-[:ABOUT]->(c:Concept {name: '{{conceptName}}'})
              RETURN count(c)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, conceptCount);

        var aboutVladCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {party_id: '{{partyId}}', room_id: '{{roomId}}', anchor_message_id: {{messageId}}})-[:ABOUT]->(p:Participant {persona_id: '{{personaVlad.Id}}', party_id: '{{partyId}}'})
              RETURN count(p)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, aboutVladCount);

        var recollectionCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {party_id: '{{partyId}}'})-[:RECOLLECTS]->(e:Event {room_id: '{{roomId}}', anchor_message_id: {{messageId}}})
              RETURN count(part)
            $cy$) AS (n int);
            """);
        Assert.Equal(2, recollectionCount);

        var userRecollectionCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{userBob.Id}}', party_id: '{{partyId}}'})-[:RECOLLECTS]->(e:Event)
              RETURN count(part)
            $cy$) AS (n int);
            """);
        Assert.Equal(0, userRecollectionCount);

        var hasParticipantCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (persona:Persona {id: '{{personaVlad.Id}}'})-[:HAS_PARTICIPANT]->(part:Participant {persona_id: '{{personaVlad.Id}}', party_id: '{{partyId}}'})
              RETURN count(persona)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, hasParticipantCount);

        var snippet = await QueryAgtypeStringAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{personaHana.Id}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(e:Event {room_id: '{{roomId}}', anchor_message_id: {{messageId}}})
              RETURN r.snippet
            $cy$) AS (s text);
            """);
        Assert.Equal("you defended Lisp from Vlad's eye-roll", snippet);

        // ADR 0015 salience substrate: every edge carries a stable uuid id, the weight the
        // extractor emitted, and recall_count 0. last_recalled stays absent until the
        // first pick (Cypher's null-is-absence).
        var (hanaEdgeId, hanaWeight, hanaRecallCount) = await QueryEdgeSubstrateAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{personaHana.Id}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(e:Event {room_id: '{{roomId}}', anchor_message_id: {{messageId}}})
              RETURN r.id, r.weight, r.recall_count
            $cy$) AS (id text, weight text, recall_count int);
            """);
        Assert.True(Guid.TryParse(hanaEdgeId, out var hanaEdgeGuid));
        Assert.NotEqual(Guid.Empty, hanaEdgeGuid);
        Assert.Equal(0.8, double.Parse(hanaWeight!, CultureInfo.InvariantCulture), precision: 6);
        Assert.Equal(0, hanaRecallCount);

        var vladWeight = await QueryAgtypeStringAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{personaVlad.Id}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(e:Event {room_id: '{{roomId}}', anchor_message_id: {{messageId}}})
              RETURN r.weight
            $cy$) AS (s text);
            """);
        Assert.Equal(0.3, double.Parse(vladWeight!, CultureInfo.InvariantCulture), precision: 6);

        var description2 = await QueryAgtypeStringAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {party_id: '{{partyId}}', room_id: '{{roomId}}', anchor_message_id: {{messageId}}})
              RETURN e.description
            $cy$) AS (s text);
            """);
        Assert.Equal(description, description2);
    }

    [Fact]
    public async Task GetPartyMemoryGraphAsync_EmptyParty_ReturnsEmpty()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var repo = new MemoryRepository(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);

        var graph = await repo.GetPartyMemoryGraphAsync(partyId, CancellationToken.None);

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Links);
    }

    [Fact]
    public async Task GetPartyMemoryGraphAsync_AfterCapture_ContainsExpectedNodesAndEdges()
    {
        RequireDevStack();

        var (partyId, roomId, messageId, vlad, hana, userBob, conceptName, conceptDisplay, snippetHana, snippetVlad)
            = await SeedFullCaptureAsync();

        var repo = new MemoryRepository(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);
        var graph = await repo.GetPartyMemoryGraphAsync(partyId, CancellationToken.None);

        // No Room/Message vertices: the anchor rides as properties on the Event node.
        Assert.DoesNotContain(graph.Nodes, n => n.Kind == "Room");
        Assert.DoesNotContain(graph.Nodes, n => n.Kind == "Message");
        var eventNode = Assert.Single(graph.Nodes, n => n.Kind == "Event");
        Assert.False(string.IsNullOrEmpty(eventNode.Description));
        Assert.Equal(roomId.ToString(), eventNode.RoomId);
        Assert.Equal(messageId, eventNode.AnchorMessageId);
        Assert.Contains(graph.Nodes, n => n.Id == $"concept:{conceptName}" && n.Kind == "Concept" && n.Display == conceptDisplay);
        Assert.Contains(graph.Nodes, n => n.Id == $"part:{vlad.Id}:{partyId}" && n.Kind == "Participant");
        Assert.Contains(graph.Nodes, n => n.Id == $"part:{hana.Id}:{partyId}" && n.Kind == "Participant");
        Assert.Contains(graph.Nodes, n => n.Id == $"persona:{vlad.Id}" && n.Kind == "Persona");
        Assert.Contains(graph.Nodes, n => n.Id == $"persona:{hana.Id}" && n.Kind == "Persona");

        // Silent user (no Recollections, not :ABOUT'd by any in-party Event) is invisible.
        Assert.DoesNotContain(graph.Nodes, n => n.Id.Contains(userBob.Id.ToString()));

        Assert.DoesNotContain(graph.Links, l => l.Kind == "ANCHORED_TO");
        Assert.Contains(graph.Links, l => l.Source == eventNode.Id && l.Target == $"concept:{conceptName}" && l.Kind == "ABOUT");
        Assert.Contains(graph.Links, l => l.Source == eventNode.Id && l.Target == $"part:{vlad.Id}:{partyId}" && l.Kind == "ABOUT");
        Assert.Contains(graph.Links, l => l.Source == $"part:{hana.Id}:{partyId}" && l.Target == eventNode.Id && l.Kind == "RECOLLECTS" && l.Snippet == snippetHana);
        Assert.Contains(graph.Links, l => l.Source == $"part:{vlad.Id}:{partyId}" && l.Target == eventNode.Id && l.Kind == "RECOLLECTS" && l.Snippet == snippetVlad);
        Assert.Contains(graph.Links, l => l.Source == $"persona:{hana.Id}" && l.Target == $"part:{hana.Id}:{partyId}" && l.Kind == "HAS_PARTICIPANT");
        Assert.Contains(graph.Links, l => l.Source == $"persona:{vlad.Id}" && l.Target == $"part:{vlad.Id}:{partyId}" && l.Kind == "HAS_PARTICIPANT");
    }

    [Fact]
    public async Task GetPartyMemoryGraphAsync_PartyIsolation()
    {
        RequireDevStack();

        var (partyA, roomA, _, vladA, _, _, conceptNameA, _, _, _) = await SeedFullCaptureAsync();
        var (partyB, roomB, _, _, _, _, _, _, _, _) = await SeedFullCaptureAsync();

        var repo = new MemoryRepository(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);
        var graphB = await repo.GetPartyMemoryGraphAsync(partyB, CancellationToken.None);

        // Party B's own Event is present; none of Party A's nodes leak across the party scope.
        Assert.Contains(graphB.Nodes, n => n.Kind == "Event" && n.RoomId == roomB.ToString());
        Assert.DoesNotContain(graphB.Nodes, n => n.Kind == "Event" && n.RoomId == roomA.ToString());
        Assert.DoesNotContain(graphB.Nodes, n => n.Id == $"part:{vladA.Id}:{partyA}");
        Assert.DoesNotContain(graphB.Nodes, n => n.Id == $"concept:{conceptNameA}");
    }

    [Fact]
    public async Task GetPartyMemoryGraphAsync_SharedConcept_AppearsInBothPartiesIndependently()
    {
        RequireDevStack();

        var sharedConceptDisplay = $"Lisp-{Guid.NewGuid():N}";
        var sharedConceptName = sharedConceptDisplay.ToLowerInvariant();

        var (partyA, _, _, _, _, _, _, _, _, _) = await SeedFullCaptureAsync(conceptName: sharedConceptName, conceptDisplay: sharedConceptDisplay);
        var (partyB, _, _, _, _, _, _, _, _, _) = await SeedFullCaptureAsync(conceptName: sharedConceptName, conceptDisplay: sharedConceptDisplay);

        var repo = new MemoryRepository(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);
        var graphA = await repo.GetPartyMemoryGraphAsync(partyA, CancellationToken.None);
        var graphB = await repo.GetPartyMemoryGraphAsync(partyB, CancellationToken.None);

        Assert.Contains(graphA.Nodes, n => n.Id == $"concept:{sharedConceptName}");
        Assert.Contains(graphB.Nodes, n => n.Id == $"concept:{sharedConceptName}");
    }

    private async Task<(Guid partyId, Guid roomId, int messageId,
        ParticipantView vlad, ParticipantView hana, ParticipantView userBob,
        string conceptName, string conceptDisplay,
        string snippetHana, string snippetVlad)> SeedFullCaptureAsync(string? conceptName = null, string? conceptDisplay = null)
    {
        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var messageId = unchecked((int)((DateTimeOffset.UtcNow.Ticks + Environment.TickCount) & 0x7FFFFFFF));
        var vlad = new ParticipantView(Guid.NewGuid(), "Vlad", Driver: DriverKind.LLM);
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var userBob = new ParticipantView(Guid.NewGuid(), "Bob", Driver: DriverKind.User);

        conceptDisplay ??= $"Lisp-{Guid.NewGuid():N}";
        conceptName ??= conceptDisplay.ToLowerInvariant();

        var snippetHana = $"hana-{Guid.NewGuid():N}";
        var snippetVlad = $"vlad-{Guid.NewGuid():N}";

        var source = new ChatMessage
        {
            MessageId = messageId,
            Content = "marker",
            SenderId = hana.Id,
            SenderType = "assistant",
            ChatGroupId = roomId,
        };

        var fake = new FakeMemoryExtractor(
            extraction: new EventExtraction(
                Description: $"Hana defended {conceptDisplay} after Vlad's interruption.",
                Concepts: new List<ConceptTag> { new(conceptName, conceptDisplay) },
                ParticipantIds: new List<Guid> { vlad.Id }),
            recollectionByPersona: new Dictionary<string, RecollectionDraft>
            {
                ["Vlad"] = new(snippetVlad, 0.5),
                ["Hana"] = new(snippetHana, 0.5),
            });

        var repo = new MemoryRepository(fixture.Factory, fake, NullLogger<MemoryRepository>.Instance);
        await repo.CaptureMomentAsync(partyId, roomId, messageId,
            presentParticipants: new[] { vlad, hana, userBob },
            recentContext: new[] { source },
            ct: CancellationToken.None);

        return (partyId, roomId, messageId, vlad, hana, userBob, conceptName, conceptDisplay, snippetHana, snippetVlad);
    }

    private void RequireDevStack()
    {
        if (!fixture.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Dev Postgres+AGE unavailable: {fixture.UnavailableReason}. " +
                "Start the Aspire AppHost (`dotnet run --project aspire/Proompting.AppHost`) and rerun.");
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

    [Fact]
    public async Task Recall_ReturnsSnippetsCapturedForPersona()
    {
        if (!fixture.IsAvailable)
        {
            throw new InvalidOperationException(
                $"Dev Postgres+AGE unavailable: {fixture.UnavailableReason}. " +
                "Start the Aspire AppHost (`dotnet run --project aspire/Proompting.AppHost`) and rerun.");
        }

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var messageId = unchecked((int)(DateTimeOffset.UtcNow.Ticks & 0x7FFFFFFF));
        var personaHana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var personaVlad = new ParticipantView(Guid.NewGuid(), "Vlad", Driver: DriverKind.LLM);

        const string hanaSnippet = "you defended Lisp from Vlad's eye-roll";
        const string vladSnippet = "you watched Hana double down on Lisp after you cut in";

        var sourceMessage = new ChatMessage
        {
            MessageId = messageId,
            Content = "Lisp is elegant once you get the hang of it.",
            SenderId = personaHana.Id,
            SenderType = "assistant",
            ChatGroupId = roomId,
        };

        var fake = new FakeMemoryExtractor(
            extraction: new EventExtraction(
                "Hana defended Lisp after Vlad's interruption.",
                Concepts: new List<ConceptTag>(),
                ParticipantIds: new List<Guid>()),
            recollectionByPersona: new Dictionary<string, RecollectionDraft>
            {
                ["Hana"] = new(hanaSnippet, 0.7),
                ["Vlad"] = new(vladSnippet, 0.4),
            });

        var repo = new MemoryRepository(fixture.Factory, fake, NullLogger<MemoryRepository>.Instance);

        var capture = await repo.CaptureMomentAsync(
            partyId, roomId, messageId,
            presentParticipants: new[] { personaHana, personaVlad },
            recentContext: new[] { sourceMessage },
            ct: CancellationToken.None);

        Assert.True(capture.EventCreated);
        Assert.Equal(2, capture.RecollectionsCreated);

        var hanaRecall = await repo.RecallAsync(personaHana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 5, CancellationToken.None);
        var vladRecall = await repo.RecallAsync(personaVlad.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 5, CancellationToken.None);

        var hanaSnippets = hanaRecall.Select(m => m.Snippet).ToList();
        var vladSnippets = vladRecall.Select(m => m.Snippet).ToList();
        Assert.Contains(hanaSnippet, hanaSnippets);
        Assert.Contains(vladSnippet, vladSnippets);
        Assert.DoesNotContain(vladSnippet, hanaSnippets);
        Assert.DoesNotContain(hanaSnippet, vladSnippets);

        // Recall carries the substrate through: weight round-trips, the edge id is the
        // strengthening key, and a just-captured memory scores ≈ its weight (decay ≈ 1).
        var hanaMemory = hanaRecall.Single(m => m.Snippet == hanaSnippet);
        Assert.NotEqual(Guid.Empty, hanaMemory.EdgeId);
        Assert.Equal(0.7, hanaMemory.Weight, precision: 6);
        Assert.Equal(0, hanaMemory.RecallCount);
        Assert.Null(hanaMemory.LastRecalled);
        Assert.InRange(hanaMemory.Salience, 0.65, 0.7);
    }

    [Fact]
    public async Task Recall_RanksBySalience_WeightAndDecayAndUseBonus()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);

        var repo = MakeRepo();

        // Two captures for the same persona: a heavy one and a light one.
        var heavySnippet = $"heavy-{Guid.NewGuid():N}";
        var lightSnippet = $"light-{Guid.NewGuid():N}";
        await CaptureForAsync(repo, partyId, roomId, hana, heavySnippet, weight: 0.9);
        await CaptureForAsync(repo, partyId, roomId, hana, lightSnippet, weight: 0.2);

        // Fresh + heavy outranks fresh + light: salience ≈ weight when decay ≈ 1.
        var ranked = await repo.RecallAsync(hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        Assert.Equal(heavySnippet, ranked[0].Snippet);
        Assert.Equal(lightSnippet, ranked[1].Snippet);

        // Backdate the heavy memory by 30 days (4+ half-lives): 0.9 × 0.5^(30/7) ≈ 0.05,
        // so the fresh light memory (0.2) now wins — an old high-weight memory ranks below
        // a fresh one.
        var heavyEdgeId = ranked[0].EdgeId;
        var backdatedIso = DateTimeOffset.UtcNow.AddDays(-30).ToString("o");
        await ExecuteCypherAsync($$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (:Participant)-[r:RECOLLECTS {id: '{{heavyEdgeId}}'}]->(:Event)
              SET r.ts = '{{backdatedIso}}'
              RETURN r.id
            $cy$) AS (id ag_catalog.agtype);
            """);

        var afterDecay = await repo.RecallAsync(hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        Assert.Equal(lightSnippet, afterDecay[0].Snippet);
        Assert.Equal(heavySnippet, afterDecay[1].Snippet);

        // Strengthen the decayed memory (several picks, just recalled): the use bonus
        // (≈ 0.15·ln(1+n), undecayed) lifts it back above the fresh light one.
        for (var i = 0; i < 5; i++)
        {
            await repo.StrengthenRecollectionAsync(heavyEdgeId, CancellationToken.None);
        }

        var afterStrengthening = await repo.RecallAsync(hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        Assert.Equal(heavySnippet, afterStrengthening[0].Snippet);
        Assert.True(afterStrengthening[0].RecallCount == 5);
        Assert.NotNull(afterStrengthening[0].LastRecalled);
    }

    [Fact]
    public async Task Strengthen_UpdatesPickedEdgeOnly()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);

        var repo = MakeRepo();
        var pickedSnippet = $"picked-{Guid.NewGuid():N}";
        var bystanderSnippet = $"bystander-{Guid.NewGuid():N}";
        await CaptureForAsync(repo, partyId, roomId, hana, pickedSnippet, weight: 0.5);
        await CaptureForAsync(repo, partyId, roomId, hana, bystanderSnippet, weight: 0.5);

        var before = await repo.RecallAsync(hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        var picked = before.Single(m => m.Snippet == pickedSnippet);

        await repo.StrengthenRecollectionAsync(picked.EdgeId, CancellationToken.None);

        var after = await repo.RecallAsync(hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        var strengthened = after.Single(m => m.Snippet == pickedSnippet);
        var bystander = after.Single(m => m.Snippet == bystanderSnippet);

        // The pick strengthened exactly one edge: count bumped, last_recalled stamped.
        Assert.Equal(1, strengthened.RecallCount);
        Assert.NotNull(strengthened.LastRecalled);
        Assert.InRange(
            strengthened.LastRecalled!.Value,
            DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));

        // Mere surfacing (the recalls above) did NOT strengthen the bystander.
        Assert.Equal(0, bystander.RecallCount);
        Assert.Null(bystander.LastRecalled);

        // Unknown id: silent no-op, not an exception.
        await repo.StrengthenRecollectionAsync(Guid.NewGuid(), CancellationToken.None);
    }

    // ─── Two-arm recall: quota union of relevant + recent (ADR 0015, #88) ─────────────────

    [Fact]
    public async Task Recall_RelevantArm_SurfacesCastAnchoredMemory_BuriedUnderRecent()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var vladId = Guid.NewGuid();

        var repo = MakeRepo();

        // One memory whose Event is :ABOUT Vlad, then ten heavier unrelated memories on
        // top — recency-only recall at limit 10 can never surface the buried one.
        var buriedSnippet = $"buried-about-vlad-{Guid.NewGuid():N}";
        await CaptureForAsync(repo, partyId, roomId, hana, buriedSnippet, weight: 0.5,
            aboutParticipantIds: new[] { vladId });
        for (var i = 0; i < 10; i++)
        {
            await CaptureForAsync(repo, partyId, roomId, hana, $"filler-{i}-{Guid.NewGuid():N}", weight: 0.6);
        }

        var withoutAnchors = await repo.RecallAsync(
            hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        Assert.DoesNotContain(buriedSnippet, withoutAnchors.Select(m => m.Snippet));

        // Vlad enters the cast: the graph walk surfaces the buried memory through the
        // relevant arm regardless of how deep recency buried it.
        var withCast = await repo.RecallAsync(
            hana.Id, partyId, new[] { hana.Id, vladId }, "completely unrelated chatter",
            limit: 10, CancellationToken.None);
        var surfaced = Assert.Single(withCast, m => m.Snippet == buriedSnippet);
        Assert.Equal(RecallArm.Relevant, surfaced.Arm);
        Assert.Equal(10, withCast.Count);
    }

    [Fact]
    public async Task Recall_RelevantArm_ConceptTokenMatch_SurfacesBuriedMemory()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);

        var repo = MakeRepo();

        // Unique single-token concept name (normalised = lowercase) so the triggering
        // message's token matches exactly one Concept anchor.
        var conceptDisplay = $"Zorblax{Guid.NewGuid():N}";
        var conceptName = conceptDisplay.ToLowerInvariant();

        var buriedSnippet = $"buried-concept-{Guid.NewGuid():N}";
        await CaptureForAsync(repo, partyId, roomId, hana, buriedSnippet, weight: 0.5,
            concept: new ConceptTag(conceptName, conceptDisplay));
        for (var i = 0; i < 10; i++)
        {
            await CaptureForAsync(repo, partyId, roomId, hana, $"filler-{i}-{Guid.NewGuid():N}", weight: 0.6);
        }

        var withoutAnchors = await repo.RecallAsync(
            hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        Assert.DoesNotContain(buriedSnippet, withoutAnchors.Select(m => m.Snippet));

        // The triggering message names the concept (different casing — tokenisation
        // lowercases to match the normalised Concept name).
        var withConcept = await repo.RecallAsync(
            hana.Id, partyId, new[] { hana.Id }, $"so what about {conceptDisplay} then?",
            limit: 10, CancellationToken.None);
        var surfaced = Assert.Single(withConcept, m => m.Snippet == buriedSnippet);
        Assert.Equal(RecallArm.Relevant, surfaced.Arm);
    }

    [Fact]
    public async Task Recall_ColdBeat_IdenticalToRecencyOnlyRecall()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);

        var repo = MakeRepo();
        for (var i = 0; i < 12; i++)
        {
            await CaptureForAsync(repo, partyId, roomId, hana, $"mem-{i}-{Guid.NewGuid():N}",
                weight: 0.2 + (i % 7) * 0.1);
        }

        var recencyOnly = await repo.RecallAsync(
            hana.Id, partyId, Array.Empty<Guid>(), string.Empty, limit: 10, CancellationToken.None);
        // Cast present + message text, but no Event is :ABOUT anyone and no Concept matches:
        // the relevant arm comes back empty and recency fills all slots — identical output.
        var coldBeat = await repo.RecallAsync(
            hana.Id, partyId, new[] { hana.Id, Guid.NewGuid() }, "wholly unanchored rambling",
            limit: 10, CancellationToken.None);

        Assert.Equal(10, recencyOnly.Count);
        Assert.Equal(recencyOnly.Select(m => m.EdgeId), coldBeat.Select(m => m.EdgeId));
        Assert.All(coldBeat, m => Assert.Equal(RecallArm.Recent, m.Arm));
    }

    [Fact]
    public async Task Recall_MemoryQualifyingViaBothArms_SurfacesOnce()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var hana = new ParticipantView(Guid.NewGuid(), "Hana", Driver: DriverKind.LLM);
        var vladId = Guid.NewGuid();

        var repo = MakeRepo();

        var conceptDisplay = $"Zorblax{Guid.NewGuid():N}";
        var conceptName = conceptDisplay.ToLowerInvariant();

        // Recent (only 2 memories, both inside the recency pool) AND anchored via BOTH
        // relevant legs (cast member + concept) — the union must still surface it once.
        var doubleSnippet = $"double-{Guid.NewGuid():N}";
        await CaptureForAsync(repo, partyId, roomId, hana, doubleSnippet, weight: 0.5,
            aboutParticipantIds: new[] { vladId },
            concept: new ConceptTag(conceptName, conceptDisplay));
        var plainSnippet = $"plain-{Guid.NewGuid():N}";
        await CaptureForAsync(repo, partyId, roomId, hana, plainSnippet, weight: 0.5);

        var recall = await repo.RecallAsync(
            hana.Id, partyId, new[] { hana.Id, vladId }, $"tell me about {conceptDisplay}",
            limit: 10, CancellationToken.None);

        Assert.Equal(2, recall.Count);
        Assert.Equal(2, recall.Select(m => m.EdgeId).Distinct().Count());
        var doubled = Assert.Single(recall, m => m.Snippet == doubleSnippet);
        Assert.Equal(RecallArm.Relevant, doubled.Arm);
        var plain = Assert.Single(recall, m => m.Snippet == plainSnippet);
        Assert.Equal(RecallArm.Recent, plain.Arm);
    }

    [Fact]
    public void TokenizeAnchorText_LowercasesSplitsAndBigrams()
    {
        var tokens = MemoryRepository.TokenizeAnchorText("What about Common Lisp, then?");

        Assert.Contains("common", tokens);
        Assert.Contains("lisp", tokens);
        // Adjacent-pair bigram catches two-word normalised Concept names.
        Assert.Contains("common lisp", tokens);
        // Single-letter noise is dropped from unigrams.
        Assert.DoesNotContain("a", tokens);

        Assert.Empty(MemoryRepository.TokenizeAnchorText(null));
        Assert.Empty(MemoryRepository.TokenizeAnchorText("   "));
    }

    // ─── Stance floor (ADR 0016) ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AppendStance_TowardParticipantConceptAndSelf_ListsAllWithKinds()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var author = Guid.NewGuid();
        var vlad = Guid.NewGuid();
        var conceptDisplay = $"Lisp-{Guid.NewGuid():N}";
        var conceptName = conceptDisplay.ToLowerInvariant();

        var repo = MakeRepo();

        var participantId = await repo.AppendStanceAsync(
            partyId, author, new StanceTargetSpec(StanceTargetKind.Participant, vlad, null, null),
            valence: -0.6, reasoning: "Vlad talks over people", attribution: null, CancellationToken.None);
        var conceptId = await repo.AppendStanceAsync(
            partyId, author, new StanceTargetSpec(StanceTargetKind.Concept, null, conceptName, conceptDisplay),
            valence: 0.8, reasoning: "Lisp is elegant once it clicks", attribution: null, CancellationToken.None);
        var selfId = await repo.AppendStanceAsync(
            partyId, author, new StanceTargetSpec(StanceTargetKind.Self, null, null, null),
            valence: 0.3, reasoning: "you hold your ground when challenged", attribution: null, CancellationToken.None);

        var stances = await repo.ListStancesAsync(partyId, author, CancellationToken.None);

        var p = Assert.Single(stances, s => s.Id == participantId);
        Assert.Equal(StanceTargetKind.Participant, p.TargetKind);
        Assert.Equal(vlad, p.TargetPersonaId);
        Assert.Equal(-0.6, p.Valence, precision: 6);
        Assert.True(p.IsCurrent);

        var c = Assert.Single(stances, s => s.Id == conceptId);
        Assert.Equal(StanceTargetKind.Concept, c.TargetKind);
        Assert.Equal(conceptName, c.TargetConceptName);
        Assert.Equal(conceptDisplay, c.TargetConceptDisplay);

        var self = Assert.Single(stances, s => s.Id == selfId);
        Assert.Equal(StanceTargetKind.Self, self.TargetKind);
        Assert.Equal(author, self.TargetPersonaId);
    }

    [Fact]
    public async Task AppendStance_SecondTowardSameTarget_LatestWins_HistoryPreserved()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var author = Guid.NewGuid();
        var vlad = Guid.NewGuid();
        var present = new[] { author, vlad };

        var repo = MakeRepo();

        var firstId = await repo.AppendStanceAsync(
            partyId, author, new StanceTargetSpec(StanceTargetKind.Participant, vlad, null, null),
            valence: 0.7, reasoning: "you admire how Vlad tells a story", attribution: null, CancellationToken.None);
        // ts is ISO-8601 with tick precision; a small gap guarantees a strict ordering.
        await Task.Delay(5);
        var secondId = await repo.AppendStanceAsync(
            partyId, author, new StanceTargetSpec(StanceTargetKind.Participant, vlad, null, null),
            valence: -0.7, reasoning: "Vlad exaggerates — you've caught him inflating three stories", attribution: null,
            CancellationToken.None);

        Assert.NotEqual(firstId, secondId);

        // History preserved: both edges queryable; only the latest flagged current.
        var all = await repo.ListStancesAsync(partyId, author, CancellationToken.None);
        var towardVlad = all.Where(s => s.TargetPersonaId == vlad).ToList();
        Assert.Equal(2, towardVlad.Count);
        Assert.True(towardVlad.Single(s => s.Id == secondId).IsCurrent);
        Assert.False(towardVlad.Single(s => s.Id == firstId).IsCurrent);

        // Latest-wins read: the second reasoning is what surfaces; the first does not.
        var lines = await repo.RecallStancesAsync(
            partyId, author, present, anchorText: "anything", limit: 5, CancellationToken.None);
        var towardVladLine = Assert.Single(lines, l => l.Reasoning.Contains("Vlad", StringComparison.Ordinal));
        Assert.Contains("exaggerates", towardVladLine.Reasoning);
        Assert.Equal(-0.7, towardVladLine.Valence, precision: 6);
    }

    [Fact]
    public async Task RecallStances_OnlyAnchoredTargetsRender()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var author = Guid.NewGuid();
        var present = Guid.NewGuid();
        var absent = Guid.NewGuid();
        var liveConcept = $"Lisp-{Guid.NewGuid():N}";
        var deadConcept = $"Forth-{Guid.NewGuid():N}";

        var repo = MakeRepo();

        await repo.AppendStanceAsync(partyId, author,
            new StanceTargetSpec(StanceTargetKind.Participant, present, null, null),
            0.5, "you trust the present one", attribution: null, CancellationToken.None);
        await repo.AppendStanceAsync(partyId, author,
            new StanceTargetSpec(StanceTargetKind.Participant, absent, null, null),
            0.5, "you miss the absent one", attribution: null, CancellationToken.None);
        await repo.AppendStanceAsync(partyId, author,
            new StanceTargetSpec(StanceTargetKind.Concept, null, liveConcept.ToLowerInvariant(), liveConcept),
            0.5, "live concept is elegant", attribution: null, CancellationToken.None);
        await repo.AppendStanceAsync(partyId, author,
            new StanceTargetSpec(StanceTargetKind.Concept, null, deadConcept.ToLowerInvariant(), deadConcept),
            0.5, "dead concept is clunky", attribution: null, CancellationToken.None);

        // Present cast = author + present participant; triggering message names only liveConcept.
        var lines = await repo.RecallStancesAsync(
            partyId, author, new[] { author, present },
            anchorText: $"what do you think of {liveConcept} today?",
            limit: 5, CancellationToken.None);

        var reasons = lines.Select(l => l.Reasoning).ToList();
        Assert.Contains("you trust the present one", reasons);
        Assert.Contains("live concept is elegant", reasons);
        Assert.DoesNotContain("you miss the absent one", reasons);
        Assert.DoesNotContain("dead concept is clunky", reasons);
    }

    [Fact]
    public async Task RecallStances_CapsAtLimit_StrongestFirst()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var author = Guid.NewGuid();

        var repo = MakeRepo();

        // Seven distinct present participants, ascending |valence|, so the cap must keep the
        // strongest-felt and drop the weakest.
        var present = new List<Guid> { author };
        for (var i = 1; i <= 7; i++)
        {
            var target = Guid.NewGuid();
            present.Add(target);
            await repo.AppendStanceAsync(partyId, author,
                new StanceTargetSpec(StanceTargetKind.Participant, target, null, null),
                valence: i / 10.0, reasoning: $"stance-strength-{i}", attribution: null, CancellationToken.None);
        }

        var lines = await repo.RecallStancesAsync(
            partyId, author, present, anchorText: "n/a", limit: 5, CancellationToken.None);

        Assert.Equal(5, lines.Count);
        Assert.Equal("stance-strength-7", lines[0].Reasoning);
        Assert.DoesNotContain(lines, l => l.Reasoning is "stance-strength-1" or "stance-strength-2");
    }

    private MemoryRepository MakeRepo()
        => new(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);

    /// <summary>
    /// Capture one Event whose sole Recollection lands on <paramref name="participant"/>
    /// with the given snippet/weight — the minimal seed for recall-ranking tests.
    /// Optional <paramref name="aboutParticipantIds"/> / <paramref name="concept"/> hang
    /// :ABOUT anchors off the Event for the relevant-arm walk.
    /// </summary>
    private async Task CaptureForAsync(
        MemoryRepository _, Guid partyId, Guid roomId, ParticipantView participant,
        string snippet, double weight,
        IReadOnlyList<Guid>? aboutParticipantIds = null,
        ConceptTag? concept = null)
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
        var fake = new FakeMemoryExtractor(
            extraction: new EventExtraction(
                Description: $"Moment for {snippet}.",
                Concepts: concept is null ? new List<ConceptTag>() : new List<ConceptTag> { concept },
                ParticipantIds: aboutParticipantIds?.ToList() ?? new List<Guid>()),
            recollectionByPersona: new Dictionary<string, RecollectionDraft>
            {
                [participant.Name] = new(snippet, weight),
            });
        var repo = new MemoryRepository(fixture.Factory, fake, NullLogger<MemoryRepository>.Instance);
        var result = await repo.CaptureMomentAsync(
            partyId, roomId, messageId,
            presentParticipants: new[] { participant },
            recentContext: new[] { source },
            ct: CancellationToken.None);
        Assert.True(result.EventCreated);
        Assert.Equal(1, result.RecollectionsCreated);
    }

    private async Task ExecuteCypherAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using (var load = conn.CreateCommand())
        {
            load.CommandText = "LOAD 'age';";
            await load.ExecuteNonQueryAsync();
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    // Each caller types the cypher() output column as `int` / `text` (instead of `agtype`)
    // so Postgres applies AGE's agtype output cast — unquoting/unescaping string scalars —
    // and Npgsql reads through its built-in converters. Reading agtype directly throws
    // InvalidCastException; no default reader is registered for ag_catalog.agtype.
    private static async Task<int> QueryAgtypeIntAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync();
        return raw is null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> QueryAgtypeStringAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync();
        return raw as string;
    }

    // One round-trip for the ADR 0015 substrate triple (id, weight, recall_count) so the
    // MATCH clause isn't repeated per property.
    private static async Task<(string? Id, string? Weight, int RecallCount)> QueryEdgeSubstrateAsync(
        NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Expected exactly one RECOLLECTS edge row");
        return (
            reader.IsDBNull(0) ? null : reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? 0 : reader.GetInt32(2));
    }

    private sealed class FakeMemoryExtractor(
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
            // Map the by-name fixture data back onto each target's PersonaId, mirroring the
            // production extractor's name-keyed JSON → PersonaId resolution.
            var result = new Dictionary<Guid, RecollectionDraft>();
            foreach (var t in targets)
            {
                if (recollectionByPersona.TryGetValue(t.Name, out var d) && !string.IsNullOrWhiteSpace(d.Snippet))
                {
                    result[t.PersonaId] = d;
                }
            }
            return Task.FromResult<IReadOnlyDictionary<Guid, RecollectionDraft>>(result);
        }
    }
}
