using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PartyTown.Data;
using PartyTown.Model;

namespace PartyTown.Services.Memory;

/// <summary>
/// Default <see cref="IMemoryRepository"/> backed by Apache AGE. All Cypher writes for a
/// single capture run inside one Postgres transaction so the Event and its Recollection
/// edges are atomic.
/// </summary>
public sealed class MemoryRepository(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryExtractor extractor,
    ILogger<MemoryRepository> logger) : IMemoryRepository
{
    public async Task<MemoryCaptureResult> CaptureMomentAsync(
        Guid partyId,
        Guid roomId,
        int messageId,
        IReadOnlyList<ParticipantView> presentParticipants,
        IReadOnlyList<ChatMessage> recentContext,
        CancellationToken ct)
    {
        var sourceMessage = recentContext.FirstOrDefault(m => m.MessageId == messageId)
            ?? throw new KeyNotFoundException(
                $"Source message {messageId} not present in recentContext (party {partyId}, room {roomId}).");

        var nameById = presentParticipants.ToDictionary(p => p.Id, p => p.Name);
        string ResolveName(Guid senderId) => nameById.TryGetValue(senderId, out var n) ? n : "Unknown";
        var sourceAuthor = ResolveName(sourceMessage.SenderId);

        var extraction = await extractor.ExtractEventAsync(
            sourceMessage, sourceAuthor, recentContext, presentParticipants, ResolveName, ct);

        if (extraction is null || string.IsNullOrWhiteSpace(extraction.Description))
        {
            logger.LogInformation(
                "Capture skipped: extractor declined to describe message {MessageId} in room {RoomId}",
                messageId, roomId);
            return new MemoryCaptureResult(EventCreated: false, RecollectionsCreated: 0, ConceptsTouched: 0);
        }

        // Narrator (System driver) accumulates memory like any other observer — only User-driven Participants skip.
        var recollectionTargets = presentParticipants.Where(p => p.Driver != DriverKind.User).ToList();
        var recollectionTasks = recollectionTargets.Select(p =>
            extractor.ExtractRecollectionAsync(p.Name, isSpeaker: p.Id == sourceMessage.SenderId, sourceMessage, sourceAuthor, recentContext, ResolveName, ct)
                .ContinueWith(t => (Participant: p, Snippet: t.Result), ct, TaskContinuationOptions.None, TaskScheduler.Default));
        var recollections = (await Task.WhenAll(recollectionTasks))
            .Where(r => !string.IsNullOrWhiteSpace(r.Snippet))
            .ToList();

        var eventId = Guid.NewGuid();
        var nowIso = DateTimeOffset.UtcNow.ToString("o");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // LOAD 'age' is fired automatically by AgeOperatorInterceptor when this connection
        // opens. search_path lives at the database level (05-age-setup.sql), so qualified
        // agtype casts resolve without a per-session SET.
        await CreateEventAsync(db, partyId, roomId, sourceMessage.MessageId, eventId, extraction.Description, nowIso, ct);

        foreach (var concept in extraction.Concepts)
        {
            await AddConceptEdgeAsync(db, eventId, concept, ct);
        }

        foreach (var aboutParticipantId in extraction.ParticipantIds)
        {
            await AddAboutParticipantEdgeAsync(db, eventId, partyId, aboutParticipantId, ct);
        }

        foreach (var (participant, snippet) in recollections)
        {
            await AddRecollectionAsync(db, eventId, partyId, participant.Id, snippet, nowIso, ct);
        }

        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Captured Event {EventId} for message {MessageId} in room {RoomId}: {RecollectionCount} recollections, {ConceptCount} concepts",
            eventId, messageId, roomId, recollections.Count, extraction.Concepts.Count);

        return new MemoryCaptureResult(
            EventCreated: true,
            RecollectionsCreated: recollections.Count,
            ConceptsTouched: extraction.Concepts.Count);
    }

    public async Task<IReadOnlyList<string>> RecallRecentSnippetsAsync(
        Guid personaId,
        Guid partyId,
        int limit,
        CancellationToken ct)
    {
        if (limit <= 0)
        {
            return Array.Empty<string>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // OpenConnectionAsync is EF's documented hook for keeping a connection open across
        // multiple commands; it suppresses the auto-close that single-shot ExecuteSqlRawAsync
        // would do between statements. AgeOperatorInterceptor fires LOAD 'age' as a side
        // effect of the open. ts is stored as ISO-8601 — lex order is chronological, so
        // ORDER BY r.ts DESC sorts newest-first without a datetime cast. limit is an int we
        // control, not user input — inline is fine. See header comment re: Cypher literal inlining.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // Type the projected column as `text` so Postgres applies agtype's output cast
            // (unquoting/unescaping string scalars). Reading agtype directly via Npgsql
            // throws InvalidCastException — no default object/string reader is registered
            // for `ag_catalog.agtype`. `text` reads through the built-in string converter.
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(:Event)
                  RETURN r.snippet
                  ORDER BY r.ts DESC
                  LIMIT {{limit.ToString(CultureInfo.InvariantCulture)}}
                $cy$) AS (snippet text)
                """;

            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var snippets = new List<string>(limit);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;
                var snippet = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    snippets.Add(snippet);
                }
            }

            return snippets;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // AGE's cypher_analyze.c requires the third arg of cypher() to be `IsA(arg, Param)` —
    // any wrapping coercion (an implicit text→agtype cast inserted because Npgsql doesn't
    // know agtype's OID, or an explicit `$1::agtype`) breaks the check. Rather than teach
    // Npgsql about the agtype OID at startup, we inline values directly as Cypher literals
    // — exactly the shape MemoryRepositoryIntegrationTest already uses against this DB.
    // Untrusted strings (LLM-sourced description/snippet, persona-supplied concept names)
    // go through CypherStr; Guids and ints serialise unambiguously.

    private static Task CreateEventAsync(
        AppDbContext db,
        Guid partyId, Guid roomId, int messageId,
        Guid eventId, string description, string nowIso,
        CancellationToken ct)
    {
        // Defensive SET room.party_id picks up Rooms that pre-date the eager EnsureRoomAsync
        // path. Idempotent — a Room created via EnsureRoom already has it.
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MERGE (party:Party {id: '{{partyId}}'})
              MERGE (room:Room {id: '{{roomId}}'})
              SET room.party_id = '{{partyId}}'
              MERGE (msg:Message {room_id: '{{roomId}}', id: {{messageId.ToString(CultureInfo.InvariantCulture)}}})
              CREATE (e:Event {event_id: '{{eventId}}', description: {{CypherStr(description)}}, created_at: '{{nowIso}}', anchor_message_id: {{messageId.ToString(CultureInfo.InvariantCulture)}}})
              CREATE (e)-[:ANCHORED_TO]->(msg)
              RETURN e.event_id
            $cy$) AS (event_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    public async Task EnsureRoomAsync(Guid partyId, Guid roomId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MERGE (room:Room {id: '{{roomId}}'})
                  SET room.party_id = '{{partyId}}'
                  RETURN room.id
                $cy$) AS (id ag_catalog.agtype)
                """;

            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<MemoryGraphDto> GetPartyMemoryGraphAsync(Guid partyId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();

            var nodes = new Dictionary<string, MemoryGraphNode>(StringComparer.Ordinal);
            var links = new HashSet<string>(StringComparer.Ordinal);
            var orderedLinks = new List<MemoryGraphLink>();

            void AddNode(MemoryGraphNode node)
            {
                if (!nodes.TryGetValue(node.Id, out var existing))
                {
                    nodes[node.Id] = node;
                    return;
                }
                // Merge in any new non-null scalars (e.g. Event.description from one row,
                // Concept.display from another) without churning the dedup key.
                nodes[node.Id] = existing with
                {
                    Description = existing.Description ?? node.Description,
                    Display = existing.Display ?? node.Display,
                    CreatedAt = existing.CreatedAt ?? node.CreatedAt,
                };
            }

            void AddLink(MemoryGraphLink link)
            {
                var key = $"{link.Source}\0{link.Target}\0{link.Kind}\0{link.Snippet}\0{link.Ts}";
                if (links.Add(key))
                {
                    orderedLinks.Add(link);
                }
            }

            // Anchor: every Room in this Party becomes a node, even if it has no Events.
            // Without this branch a freshly-created Room would be invisible.
            var roomSql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (room:Room {party_id: '{{partyId}}'})
                  RETURN room.id
                $cy$) AS (room_id text);
                """;
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = roomSql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    if (reader.IsDBNull(0)) continue;
                    var roomId = reader.GetString(0);
                    AddNode(new MemoryGraphNode($"room:{roomId}", "Room"));
                }
            }

            if (nodes.Count == 0)
            {
                return new MemoryGraphDto(Array.Empty<MemoryGraphNode>(), Array.Empty<MemoryGraphLink>());
            }

            // Events anchored to a Message in an in-party Room, plus :ABOUT Concepts,
            // :ABOUT Participants, RECOLLECTS Participants and their Personas. Single
            // OPTIONAL-MATCH-driven Cypher; wide rows with nulls dedup'd in C#. Projected
            // scalars cast to text/int per the AGE agtype reader footgun.
            var graphSql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (room:Room {party_id: '{{partyId}}'})
                  MATCH (e:Event)-[:ANCHORED_TO]->(msg:Message)
                  WHERE msg.room_id = room.id
                  OPTIONAL MATCH (e)-[:ABOUT]->(c:Concept)
                  OPTIONAL MATCH (e)-[:ABOUT]->(p_about:Participant {party_id: '{{partyId}}'})
                  OPTIONAL MATCH (persona_about:Persona)-[:HAS_PARTICIPANT]->(p_about)
                  OPTIONAL MATCH (part:Participant {party_id: '{{partyId}}'})-[rec:RECOLLECTS]->(e)
                  OPTIONAL MATCH (persona_rec:Persona)-[:HAS_PARTICIPANT]->(part)
                  RETURN room.id,
                         msg.room_id, msg.id,
                         e.event_id, e.description, e.created_at,
                         c.name, c.display,
                         p_about.persona_id,
                         persona_about.id,
                         part.persona_id,
                         persona_rec.id,
                         rec.snippet, rec.ts
                $cy$) AS (
                  room_id text,
                  msg_room_id text, msg_id int,
                  event_id text, event_description text, event_created_at text,
                  concept_name text, concept_display text,
                  p_about_persona_id text,
                  persona_about_id text,
                  part_persona_id text,
                  persona_rec_id text,
                  rec_snippet text, rec_ts text
                );
                """;

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = graphSql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    string? Get(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

                    var roomId = Get(0);
                    var msgRoomId = Get(1);
                    var msgId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2);
                    var eventId = Get(3);
                    var eventDescription = Get(4);
                    var eventCreatedAt = Get(5);
                    var conceptName = Get(6);
                    var conceptDisplay = Get(7);
                    var pAboutPersonaId = Get(8);
                    var personaAboutId = Get(9);
                    var partPersonaId = Get(10);
                    var personaRecId = Get(11);
                    var recSnippet = Get(12);
                    var recTs = Get(13);

                    if (roomId is not null)
                    {
                        AddNode(new MemoryGraphNode($"room:{roomId}", "Room"));
                    }

                    string? messageNodeId = null;
                    if (msgRoomId is not null && msgId is not null)
                    {
                        messageNodeId = $"msg:{msgRoomId}:{msgId.Value.ToString(CultureInfo.InvariantCulture)}";
                        AddNode(new MemoryGraphNode(messageNodeId, "Message"));
                    }

                    string? eventNodeId = null;
                    if (eventId is not null)
                    {
                        eventNodeId = $"event:{eventId}";
                        AddNode(new MemoryGraphNode(
                            eventNodeId, "Event",
                            Description: eventDescription,
                            CreatedAt: eventCreatedAt));

                        if (messageNodeId is not null)
                        {
                            AddLink(new MemoryGraphLink(eventNodeId, messageNodeId, "ANCHORED_TO"));
                        }
                    }

                    if (conceptName is not null)
                    {
                        var conceptNodeId = $"concept:{conceptName}";
                        AddNode(new MemoryGraphNode(conceptNodeId, "Concept", Display: conceptDisplay));
                        if (eventNodeId is not null)
                        {
                            AddLink(new MemoryGraphLink(eventNodeId, conceptNodeId, "ABOUT"));
                        }
                    }

                    if (pAboutPersonaId is not null)
                    {
                        var aboutNodeId = $"part:{pAboutPersonaId}:{partyId}";
                        AddNode(new MemoryGraphNode(aboutNodeId, "Participant"));
                        if (eventNodeId is not null)
                        {
                            AddLink(new MemoryGraphLink(eventNodeId, aboutNodeId, "ABOUT"));
                        }
                        if (personaAboutId is not null)
                        {
                            var personaNodeId = $"persona:{personaAboutId}";
                            AddNode(new MemoryGraphNode(personaNodeId, "Persona"));
                            AddLink(new MemoryGraphLink(personaNodeId, aboutNodeId, "HAS_PARTICIPANT"));
                        }
                    }

                    if (partPersonaId is not null)
                    {
                        var partNodeId = $"part:{partPersonaId}:{partyId}";
                        AddNode(new MemoryGraphNode(partNodeId, "Participant"));
                        if (eventNodeId is not null && recSnippet is not null)
                        {
                            AddLink(new MemoryGraphLink(
                                partNodeId, eventNodeId, "RECOLLECTS",
                                Snippet: recSnippet, Ts: recTs));
                        }
                        if (personaRecId is not null)
                        {
                            var personaNodeId = $"persona:{personaRecId}";
                            AddNode(new MemoryGraphNode(personaNodeId, "Persona"));
                            AddLink(new MemoryGraphLink(personaNodeId, partNodeId, "HAS_PARTICIPANT"));
                        }
                    }
                }
            }

            return new MemoryGraphDto(
                nodes.Values.ToList(),
                orderedLinks);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static Task AddConceptEdgeAsync(AppDbContext db, Guid eventId, ConceptTag concept, CancellationToken ct)
    {
        // AGE's Cypher parser doesn't accept `ON CREATE SET`. coalesce() keeps the
        // first-written display while still setting it on the very first MERGE.
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{eventId}}'})
              MERGE (c:Concept {name: {{CypherStr(concept.Name)}}})
              SET c.display = coalesce(c.display, {{CypherStr(concept.Display)}})
              CREATE (e)-[:ABOUT]->(c)
              RETURN c.name
            $cy$) AS (concept_name ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    private static Task AddAboutParticipantEdgeAsync(AppDbContext db, Guid eventId, Guid partyId, Guid participantPersonaId, CancellationToken ct)
    {
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{eventId}}'})
              MERGE (party:Party {id: '{{partyId}}'})
              MERGE (p:Participant {persona_id: '{{participantPersonaId}}', party_id: '{{partyId}}'})
              MERGE (p)-[:IN_PARTY]->(party)
              CREATE (e)-[:ABOUT]->(p)
              RETURN p.persona_id
            $cy$) AS (persona_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    private static Task AddRecollectionAsync(
        AppDbContext db,
        Guid eventId, Guid partyId, Guid personaId, string snippet, string nowIso,
        CancellationToken ct)
    {
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{eventId}}'})
              MERGE (party:Party {id: '{{partyId}}'})
              MERGE (persona:Persona {id: '{{personaId}}'})
              MERGE (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
              MERGE (persona)-[:HAS_PARTICIPANT]->(part)
              MERGE (part)-[:IN_PARTY]->(party)
              CREATE (part)-[:RECOLLECTS {snippet: {{CypherStr(snippet)}}, ts: '{{nowIso}}'}]->(e)
              RETURN part.persona_id
            $cy$) AS (persona_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    // We bypass EF's ExecuteSqlRawAsync because it routes the SQL through String.Format and
    // the Cypher payload's literal `{` / `}` break formatting. The raw NpgsqlCommand path
    // sends the text untouched; we still piggy-back on EF's connection + active transaction.
    private static async Task ExecuteCypherAsync(AppDbContext db, string sql, CancellationToken ct)
    {
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Cypher single-quoted string literal with backslash-escaping per the openCypher spec.
    // Keeps untrusted input from terminating the literal or smuggling in a `$cy$` close.
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
