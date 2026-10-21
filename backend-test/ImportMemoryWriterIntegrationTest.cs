using System.Globalization;
using BackendTest.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PartyTown.Services.Memory;

namespace BackendTest;

/// <summary>Import seeding writes Capture's graph shapes, tagged <c>source: 'import'</c>, and converges on re-run.</summary>
public sealed class ImportMemoryWriterIntegrationTest(MemoryGraphFixture fixture) : IClassFixture<MemoryGraphFixture>
{
    [Fact]
    public async Task SeedEvents_WritesImportSource_NormalisedConcept_AndRerunConverges()
    {
        if (!fixture.IsAvailable)
            throw new InvalidOperationException($"Dev Postgres+AGE unavailable: {fixture.UnavailableReason}.");

        var (partyId, roomId, persona) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var concept = $"  Lisp-{Guid.NewGuid():N}  ";
        var seed = new ImportedEventSeed
        {
            EventId = Guid.NewGuid(), Description = "Hana defended Lisp.", At = DateTimeOffset.UtcNow.AddDays(-30),
            Weight = 0.6, AnchorOrdinal = 3, RecollectorPersonaIds = new() { persona },
            Concepts = new() { new ImportedConceptSeed(concept, "Lisp") },
        };
        var writer = new ImportMemoryWriter(fixture.Factory, NullLogger<ImportMemoryWriter>.Instance);

        await writer.SeedEventsAsync(partyId, roomId, new[] { seed }, CancellationToken.None);
        await writer.SeedEventsAsync(partyId, roomId, new[] { seed }, CancellationToken.None);

        await using var conn = new NpgsqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using (var load = conn.CreateCommand()) { load.CommandText = "LOAD 'age';"; await load.ExecuteNonQueryAsync(); }

        Assert.Equal(1, await CountAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{seed.EventId}}', source: 'import', anchor_message_id: 3})
              RETURN count(e) $cy$) AS (n int);
            """));
        Assert.Equal(1, await CountAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (:Participant {persona_id: '{{persona}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(:Event {event_id: '{{seed.EventId}}'})
              RETURN count(r) $cy$) AS (n int);
            """));
        Assert.Equal(1, await CountAsync(conn, $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (:Event {event_id: '{{seed.EventId}}'})-[:ABOUT]->(c:Concept {name: '{{concept.Trim().ToLowerInvariant()}}'})
              RETURN count(c) $cy$) AS (n int);
            """));

        await writer.DeleteRoomEventsAsync(partyId, roomId, CancellationToken.None);
    }

    private static async Task<int> CountAsync(NpgsqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0, CultureInfo.InvariantCulture);
    }
}
