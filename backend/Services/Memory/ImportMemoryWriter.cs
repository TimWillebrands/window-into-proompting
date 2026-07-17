using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PartyTown.Data;

namespace PartyTown.Services.Memory;

/// <summary>Concept link for one imported Event: <c>name</c> is the MERGE key (lowercased),
/// <c>Display</c> the human-facing label (kept via coalesce, first writer wins).</summary>
public sealed record ImportedConceptSeed(string Name, string Display);

/// <summary>One event-routed episode headed for AGE. Ids are caller-supplied (draft item
/// id) so a retried commit rewrites the same vertex instead of duplicating it.</summary>
public sealed record ImportedEventSeed
{
    public Guid EventId { get; init; }
    public string Description { get; init; } = string.Empty;

    /// <summary>Anchor-scheme timestamp (anchor + chunk-ordinal · spacing) — the one thing
    /// the organic capture path cannot do, and the reason this writer exists.</summary>
    public DateTimeOffset At { get; init; }

    public double Weight { get; init; }

    /// <summary>Chunk ordinal, stored as <c>anchor_message_id</c> (imported events have no
    /// Room message id; recall never joins on it, ordering diagnostics do).</summary>
    public int AnchorOrdinal { get; init; }

    public List<Guid> RecollectorPersonaIds { get; init; } = new();
    public List<ImportedConceptSeed> Concepts { get; init; } = new();
}

public sealed record ImportSeedStats(int Events, int Recollections, int ConceptLinks);

/// <summary>Scene-commit writes into the memory graph (ADR 0017 slice 3).</summary>
public interface IImportMemoryWriter
{
    /// <summary>Write one scene's event-routed episodes: Event vertices at scheme
    /// timestamps, ABOUT edges to Participants and Concepts, one RECOLLECTS per matched
    /// participant carrying the episode weight. One transaction; retry-safe (each Event is
    /// deleted-then-recreated by its stable id, so a re-run converges).</summary>
    Task<ImportSeedStats> SeedEventsAsync(
        Guid partyId, Guid roomId, IReadOnlyList<ImportedEventSeed> events, CancellationToken ct);

    /// <summary>Whole-import rollback: detach-delete every Event this Room owns. Cheap by
    /// design (ADR 0017: "whole-import rollback stays cheap — delete the Room").</summary>
    Task DeleteRoomEventsAsync(Guid partyId, Guid roomId, CancellationToken ct);
}

