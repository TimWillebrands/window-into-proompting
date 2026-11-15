using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PartyTown.Data;

namespace PartyTown.Services.Memory;

internal sealed record EventVertex(
    Guid EventId, string Description, string CreatedAtIso, Guid PartyId, Guid RoomId,
    string Source, int AnchorMessageId, Guid? PlaceId = null);

internal static class MemoryGraphWrites
{
    public static Task CreateEventAsync(AppDbContext db, EventVertex ev, CancellationToken ct)
    {
        var place = ev.PlaceId is { } placeId ? $",\n                place_id: '{placeId}'" : string.Empty;
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              CREATE (e:Event {
                event_id: '{{ev.EventId}}',
                description: {{CypherStr(ev.Description)}},
                created_at: '{{ev.CreatedAtIso}}',
                party_id: '{{ev.PartyId}}',
                room_id: '{{ev.RoomId}}',
                source: {{CypherStr(ev.Source)}},
                anchor_message_id: {{ev.AnchorMessageId.ToString(CultureInfo.InvariantCulture)}}{{place}}
              })
              RETURN e.event_id
            $cy$) AS (event_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    public static Task AddConceptEdgeAsync(AppDbContext db, Guid eventId, string name, string display, CancellationToken ct)
    {
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{eventId}}'})
              MERGE (c:Concept {name: {{CypherStr(name)}}})
              SET c.display = coalesce(c.display, {{CypherStr(display)}})
              CREATE (e)-[:ABOUT]->(c)
              RETURN c.name
            $cy$) AS (concept_name ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    public static Task AddAboutParticipantEdgeAsync(AppDbContext db, Guid eventId, Guid partyId, Guid personaId, CancellationToken ct)
    {
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{eventId}}'})
              MERGE (p:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
              CREATE (e)-[:ABOUT]->(p)
              RETURN p.persona_id
            $cy$) AS (persona_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    public static Task AddRecollectionAsync(
        AppDbContext db, Guid eventId, Guid partyId, Guid personaId,
        Guid edgeId, string snippet, string tsIso, double weight, CancellationToken ct)
    {
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{eventId}}'})
              MERGE (persona:Persona {id: '{{personaId}}'})
              MERGE (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
              MERGE (persona)-[:HAS_PARTICIPANT]->(part)
              CREATE (part)-[:RECOLLECTS {
                id: '{{edgeId}}',
                snippet: {{CypherStr(snippet)}},
                ts: '{{tsIso}}',
                weight: {{Math.Clamp(weight, 0.0, 1.0).ToString("0.0###", CultureInfo.InvariantCulture)}},
                recall_count: 0
              }]->(e)
              RETURN part.persona_id
            $cy$) AS (persona_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    internal static async Task ExecuteCypherAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static string CypherStr(string s)
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
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append('\'');
        return sb.ToString();
    }
}
