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

        // Match-or-mint: hand the extractor the existing Concept vocabulary so it reuses a
        // tag when one fits instead of fragmenting reality with near-duplicates.
        var existingConcepts = await FetchExistingConceptDisplaysAsync(ct);

        var extraction = await extractor.ExtractEventAsync(
            sourceMessage, sourceAuthor, recentContext, presentParticipants, existingConcepts, ResolveName, ct);

        if (extraction is null || string.IsNullOrWhiteSpace(extraction.Description))
        {
            logger.LogInformation(
                "Capture skipped: extractor declined to describe message {MessageId} in room {RoomId}",
                messageId, roomId);
            return new MemoryCaptureResult(EventCreated: false, RecollectionsCreated: 0, ConceptsTouched: 0);
        }

        // Narrator (System driver) accumulates memory like any other observer — only User-driven Participants skip.
        // One batched LLM call covers every target; a target the model declined is absent from the map.
        var recollectionTargets = presentParticipants
            .Where(p => p.Driver != DriverKind.User)
            .Select(p => new RecollectionTarget(p.Id, p.Name, IsSpeaker: p.Id == sourceMessage.SenderId))
            .ToList();
        var draftByPersona = await extractor.ExtractRecollectionsAsync(
            recollectionTargets, sourceMessage, sourceAuthor, recentContext, ResolveName, ct);
        var recollections = recollectionTargets
            .Select(t => (t.PersonaId, Draft: draftByPersona.GetValueOrDefault(t.PersonaId)))
            .Where(r => r.Draft is not null && !string.IsNullOrWhiteSpace(r.Draft.Snippet))
            .Select(r => (r.PersonaId, Draft: r.Draft!))
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

        foreach (var (personaId, draft) in recollections)
        {
            await AddRecollectionAsync(db, eventId, partyId, personaId, draft, nowIso, ct);
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

    // How many recent edges the recall query pulls before C#-side salience ranking. Far
    // above the surfaced N≈10 so a fresh low-weight burst can't push an oft-recalled older
    // memory out of the candidate set before scoring sees it.
    private const int RecallCandidatePool = 50;

    public async Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        Guid personaId,
        Guid partyId,
        int limit,
        CancellationToken ct)
    {
        if (limit <= 0)
        {
            return Array.Empty<RecalledMemory>();
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // OpenConnectionAsync is EF's documented hook for keeping a connection open across
        // multiple commands; it suppresses the auto-close that single-shot ExecuteSqlRawAsync
        // would do between statements. AgeOperatorInterceptor fires LOAD 'age' as a side
        // effect of the open. ts is stored as ISO-8601 — lex order is chronological, so
        // ORDER BY r.ts DESC sorts newest-first without a datetime cast. The pool size is a
        // const we control, not user input — inline is fine. See header comment re: Cypher
        // literal inlining.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // Type projected columns as `text` / `int` so Postgres applies agtype's output
            // cast (unquoting/unescaping string scalars). Reading agtype directly via Npgsql
            // throws InvalidCastException — no default object/string reader is registered
            // for `ag_catalog.agtype`. weight is projected as text and parsed in C# to stay
            // on that proven cast path. Salience is deliberately NOT computed in Cypher
            // (ADR 0015) — fetch recent candidates, score and rank here.
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(:Event)
                  RETURN r.id, r.snippet, r.weight, r.recall_count, r.last_recalled, r.ts
                  ORDER BY r.ts DESC
                  LIMIT {{RecallCandidatePool.ToString(CultureInfo.InvariantCulture)}}
                $cy$) AS (id text, snippet text, weight text, recall_count int, last_recalled text, ts text)
                """;

            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var now = DateTimeOffset.UtcNow;
            var candidates = new List<RecalledMemory>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string? Get(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

                var snippet = Get(1);
                if (string.IsNullOrWhiteSpace(snippet)) continue;

                // Pre-ADR-0015 edges lack id/weight/recall_count — they stay surfaceable
                // (Guid.Empty marks them unstrengthenable) at the default midpoint weight;
                // per the no-migration-safety stance, old edges get no backfill.
                var edgeId = Guid.TryParse(Get(0), out var parsedId) ? parsedId : Guid.Empty;
                var weight = double.TryParse(Get(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                    ? Math.Clamp(w, 0.0, 1.0)
                    : SalienceMath.DefaultWeight;
                var recallCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                var lastRecalled = ParseIso(Get(4));
                var capturedAt = ParseIso(Get(5)) ?? now;

                var salience = SalienceMath.Score(weight, capturedAt, recallCount, lastRecalled, now);
                candidates.Add(new RecalledMemory(
                    edgeId, snippet, weight, recallCount, lastRecalled, capturedAt, salience));
            }

            return candidates
                .OrderByDescending(m => m.Salience)
                .ThenByDescending(m => m.CapturedAt)
                .Take(limit)
                .ToList();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task StrengthenRecollectionAsync(Guid recollectionId, CancellationToken ct)
    {
        if (recollectionId == Guid.Empty)
        {
            return;
        }

        var nowIso = DateTimeOffset.UtcNow.ToString("o");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // coalesce keeps the write idempotent against a hand-made edge missing
            // recall_count. An unknown id simply matches nothing — silent no-op by design.
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (:Participant)-[r:RECOLLECTS {id: '{{recollectionId}}'}]->(:Event)
                  SET r.recall_count = coalesce(r.recall_count, 0) + 1,
                      r.last_recalled = '{{nowIso}}'
                  RETURN r.id
                $cy$) AS (id ag_catalog.agtype)
                """;
            await ExecuteCypherAsync(db, sql, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static DateTimeOffset? ParseIso(string? iso)
        => DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)
            ? ts
            : null;

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
        // The Event carries its anchor (room_id / party_id / anchor_message_id) as plain
        // properties — no Room/Message vertices, no ANCHORED_TO edge. party_id is the scope
        // key the viz and any per-Party walk anchor on.
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              CREATE (e:Event {
                event_id: '{{eventId}}',
                description: {{CypherStr(description)}},
                created_at: '{{nowIso}}',
                party_id: '{{partyId}}',
                room_id: '{{roomId}}',
                anchor_message_id: {{messageId.ToString(CultureInfo.InvariantCulture)}}
              })
              RETURN e.event_id
            $cy$) AS (event_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    // Existing Concept display labels, for the match-or-mint guidance in event extraction.
    // Read-only, single command — explicit open/close per the EF connection-lifetime footgun.
    private async Task<IReadOnlyList<string>> FetchExistingConceptDisplaysAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // Project as text so AGE's agtype output cast unquotes the scalar (the reader
            // footgun). coalesce keeps a Concept that somehow lacks a display visible by name.
            const string sql = """
                SELECT * FROM cypher('memory', $cy$
                  MATCH (c:Concept)
                  RETURN coalesce(c.display, c.name)
                  LIMIT 500
                $cy$) AS (display text)
                """;

            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var names = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0)) continue;
                var name = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // ─── Stance floor (ADR 0016) ──────────────────────────────────────────────────────────

    // One raw STANCE edge as read back, before latest-wins/anchor filtering. A row is either
    // a Participant target (TargetPersonaId set) or a Concept target (ConceptName set).
    private sealed record StanceEdgeRow(
        Guid Id,
        double Valence,
        string Reasoning,
        DateTimeOffset Ts,
        Guid? TargetPersonaId,
        string? ConceptName,
        string? ConceptDisplay);

    public async Task<Guid> AppendStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        CancellationToken ct)
    {
        var edgeId = Guid.NewGuid();
        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var valenceLiteral = Math.Clamp(valence, -1.0, 1.0)
            .ToString("0.0###", CultureInfo.InvariantCulture);

        // Edge property block is identical across target shapes — only the matched/merged
        // target node differs. reasoning is curator free-text (untrusted) → CypherStr.
        var edgeProps =
            $"{{ id: '{edgeId}', valence: {valenceLiteral}, reasoning: {CypherStr(reasoning)}, ts: '{nowIso}' }}";

        string sql;
        if (target.Kind == StanceTargetKind.Concept)
        {
            var name = target.ConceptName ?? string.Empty;
            var display = target.ConceptDisplay ?? name;
            sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MERGE (src:Participant {persona_id: '{{sourcePersonaId}}', party_id: '{{partyId}}'})
                  MERGE (c:Concept {name: {{CypherStr(name)}}})
                  SET c.display = coalesce(c.display, {{CypherStr(display)}})
                  CREATE (src)-[s:STANCE {{edgeProps}}]->(c)
                  RETURN s.id
                $cy$) AS (id ag_catalog.agtype)
                """;
        }
        else
        {
            // Self points the edge back at the source Participant — same MERGE key, so both
            // MERGEs resolve to one node and CREATE writes a self-loop.
            var targetPersonaId = target.Kind == StanceTargetKind.Self
                ? sourcePersonaId
                : target.PersonaId ?? throw new ArgumentException(
                    "Participant Stance target requires PersonaId.", nameof(target));
            sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MERGE (src:Participant {persona_id: '{{sourcePersonaId}}', party_id: '{{partyId}}'})
                  MERGE (tgt:Participant {persona_id: '{{targetPersonaId}}', party_id: '{{partyId}}'})
                  CREATE (src)-[s:STANCE {{edgeProps}}]->(tgt)
                  RETURN s.id
                $cy$) AS (id ag_catalog.agtype)
                """;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await ExecuteCypherAsync(db, sql, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        logger.LogInformation(
            "Appended Stance {EdgeId} from persona {PersonaId} ({Kind}) in party {PartyId}",
            edgeId, sourcePersonaId, target.Kind, partyId);

        return edgeId;
    }

    public async Task<IReadOnlyList<StanceRecord>> ListStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        CancellationToken ct)
    {
        var rows = await FetchStanceEdgesAsync(partyId, sourcePersonaId, ct);

        // Newest-first already (ORDER BY ts DESC); the first row per target is current.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var records = new List<StanceRecord>(rows.Count);
        foreach (var row in rows)
        {
            var isCurrent = seen.Add(TargetKey(row));
            records.Add(ToRecord(row, sourcePersonaId, isCurrent));
        }
        return records;
    }

    public async Task<IReadOnlyList<StanceLine>> RecallStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct)
    {
        if (limit <= 0)
        {
            return Array.Empty<StanceLine>();
        }

        var rows = await FetchStanceEdgesAsync(partyId, sourcePersonaId, ct);
        var present = new HashSet<Guid>(presentPersonaIds);
        var text = anchorText ?? string.Empty;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var anchored = new List<StanceEdgeRow>();
        foreach (var row in rows)
        {
            // Latest-wins: skip any superseded edge for a target we've already taken.
            if (!seen.Add(TargetKey(row)))
            {
                continue;
            }

            var keep = row.TargetPersonaId is Guid tp
                // A Participant target (self included — source is in the present cast) renders
                // only while that Participant is in the room.
                ? present.Contains(tp)
                // A Concept renders only when it's live in the triggering message.
                : ConceptMatchesAnchor(row, text);

            if (keep)
            {
                anchored.Add(row);
            }
        }

        return anchored
            .OrderByDescending(r => Math.Abs(r.Valence))
            .ThenByDescending(r => r.Ts)
            .Take(limit)
            .Select(r => new StanceLine(r.Reasoning, r.Valence))
            .ToList();
    }

    private static bool ConceptMatchesAnchor(StanceEdgeRow row, string anchorText)
        => (row.ConceptName is { Length: > 0 } name
                && anchorText.Contains(name, StringComparison.OrdinalIgnoreCase))
            || (row.ConceptDisplay is { Length: > 0 } display
                && anchorText.Contains(display, StringComparison.OrdinalIgnoreCase));

    // Dedup key for latest-wins. A Participant target keys on its persona id; a Concept on its
    // normalised name. Self collapses into its persona-id key like any Participant target.
    private static string TargetKey(StanceEdgeRow row)
        => row.TargetPersonaId is Guid tp ? $"p:{tp}" : $"c:{row.ConceptName}";

    private static StanceRecord ToRecord(StanceEdgeRow row, Guid sourcePersonaId, bool isCurrent)
    {
        if (row.TargetPersonaId is Guid tp)
        {
            var kind = tp == sourcePersonaId ? StanceTargetKind.Self : StanceTargetKind.Participant;
            return new StanceRecord(
                row.Id, row.Valence, row.Reasoning, row.Ts,
                kind, tp, null, null, isCurrent);
        }
        return new StanceRecord(
            row.Id, row.Valence, row.Reasoning, row.Ts,
            StanceTargetKind.Concept, null, row.ConceptName, row.ConceptDisplay, isCurrent);
    }

    // All STANCE edges authored by one Participant, newest first. Single read-only Cypher —
    // explicit open/close per the EF connection-lifetime footgun; scalars cast to text so
    // AGE's agtype output cast unquotes them (valence parsed in C# like RECOLLECTS.weight).
    private async Task<IReadOnlyList<StanceEdgeRow>> FetchStanceEdgesAsync(
        Guid partyId, Guid sourcePersonaId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (src:Participant {persona_id: '{{sourcePersonaId}}', party_id: '{{partyId}}'})-[s:STANCE]->(tgt)
                  RETURN s.id, s.valence, s.reasoning, s.ts, tgt.persona_id, tgt.name, tgt.display
                  ORDER BY s.ts DESC
                $cy$) AS (id text, valence text, reasoning text, ts text,
                          target_persona_id text, concept_name text, concept_display text)
                """;

            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var rows = new List<StanceEdgeRow>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                string? Get(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

                if (!Guid.TryParse(Get(0), out var id))
                {
                    continue;
                }
                var reasoning = Get(2);
                if (string.IsNullOrWhiteSpace(reasoning))
                {
                    continue;
                }
                var valence = double.TryParse(Get(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? Math.Clamp(v, -1.0, 1.0)
                    : 0.0;
                var ts = ParseIso(Get(3)) ?? DateTimeOffset.UtcNow;

                var targetPersonaId = Guid.TryParse(Get(4), out var tp) ? tp : (Guid?)null;
                var conceptName = Get(5);
                var conceptDisplay = Get(6);

                // A well-formed edge points at exactly one of the two target shapes.
                if (targetPersonaId is null && string.IsNullOrWhiteSpace(conceptName))
                {
                    continue;
                }

                rows.Add(new StanceEdgeRow(
                    id, valence, reasoning, ts, targetPersonaId, conceptName, conceptDisplay));
            }

            return rows;
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
                    RoomId = existing.RoomId ?? node.RoomId,
                    AnchorMessageId = existing.AnchorMessageId ?? node.AnchorMessageId,
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

            // Anchor on Event party_id (Room/Message vertices are gone — the anchor lives as
            // properties on the Event). Empty Rooms no longer appear; the viz lists Rooms
            // from REST. :ABOUT Concepts, :ABOUT Participants, RECOLLECTS Participants and
            // their Personas hang off each Event. Single OPTIONAL-MATCH-driven Cypher; wide
            // rows with nulls dedup'd in C#. Projected scalars cast to text/int per the AGE
            // agtype reader footgun.
            var graphSql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (e:Event {party_id: '{{partyId}}'})
                  OPTIONAL MATCH (e)-[:ABOUT]->(c:Concept)
                  OPTIONAL MATCH (e)-[:ABOUT]->(p_about:Participant {party_id: '{{partyId}}'})
                  OPTIONAL MATCH (persona_about:Persona)-[:HAS_PARTICIPANT]->(p_about)
                  OPTIONAL MATCH (part:Participant {party_id: '{{partyId}}'})-[rec:RECOLLECTS]->(e)
                  OPTIONAL MATCH (persona_rec:Persona)-[:HAS_PARTICIPANT]->(part)
                  RETURN e.event_id, e.description, e.created_at, e.room_id, e.anchor_message_id,
                         c.name, c.display,
                         p_about.persona_id,
                         persona_about.id,
                         part.persona_id,
                         persona_rec.id,
                         rec.snippet, rec.ts
                $cy$) AS (
                  event_id text, event_description text, event_created_at text,
                  event_room_id text, event_anchor_message_id int,
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

                    var eventId = Get(0);
                    var eventDescription = Get(1);
                    var eventCreatedAt = Get(2);
                    var eventRoomId = Get(3);
                    var eventAnchorMessageId = reader.IsDBNull(4) ? (int?)null : reader.GetInt32(4);
                    var conceptName = Get(5);
                    var conceptDisplay = Get(6);
                    var pAboutPersonaId = Get(7);
                    var personaAboutId = Get(8);
                    var partPersonaId = Get(9);
                    var personaRecId = Get(10);
                    var recSnippet = Get(11);
                    var recTs = Get(12);

                    string? eventNodeId = null;
                    if (eventId is not null)
                    {
                        eventNodeId = $"event:{eventId}";
                        AddNode(new MemoryGraphNode(
                            eventNodeId, "Event",
                            Description: eventDescription,
                            CreatedAt: eventCreatedAt,
                            RoomId: eventRoomId,
                            AnchorMessageId: eventAnchorMessageId));
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
              MERGE (p:Participant {persona_id: '{{participantPersonaId}}', party_id: '{{partyId}}'})
              CREATE (e)-[:ABOUT]->(p)
              RETURN p.persona_id
            $cy$) AS (persona_id ag_catalog.agtype)
            """;
        return ExecuteCypherAsync(db, sql, ct);
    }

    private static Task AddRecollectionAsync(
        AppDbContext db,
        Guid eventId, Guid partyId, Guid personaId, RecollectionDraft draft, string nowIso,
        CancellationToken ct)
    {
        // ADR 0015 salience substrate: id is the edge's stable identity (strengthening key,
        // future-embeddings join key); weight rode the extraction call; recall_count starts
        // 0. last_recalled is deliberately absent until the first pick — Cypher SET-to-null
        // removes a property anyway, so absence IS the null representation.
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (e:Event {event_id: '{{eventId}}'})
              MERGE (persona:Persona {id: '{{personaId}}'})
              MERGE (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
              MERGE (persona)-[:HAS_PARTICIPANT]->(part)
              CREATE (part)-[:RECOLLECTS {
                id: '{{Guid.NewGuid()}}',
                snippet: {{CypherStr(draft.Snippet)}},
                ts: '{{nowIso}}',
                weight: {{Math.Clamp(draft.Weight, 0.0, 1.0).ToString("0.0###", CultureInfo.InvariantCulture)}},
                recall_count: 0
              }]->(e)
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