/// <summary>
/// Production port of the phase-2 probe's raw-Cypher seeder: mirrors
/// <see cref="MemoryRepository"/>'s vertex/edge shapes (Event / ABOUT / Concept MERGE /
/// RECOLLECTS / HAS_PARTICIPANT) but takes timestamps and weights from the reviewed draft
/// instead of stamping now — no LLM anywhere. Respects the AGE footguns: values inlined as
/// Cypher literals (never cypher() params), reads typed via output casts (none needed here).
/// </summary>
public sealed class ImportMemoryWriter(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ImportMemoryWriter> logger) : IImportMemoryWriter
{
    public async Task<ImportSeedStats> SeedEventsAsync(
        Guid partyId, Guid roomId, IReadOnlyList<ImportedEventSeed> events, CancellationToken ct)
    {
        if (events.Count == 0) return new ImportSeedStats(0, 0, 0);

        var recollections = 0;
        var conceptLinks = 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var ev in events)
        {
            var atIso = ev.At.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

            // Delete-then-create keeps the write idempotent without leaning on AGE's edge
            // MERGE-with-properties support: a retried commit (or a re-seed after a partial
            // failure in a later step) rewrites the same event id cleanly.
            await ExecuteCypherAsync(db, $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (e:Event {event_id: '{{ev.EventId}}'})
                  DETACH DELETE e
                $cy$) AS (result ag_catalog.agtype)
                """, ct);

            await ExecuteCypherAsync(db, $$"""
                SELECT * FROM cypher('memory', $cy$
                  CREATE (e:Event {
                    event_id: '{{ev.EventId}}',
                    description: {{CypherStr(ev.Description)}},
                    created_at: '{{atIso}}',
                    party_id: '{{partyId}}',
                    room_id: '{{roomId}}',
                    anchor_message_id: {{ev.AnchorOrdinal.ToString(CultureInfo.InvariantCulture)}}
                  })
                  RETURN e.event_id
                $cy$) AS (event_id ag_catalog.agtype)
                """, ct);

            foreach (var personaId in ev.RecollectorPersonaIds)
            {
                await ExecuteCypherAsync(db, $$"""
                    SELECT * FROM cypher('memory', $cy$
                      MATCH (e:Event {event_id: '{{ev.EventId}}'})
                      MERGE (p:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
                      CREATE (e)-[:ABOUT]->(p)
                      RETURN p.persona_id
                    $cy$) AS (persona_id ag_catalog.agtype)
                    """, ct);

                // The imported Recollection snippet is the event description itself — the
                // import has no per-persona perspective source (that was the organic
                // capture path's second LLM call, which commit deliberately lacks).
                await ExecuteCypherAsync(db, $$"""
                    SELECT * FROM cypher('memory', $cy$
                      MATCH (e:Event {event_id: '{{ev.EventId}}'})
                      MERGE (persona:Persona {id: '{{personaId}}'})
                      MERGE (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
                      MERGE (persona)-[:HAS_PARTICIPANT]->(part)
                      CREATE (part)-[:RECOLLECTS {
                        id: '{{RecollectionId(ev.EventId, personaId)}}',
                        snippet: {{CypherStr(ev.Description)}},
                        ts: '{{atIso}}',
                        weight: {{Math.Clamp(ev.Weight, 0.0, 1.0).ToString("0.0###", CultureInfo.InvariantCulture)}},
                        recall_count: 0
                      }]->(e)
                      RETURN part.persona_id
                    $cy$) AS (persona_id ag_catalog.agtype)
                    """, ct);
                recollections++;
            }

            foreach (var concept in ev.Concepts)
            {
                await ExecuteCypherAsync(db, $$"""
                    SELECT * FROM cypher('memory', $cy$
                      MATCH (e:Event {event_id: '{{ev.EventId}}'})
                      MERGE (c:Concept {name: {{CypherStr(concept.Name)}}})
                      SET c.display = coalesce(c.display, {{CypherStr(concept.Display)}})
                      CREATE (e)-[:ABOUT]->(c)
                      RETURN c.name
                    $cy$) AS (concept_name ag_catalog.agtype)
                    """, ct);
                conceptLinks++;
            }
        }

        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Import seeded {Events} events, {Recollections} recollections, {ConceptLinks} concept links into room {RoomId}",
            events.Count, recollections, conceptLinks, roomId);
        return new ImportSeedStats(events.Count, recollections, conceptLinks);
    }

    public async Task DeleteRoomEventsAsync(Guid partyId, Guid roomId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await ExecuteCypherAsync(db, $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (e:Event {party_id: '{{partyId}}', room_id: '{{roomId}}'})
                  DETACH DELETE e
                $cy$) AS (result ag_catalog.agtype)
                """, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        logger.LogInformation("Import rollback: deleted all events for room {RoomId} in party {PartyId}", roomId, partyId);
    }

    /// <summary>Stable edge id per (event, persona) so a re-seed rewrites, never duplicates.</summary>
    private static Guid RecollectionId(Guid eventId, Guid personaId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"partytown-import-recollection:{eventId}:{personaId}")));

    // Mirrors of MemoryRepository's private helpers (see its comments for the EF/AGE
    // rationale): raw NpgsqlCommand to keep Cypher braces away from String.Format, and a
    // single-quoted literal escaper so draft text can't terminate the $cy$ payload.

    private static async Task ExecuteCypherAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string CypherStr(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        sb.Append('\'');
        foreach (var c in s)
        {
            switch (c)
            {
                case '\'': sb.Append("\\'"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('\'');
        return sb.ToString();
    }
}
