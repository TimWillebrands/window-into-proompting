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
            recollectionByPersona: new Dictionary<string, string>
            {
                ["Vlad"] = "you watched Hana double down on Lisp after you cut in",
                ["Hana"] = "you defended Lisp from Vlad's eye-roll",
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
              MATCH (e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}})
              RETURN count(e)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, anchoredCount);

        var conceptCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}}),
                    (e)-[:ABOUT]->(c:Concept {name: '{{conceptName}}'})
              RETURN count(c)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, conceptCount);

        var aboutVladCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}}),
                    (e)-[:ABOUT]->(p:Participant {persona_id: '{{personaVlad.Id}}', party_id: '{{partyId}}'})
              RETURN count(p)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, aboutVladCount);

        var recollectionCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {party_id: '{{partyId}}'})-[:RECOLLECTS]->(e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}})
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
              MATCH (persona:Persona {id: '{{personaVlad.Id}}'})-[:HAS_PARTICIPANT]->(part:Participant {persona_id: '{{personaVlad.Id}}', party_id: '{{partyId}}'})-[:IN_PARTY]->(party:Party {id: '{{partyId}}'})
              RETURN count(persona)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, hasParticipantCount);

        var snippet = await QueryAgtypeStringAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{personaHana.Id}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}})
              RETURN r.snippet
            $cy$) AS (s text);
            """);
        Assert.Equal("you defended Lisp from Vlad's eye-roll", snippet);

        var description2 = await QueryAgtypeStringAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}})
              RETURN e.description
            $cy$) AS (s text);
            """);
        Assert.Equal(description, description2);
    }

    [Fact]
    public async Task EnsureRoomAsync_IsIdempotent_AndOverwritesPartyId()
    {
        RequireDevStack();

        var partyA = Guid.NewGuid();
        var partyB = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var repo = new MemoryRepository(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);

        // Idempotent: two calls with the same (party, room) → one Room node, party_id stays put.
        await repo.EnsureRoomAsync(partyA, roomId, CancellationToken.None);
        await repo.EnsureRoomAsync(partyA, roomId, CancellationToken.None);

        await using var conn = await OpenAgeAsync();

        var roomCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (r:Room {id: '{{roomId}}'})
              RETURN count(r)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, roomCount);

        var partyACount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (r:Room {id: '{{roomId}}', party_id: '{{partyA}}'})
              RETURN count(r)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, partyACount);

        // Overwrite semantics: a Room belongs to exactly one Party — re-tagging with a
        // new partyId moves it; the previous tag does not linger.
        await repo.EnsureRoomAsync(partyB, roomId, CancellationToken.None);

        var partyBCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (r:Room {id: '{{roomId}}', party_id: '{{partyB}}'})
              RETURN count(r)
            $cy$) AS (n int);
            """);
        Assert.Equal(1, partyBCount);

        var stillTaggedA = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (r:Room {id: '{{roomId}}', party_id: '{{partyA}}'})
              RETURN count(r)
            $cy$) AS (n int);
            """);
        Assert.Equal(0, stillTaggedA);
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
    public async Task GetPartyMemoryGraphAsync_EmptyRoom_IncludesRoomNodeOnly()
    {
        RequireDevStack();

        var partyId = Guid.NewGuid();
        var roomId = Guid.NewGuid();
        var repo = new MemoryRepository(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);

        await repo.EnsureRoomAsync(partyId, roomId, CancellationToken.None);

        var graph = await repo.GetPartyMemoryGraphAsync(partyId, CancellationToken.None);

        Assert.Single(graph.Nodes);
        Assert.Equal("Room", graph.Nodes[0].Kind);
        Assert.Equal($"room:{roomId}", graph.Nodes[0].Id);
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

        Assert.Contains(graph.Nodes, n => n.Id == $"room:{roomId}" && n.Kind == "Room");
        Assert.Contains(graph.Nodes, n => n.Id == $"msg:{roomId}:{messageId}" && n.Kind == "Message");
        var eventNode = Assert.Single(graph.Nodes, n => n.Kind == "Event");
        Assert.False(string.IsNullOrEmpty(eventNode.Description));
        Assert.Contains(graph.Nodes, n => n.Id == $"concept:{conceptName}" && n.Kind == "Concept" && n.Display == conceptDisplay);
        Assert.Contains(graph.Nodes, n => n.Id == $"part:{vlad.Id}:{partyId}" && n.Kind == "Participant");
        Assert.Contains(graph.Nodes, n => n.Id == $"part:{hana.Id}:{partyId}" && n.Kind == "Participant");
        Assert.Contains(graph.Nodes, n => n.Id == $"persona:{vlad.Id}" && n.Kind == "Persona");
        Assert.Contains(graph.Nodes, n => n.Id == $"persona:{hana.Id}" && n.Kind == "Persona");

        // Silent user (no Recollections, not :ABOUT'd by any in-party Event) is invisible.
        Assert.DoesNotContain(graph.Nodes, n => n.Id.Contains(userBob.Id.ToString()));

        Assert.Contains(graph.Links, l => l.Source == eventNode.Id && l.Target == $"msg:{roomId}:{messageId}" && l.Kind == "ANCHORED_TO");
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

        var (partyA, roomA, _, _, _, _, conceptName, _, _, _) = await SeedFullCaptureAsync();

        var partyB = Guid.NewGuid();
        var roomB = Guid.NewGuid();
        var repo = new MemoryRepository(fixture.Factory, NullExtractor.Instance, NullLogger<MemoryRepository>.Instance);
        await repo.EnsureRoomAsync(partyB, roomB, CancellationToken.None);

        var graphB = await repo.GetPartyMemoryGraphAsync(partyB, CancellationToken.None);

        // Only Party B's empty Room is visible — none of Party A's nodes or the shared Concept.
        Assert.Single(graphB.Nodes);
        Assert.Equal($"room:{roomB}", graphB.Nodes[0].Id);
        Assert.DoesNotContain(graphB.Nodes, n => n.Id == $"room:{roomA}");
        Assert.DoesNotContain(graphB.Nodes, n => n.Id == $"concept:{conceptName}");
        Assert.Empty(graphB.Links);
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
            recollectionByPersona: new Dictionary<string, string>
            {
                ["Vlad"] = snippetVlad,
                ["Hana"] = snippetHana,
            });

        var repo = new MemoryRepository(fixture.Factory, fake, NullLogger<MemoryRepository>.Instance);
        await repo.EnsureRoomAsync(partyId, roomId, CancellationToken.None);
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

    private async Task<NpgsqlConnection> OpenAgeAsync()
    {
        var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var load = conn.CreateCommand();
        load.CommandText = "LOAD 'age';";
        await load.ExecuteNonQueryAsync();
        return conn;
    }

    private sealed class NullExtractor : IMemoryExtractor
    {
        public static readonly NullExtractor Instance = new();

        public Task<EventExtraction?> ExtractEventAsync(
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            IReadOnlyList<ParticipantView> presentParticipants,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken) => Task.FromResult<EventExtraction?>(null);

        public Task<string> ExtractRecollectionAsync(
            string personaName,
            bool isSpeaker,
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken) => Task.FromResult(string.Empty);
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
            recollectionByPersona: new Dictionary<string, string>
            {
                ["Hana"] = hanaSnippet,
                ["Vlad"] = vladSnippet,
            });

        var repo = new MemoryRepository(fixture.Factory, fake, NullLogger<MemoryRepository>.Instance);

        var capture = await repo.CaptureMomentAsync(
            partyId, roomId, messageId,
            presentParticipants: new[] { personaHana, personaVlad },
            recentContext: new[] { sourceMessage },
            ct: CancellationToken.None);

        Assert.True(capture.EventCreated);
        Assert.Equal(2, capture.RecollectionsCreated);

        var hanaRecall = await repo.RecallRecentSnippetsAsync(personaHana.Id, partyId, limit: 5, CancellationToken.None);
        var vladRecall = await repo.RecallRecentSnippetsAsync(personaVlad.Id, partyId, limit: 5, CancellationToken.None);

        Assert.Contains(hanaSnippet, hanaRecall);
        Assert.Contains(vladSnippet, hanaRecall.Concat(vladRecall).ToList());
        Assert.DoesNotContain(vladSnippet, hanaRecall);
        Assert.DoesNotContain(hanaSnippet, vladRecall);
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

    private sealed class FakeMemoryExtractor(
        EventExtraction? extraction,
        IReadOnlyDictionary<string, string> recollectionByPersona) : IMemoryExtractor
    {
        public Task<EventExtraction?> ExtractEventAsync(
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            IReadOnlyList<ParticipantView> presentParticipants,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken) => Task.FromResult(extraction);

        public Task<string> ExtractRecollectionAsync(
            string personaName,
            bool isSpeaker,
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken)
            => Task.FromResult(recollectionByPersona.TryGetValue(personaName, out var s) ? s : "");
    }
}
