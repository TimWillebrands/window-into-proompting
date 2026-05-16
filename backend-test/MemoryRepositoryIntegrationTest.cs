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
        var personaVlad = new ParticipantSnapshot(Guid.NewGuid(), "Vlad", IsUser: false);
        var personaHana = new ParticipantSnapshot(Guid.NewGuid(), "Hana", IsUser: false);
        var userBob = new ParticipantSnapshot(Guid.NewGuid(), "Bob", IsUser: true);

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
            $cy$) AS (n ag_catalog.agtype);
            """);
        Assert.Equal(1, anchoredCount);

        var conceptCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}}),
                    (e)-[:ABOUT]->(c:Concept {name: '{{conceptName}}'})
              RETURN count(c)
            $cy$) AS (n ag_catalog.agtype);
            """);
        Assert.Equal(1, conceptCount);

        var aboutVladCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}}),
                    (e)-[:ABOUT]->(p:Participant {persona_id: '{{personaVlad.Id}}', party_id: '{{partyId}}'})
              RETURN count(p)
            $cy$) AS (n ag_catalog.agtype);
            """);
        Assert.Equal(1, aboutVladCount);

        var recollectionCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {party_id: '{{partyId}}'})-[:RECOLLECTS]->(e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}})
              RETURN count(part)
            $cy$) AS (n ag_catalog.agtype);
            """);
        Assert.Equal(2, recollectionCount);

        var userRecollectionCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{userBob.Id}}', party_id: '{{partyId}}'})-[:RECOLLECTS]->(e:Event)
              RETURN count(part)
            $cy$) AS (n ag_catalog.agtype);
            """);
        Assert.Equal(0, userRecollectionCount);

        var hasParticipantCount = await QueryAgtypeIntAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (persona:Persona {id: '{{personaVlad.Id}}'})-[:HAS_PARTICIPANT]->(part:Participant {persona_id: '{{personaVlad.Id}}', party_id: '{{partyId}}'})-[:IN_PARTY]->(party:Party {id: '{{partyId}}'})
              RETURN count(persona)
            $cy$) AS (n ag_catalog.agtype);
            """);
        Assert.Equal(1, hasParticipantCount);

        var snippet = await QueryAgtypeStringAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{personaHana.Id}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}})
              RETURN r.snippet
            $cy$) AS (s ag_catalog.agtype);
            """);
        Assert.Equal("you defended Lisp from Vlad's eye-roll", snippet);

        var description2 = await QueryAgtypeStringAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event)-[:ANCHORED_TO]->(m:Message {room_id: '{{roomId}}', id: {{messageId}}})
              RETURN e.description
            $cy$) AS (s ag_catalog.agtype);
            """);
        Assert.Equal(description, description2);
    }

    private static async Task<int> QueryAgtypeIntAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync();
        if (raw is null) return 0;
        var text = raw.ToString() ?? "";
        text = text.Trim().Trim('"');
        return int.Parse(text, CultureInfo.InvariantCulture);
    }

    private static async Task<string?> QueryAgtypeStringAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var raw = await cmd.ExecuteScalarAsync();
        if (raw is null) return null;
        var text = raw.ToString() ?? "";
        text = text.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            // agtype quotes string values; un-quote and unescape standard JSON escapes.
            text = System.Text.Json.JsonSerializer.Deserialize<string>(text) ?? "";
        }
        return text;
    }

    private sealed class FakeMemoryExtractor(
        EventExtraction? extraction,
        IReadOnlyDictionary<string, string> recollectionByPersona) : IMemoryExtractor
    {
        public Task<EventExtraction?> ExtractEventAsync(
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            IReadOnlyList<ParticipantSnapshot> presentParticipants,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken) => Task.FromResult(extraction);

        public Task<string> ExtractRecollectionAsync(
            string personaName,
            ChatMessage sourceMessage,
            string sourceAuthorName,
            IReadOnlyList<ChatMessage> recentContext,
            Func<Guid, string> resolveAuthorName,
            CancellationToken cancellationToken)
            => Task.FromResult(recollectionByPersona.TryGetValue(personaName, out var s) ? s : "");
    }
}
