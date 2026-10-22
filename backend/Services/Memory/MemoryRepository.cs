using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PartyTown.Data;
using PartyTown.Model;

using static PartyTown.Services.Memory.MemoryGraphWrites;

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
    public async Task<MemoryCaptureResult> CaptureAsync(Moment moment, CancellationToken ct)
    {
        if (moment.MarkIndex < 0 || moment.MarkIndex >= moment.Lines.Count)
            throw new ArgumentOutOfRangeException(nameof(moment), $"Mark {moment.MarkIndex} outside {moment.Lines.Count} lines.");

        var (partyId, roomId, presentParticipants) = (moment.PartyId, moment.ThreadId, moment.Present);
        // Extractors read chat-shaped lines; non-Room lines get their index as a local id.
        var recentContext = moment.Lines.Select((l, i) => new ChatMessage
        {
            MessageId = l.MessageId ?? i, ChatGroupId = roomId, SenderId = l.SenderId,
            SenderType = "assistant", Content = l.Text, SendAt = l.At,
        }).ToList();
        var sourceMessage = recentContext[moment.MarkIndex];
        var messageId = sourceMessage.MessageId;

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
        // Fidelity pass: a draft with corrupted perspective (unattributed first person —
        // in practice a swapped subject) keeps its weight but trades its snippet for one
        // templated from the Event description (RecollectionFidelity). A missing draft
        // stays missing — the model's "not memorable for them" is a legitimate judgment.
        var recollections = recollectionTargets
            .Select(t => (t.PersonaId, t.Name, Draft: draftByPersona.GetValueOrDefault(t.PersonaId)))
            .Where(r => r.Draft is not null && !string.IsNullOrWhiteSpace(r.Draft.Snippet))
            .Select(r =>
            {
                var (draft, substituted) = RecollectionFidelity.Sanitize(r.Draft!, extraction.Description);
                if (substituted)
                {
                    logger.LogWarning(
                        "Recollection draft for {PersonaName} had corrupted perspective; substituted event description. Original: {Original}",
                        r.Name, r.Draft!.Snippet);
                }
                return (r.PersonaId, r.Name, Draft: draft, Substituted: substituted);
            })
            .ToList();

        var eventId = Guid.NewGuid();
        var nowIso = DateTimeOffset.UtcNow.ToString("o");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // LOAD 'age' is fired automatically by AgeOperatorInterceptor when this connection
        // opens. search_path lives at the database level (05-age-setup.sql), so qualified
        // agtype casts resolve without a per-session SET.
        await CreateEventAsync(db, new EventVertex(eventId, extraction.Description, nowIso, partyId, roomId,
            moment.Source, sourceMessage.MessageId, moment.PlaceId), ct);

        foreach (var concept in extraction.Concepts)
        {
            await AddConceptEdgeAsync(db, eventId, concept.Name, concept.Display, ct);
        }

        foreach (var aboutParticipantId in extraction.ParticipantIds)
        {
            await AddAboutParticipantEdgeAsync(db, eventId, partyId, aboutParticipantId, ct);
        }

        var writtenPersonaIds = new List<Guid>();
        foreach (var (personaId, name, draft, substituted) in recollections)
        {
            // Skip a substituted snippet that near-duplicates one the persona already holds
            // (same development across adjacent Events — text similarity, not identity); clean
            // model-authored drafts always pass.
            if (substituted)
            {
                var existing = await ListRecentSnippetsAsync(db, partyId, personaId, ct);
                var duplicate = existing.FirstOrDefault(s => RecollectionFidelity.IsNearDuplicate(draft.Snippet, s));
                if (duplicate is not null)
                {
                    logger.LogInformation(
                        "Skipped substituted recollection for {PersonaName}: near-duplicate of existing snippet. New: {New} | Existing: {Existing}",
                        name, draft.Snippet, duplicate);
                    continue;
                }
            }

            await AddRecollectionAsync(db, eventId, partyId, personaId, Guid.NewGuid(), draft.Snippet, nowIso, draft.Weight, ct);
            writtenPersonaIds.Add(personaId);
        }

        await tx.CommitAsync(ct);

        logger.LogInformation(
            "Captured Event {EventId} for message {MessageId} in room {RoomId}: {RecollectionCount} recollections, {ConceptCount} concepts",
            eventId, messageId, roomId, writtenPersonaIds.Count, extraction.Concepts.Count);

        return new MemoryCaptureResult(
            EventCreated: true,
            RecollectionsCreated: writtenPersonaIds.Count,
            ConceptsTouched: extraction.Concepts.Count)
        {
            RecollectionPersonaIds = writtenPersonaIds,
        };
    }

    // Recency pool far above the surfaced N≈10 so a fresh low-weight burst can't push an oft-recalled
    // memory out before scoring; the relevant arm is unpooled by design. Shared with RecallRanking.
    private const int RecallCandidatePool = RecallRanking.CandidatePool;

    // Bounds on the anchor literals inlined into the walk query: untrusted message text
    // (multi-user boundary) must not balloon the Cypher source.
    private const int MaxAnchorTextChars = 2000;
    private const int MaxAnchorTokens = 32;
    private const int MaxAnchorBigrams = 16;
    private const int MaxCastAnchors = 32;

    public async Task<IReadOnlyList<RecalledMemory>> RecallAsync(
        Guid personaId,
        Guid partyId,
        IReadOnlyList<Guid> presentPersonaIds,
        string anchorText,
        int limit,
        CancellationToken ct,
        Guid? placeId = null)
    {
        if (limit <= 0)
        {
            return Array.Empty<RecalledMemory>();
        }

        var castAnchors = (presentPersonaIds ?? Array.Empty<Guid>())
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Take(MaxCastAnchors)
            .ToList();
        var tokenAnchors = TokenizeAnchorText(anchorText);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // OpenConnectionAsync is EF's documented hook for keeping a connection open across
        // multiple commands; it suppresses the auto-close that single-shot ExecuteSqlRawAsync
        // would do between statements. AgeOperatorInterceptor fires LOAD 'age' as a side
        // effect of the open.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // Type projected columns as `text` / `int` so Postgres applies agtype's output
            // cast (unquoting/unescaping string scalars). Reading agtype directly via Npgsql
            // throws InvalidCastException — no default object/string reader is registered
            // for `ag_catalog.agtype`. weight is projected as text and parsed in C# to stay
            // on that proven cast path. Salience is deliberately NOT computed in Cypher
            // (ADR 0015) — fetch candidates, score and rank here.
            var sql = BuildRecallSql(personaId, partyId, castAnchors, tokenAnchors, placeId);

            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;

            var now = DateTimeOffset.UtcNow;
            // Dedup across arms by edge id: an edge matched by an anchor leg also appears in
            // the recent leg (and a UNION row per flag value) — keep one entry, relevance
            // wins. Pre-ADR-0015 edges lack an id; a snippet+ts composite keeps two distinct
            // legacy edges from collapsing into one.
            var rows = new List<RecallCandidate>();
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
                var relevant = !reader.IsDBNull(6) && reader.GetInt32(6) != 0;

                var key = edgeId != Guid.Empty
                    ? edgeId.ToString("N")
                    : $"legacy|{Get(5)}|{snippet}";
                var salience = SalienceMath.Score(weight, capturedAt, recallCount, lastRecalled, now);
                rows.Add(new RecallCandidate(key, new RecalledMemory(
                    edgeId, snippet, weight, recallCount, lastRecalled, capturedAt, salience), relevant));
            }

            return RecallRanking.QuotaUnion(RecallRanking.Dedup(rows), limit);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }


    /// <summary>
    /// One Cypher round-trip for both arms. With anchors: a 3-leg UNION — Participant-anchor
    /// walk, Concept-anchor walk (each flagged <c>relevant = 1</c>), and the bare recency
    /// leg (<c>relevant = 0</c>). UNION's distinct-row semantics collapse multi-anchor hits
    /// inside a leg; the same edge still appears once per flag value, dedup'd in C#. The
    /// walk legs are deliberately un-LIMITed — a relevant memory may be arbitrarily old —
    /// while the recency pool cap moves to C# (per-leg ORDER BY/LIMIT is not legal inside a
    /// UNION). Anchorless beats keep the exact pre-walk query, recency-pooled in Cypher.
    /// </summary>
    private static string BuildRecallSql(
        Guid personaId,
        Guid partyId,
        IReadOnlyList<Guid> castAnchors,
        IReadOnlyList<string> tokenAnchors,
        Guid? placeId = null)
    {
        var match =
            $"MATCH (part:Participant {{persona_id: '{personaId}', party_id: '{partyId}'}})-[r:RECOLLECTS]->";
        const string columns =
            "r.id AS id, r.snippet AS snippet, r.weight AS weight, r.recall_count AS recall_count, " +
            "r.last_recalled AS last_recalled, r.ts AS ts";
        const string asClause =
            "AS (id text, snippet text, weight text, recall_count int, last_recalled text, ts text, relevant int)";

        if (castAnchors.Count == 0 && tokenAnchors.Count == 0 && placeId is null)
        {
            return $"""
                SELECT * FROM cypher('memory', $cy$
                  {match}(:Event)
                  RETURN {columns}, 0 AS relevant
                  ORDER BY r.ts DESC
                  LIMIT {RecallCandidatePool.ToString(CultureInfo.InvariantCulture)}
                $cy$) {asClause}
                """;
        }

        var legs = new List<string>();
        if (castAnchors.Count > 0)
        {
            var ids = string.Join(", ", castAnchors.Select(id => $"'{id}'"));
            legs.Add($$"""
                  {{match}}(:Event)-[:ABOUT]->(a:Participant {party_id: '{{partyId}}'})
                  WHERE a.persona_id IN [{{ids}}]
                  RETURN {{columns}}, 1 AS relevant
                """);
        }
        if (tokenAnchors.Count > 0)
        {
            var names = string.Join(", ", tokenAnchors.Select(CypherStr));
            legs.Add($"""
                  {match}(:Event)-[:ABOUT]->(c:Concept)
                  WHERE c.name IN [{names}]
                  RETURN {columns}, 1 AS relevant
                """);
        }
        if (placeId is { } place)
        {
            // Being back where it happened: Place talk Events carry place_id (spec .scratch/places).
            legs.Add($"""
                  {match}(e:Event)
                  WHERE e.place_id = '{place}'
                  RETURN {columns}, 1 AS relevant
                """);
        }
        legs.Add($"""
              {match}(:Event)
              RETURN {columns}, 0 AS relevant
            """);

        return $"""
            SELECT * FROM cypher('memory', $cy$
            {string.Join("\n  UNION\n", legs)}
            $cy$) {asClause}
            """;
    }

    /// <summary>
    /// Concept anchors from the triggering message: lowercase unigrams (length ≥ 2) plus
    /// adjacent-pair bigrams, matching <see cref="MemoryExtractor.NormaliseConceptName"/>'s
    /// trim+lowercase normalisation. Bigrams catch two-word Concepts ("common lisp");
    /// longer names wait for the embeddings arm (ADR 0015 deferral). Counts and input
    /// length are capped — the message is untrusted boundary input.
    /// </summary>
    /// <summary>Lowercase letter/digit runs of untrusted text, length-capped — the word stream Concept matching reads.</summary>
    internal static List<string> AnchorWords(string? text)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return words;
        var sb = new StringBuilder();
        foreach (var c in (text.Length > MaxAnchorTextChars ? text[..MaxAnchorTextChars] : text).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        if (sb.Length > 0) words.Add(sb.ToString());
        return words;
    }

    internal static IReadOnlyList<string> TokenizeAnchorText(string? anchorText)
    {
        if (string.IsNullOrWhiteSpace(anchorText))
        {
            return Array.Empty<string>();
        }

        var raw = AnchorWords(anchorText);

        var anchors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in raw)
        {
            if (anchors.Count >= MaxAnchorTokens) break;
            if (token.Length >= 2 && seen.Add(token))
            {
                anchors.Add(token);
            }
        }
        var bigrams = 0;
        for (var i = 0; i + 1 < raw.Count && bigrams < MaxAnchorBigrams; i++)
        {
            var bigram = $"{raw[i]} {raw[i + 1]}";
            if (seen.Add(bigram))
            {
                anchors.Add(bigram);
                bigrams++;
            }
        }
        return anchors;
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


    public async Task<Guid> AppendStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct)
    {
        var edgeId = Guid.NewGuid();
        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var valenceLiteral = Math.Clamp(valence, -1.0, 1.0)
            .ToString("0.0###", CultureInfo.InvariantCulture);

        // Curator writes carry no provenance properties — identical to pre-attribution edges,
        // which read back as Curator by absence. Other writers stamp origin + their pointer.
        var provenance = attribution?.Origin switch
        {
            StanceOrigin.Consolidation => $", origin: 'consolidation', run_id: '{attribution.RunId}'",
            StanceOrigin.Retract => $", origin: 'retract', retracts: '{attribution.RetractsId}'",
            _ => string.Empty,
        };

        // Edge property block is identical across target shapes — only the matched/merged
        // target node differs. reasoning is curator free-text (untrusted) → CypherStr.
        var edgeProps =
            $"{{ id: '{edgeId}', valence: {valenceLiteral}, reasoning: {CypherStr(reasoning)}, ts: '{nowIso}'{provenance} }}";

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
            "Appended Stance {EdgeId} from persona {PersonaId} ({Kind}) in party {PartyId} (origin {Origin}, run {RunId})",
            edgeId, sourcePersonaId, target.Kind, partyId,
            attribution?.Origin ?? StanceOrigin.Curator, attribution?.RunId);

        return edgeId;
    }

    public async Task<Guid?> RetractStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct)
    {
        var rows = await FetchStanceEdgesAsync(partyId, sourcePersonaId, ct);
        var original = rows.FirstOrDefault(r => r.Id == stanceId);
        if (original is null)
        {
            return null;
        }

        var target = original.TargetPersonaId is Guid tp
            ? tp == sourcePersonaId
                ? new StanceTargetSpec(StanceTargetKind.Self, null, null, null)
                : new StanceTargetSpec(StanceTargetKind.Participant, tp, null, null)
            : new StanceTargetSpec(
                StanceTargetKind.Concept, null, original.ConceptName, original.ConceptDisplay);

        // The reasoning is for the stance log, not for prompts — a retract-current target is
        // filtered out of `# Where you stand` entirely (RecallStancesAsync), so this text
        // never reaches a persona.
        var reasoning = $"Retracted: {original.Reasoning}";
        if (reasoning.Length > 600)
        {
            reasoning = reasoning[..600];
        }

        return await AppendStanceAsync(
            partyId, sourcePersonaId, target, valence: 0.0, reasoning,
            new StanceAttribution(StanceOrigin.Retract, RetractsId: stanceId), ct);
    }

    public async Task<IReadOnlyList<StanceRecord>> ListStancesAsync(
        Guid partyId,
        Guid sourcePersonaId,
        CancellationToken ct)
    {
        var rows = await FetchStanceEdgesAsync(partyId, sourcePersonaId, ct);

        // Newest-first already (ORDER BY ts DESC); the first row per target is current.
        return StanceProjection.WithCurrent(rows)
            .Select(r => StanceProjection.ToRecord(r.Row, sourcePersonaId, r.IsCurrent))
            .ToList();
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

        // Latest-wins spans Acquired (Participant-scope) and Intrinsic (Persona-scope) edges — ADR 0016.
        var rows = StanceProjection.Union(
            await FetchStanceEdgesAsync(partyId, sourcePersonaId, ct),
            await FetchIntrinsicStanceEdgesAsync(sourcePersonaId, ct));
        return StanceProjection.Project(rows, new HashSet<Guid>(presentPersonaIds), anchorText, limit);
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
                  RETURN s.id, s.valence, s.reasoning, s.ts, tgt.persona_id, tgt.name, tgt.display,
                         s.origin, s.run_id, s.retracts
                  ORDER BY s.ts DESC
                $cy$) AS (id text, valence text, reasoning text, ts text,
                          target_persona_id text, concept_name text, concept_display text,
                          origin text, run_id text, retracts text)
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

                // Absent origin = pre-attribution edge = curator (the only writer back then).
                var origin = Get(7) switch
                {
                    "consolidation" => StanceOrigin.Consolidation,
                    "retract" => StanceOrigin.Retract,
                    _ => StanceOrigin.Curator,
                };
                var runId = Guid.TryParse(Get(8), out var run) ? run : (Guid?)null;
                var retractsId = Guid.TryParse(Get(9), out var retr) ? retr : (Guid?)null;

                rows.Add(new StanceEdgeRow(
                    id, valence, reasoning, ts, targetPersonaId, conceptName, conceptDisplay,
                    origin, runId, retractsId));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // ─── Intrinsic Stances (ADR 0016, issue #91) ──────────────────────────────────────────

    public async Task<Guid> AppendIntrinsicStanceAsync(
        Guid personaId,
        StanceTargetSpec target,
        double valence,
        string reasoning,
        StanceAttribution? attribution,
        CancellationToken ct)
    {
        var edgeId = Guid.NewGuid();
        var nowIso = DateTimeOffset.UtcNow.ToString("o");
        var valenceLiteral = Math.Clamp(valence, -1.0, 1.0)
            .ToString("0.0###", CultureInfo.InvariantCulture);

        var provenance = attribution?.Origin switch
        {
            StanceOrigin.Consolidation => $", origin: 'consolidation', run_id: '{attribution.RunId}'",
            StanceOrigin.Retract => $", origin: 'retract', retracts: '{attribution.RetractsId}'",
            _ => string.Empty,
        };

        var edgeProps =
            $"{{ id: '{edgeId}', valence: {valenceLiteral}, reasoning: {CypherStr(reasoning)}, ts: '{nowIso}'{provenance} }}";

        string sql;
        if (target.Kind == StanceTargetKind.Concept)
        {
            var name = target.ConceptName ?? string.Empty;
            var display = target.ConceptDisplay ?? name;
            sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MERGE (src:Persona {id: '{{personaId}}'})
                  MERGE (c:Concept {name: {{CypherStr(name)}}})
                  SET c.display = coalesce(c.display, {{CypherStr(display)}})
                  CREATE (src)-[s:STANCE {{edgeProps}}]->(c)
                  RETURN s.id
                $cy$) AS (id ag_catalog.agtype)
                """;
        }
        else
        {
            var targetPersonaId = target.Kind == StanceTargetKind.Self
                ? personaId
                : target.PersonaId ?? throw new ArgumentException(
                    "Participant Stance target requires PersonaId.", nameof(target));
            sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MERGE (src:Persona {id: '{{personaId}}'})
                  MERGE (tgt:Persona {id: '{{targetPersonaId}}'})
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
            "Appended Intrinsic Stance {EdgeId} from persona {PersonaId} ({Kind}) (origin {Origin})",
            edgeId, personaId, target.Kind, attribution?.Origin ?? StanceOrigin.Curator);

        return edgeId;
    }

    public async Task<IReadOnlyList<StanceRecord>> ListIntrinsicStancesAsync(
        Guid personaId,
        CancellationToken ct)
    {
        var rows = await FetchIntrinsicStanceEdgesAsync(personaId, ct);

        return StanceProjection.WithCurrent(rows)
            .Select(r => StanceProjection.ToRecord(r.Row, personaId, r.IsCurrent, isIntrinsic: true))
            .ToList();
    }

    public async Task<Guid?> PromoteStanceAsync(
        Guid partyId,
        Guid sourcePersonaId,
        Guid stanceId,
        CancellationToken ct)
    {
        var rows = await FetchStanceEdgesAsync(partyId, sourcePersonaId, ct);
        var original = rows.FirstOrDefault(r => r.Id == stanceId);
        if (original is null)
        {
            return null;
        }

        // Re-target: Participant → underlying Persona (same id since Participant.persona_id
        // IS the Persona id). Concept and Self targets carry over unchanged.
        StanceTargetSpec intrinsicTarget;
        if (original.ConceptName is { Length: > 0 })
        {
            intrinsicTarget = new StanceTargetSpec(
                StanceTargetKind.Concept, null, original.ConceptName, original.ConceptDisplay);
        }
        else if (original.TargetPersonaId is Guid tp && tp == sourcePersonaId)
        {
            intrinsicTarget = new StanceTargetSpec(StanceTargetKind.Self, null, null, null);
        }
        else
        {
            // Participant target: re-point at the target's Persona (persona_id == Persona.id).
            var targetPersonaId = original.TargetPersonaId
                ?? throw new InvalidOperationException("Malformed edge: no target.");
            intrinsicTarget = new StanceTargetSpec(
                StanceTargetKind.Participant, targetPersonaId, null, null);
        }

        // Carry the source edge's provenance onto the promoted intrinsic edge so the
        // audit trail survives — a promoted Consolidation/Retract edge must not read as
        // curator-authored. Curator-origin edges keep attribution null.
        var attribution = original.Origin switch
        {
            StanceOrigin.Consolidation => new StanceAttribution(
                StanceOrigin.Consolidation, RunId: original.RunId),
            StanceOrigin.Retract => new StanceAttribution(
                StanceOrigin.Retract, RetractsId: original.RetractsId),
            _ => null,
        };

        return await AppendIntrinsicStanceAsync(
            sourcePersonaId, intrinsicTarget, original.Valence, original.Reasoning,
            attribution, ct);
    }

    // All Intrinsic STANCE edges from one Persona node, newest first. Mirrors
    // FetchStanceEdgesAsync but reads from (:Persona) and uses tgt.id for Persona targets.
    private async Task<IReadOnlyList<StanceEdgeRow>> FetchIntrinsicStanceEdgesAsync(
        Guid personaId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (src:Persona {id: '{{personaId}}'})-[s:STANCE]->(tgt)
                  RETURN s.id, s.valence, s.reasoning, s.ts, tgt.id, tgt.name, tgt.display,
                         s.origin, s.run_id, s.retracts
                  ORDER BY s.ts DESC
                $cy$) AS (id text, valence text, reasoning text, ts text,
                          target_persona_id text, concept_name text, concept_display text,
                          origin text, run_id text, retracts text)
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

                if (targetPersonaId is null && string.IsNullOrWhiteSpace(conceptName))
                {
                    continue;
                }

                var origin = Get(7) switch
                {
                    "consolidation" => StanceOrigin.Consolidation,
                    "retract" => StanceOrigin.Retract,
                    _ => StanceOrigin.Curator,
                };
                var runId = Guid.TryParse(Get(8), out var run) ? run : (Guid?)null;
                var retractsId = Guid.TryParse(Get(9), out var retr) ? retr : (Guid?)null;

                rows.Add(new StanceEdgeRow(
                    id, valence, reasoning, ts, targetPersonaId, conceptName, conceptDisplay,
                    origin, runId, retractsId));
            }

            return rows;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // ─── Consolidation v1 (ADR 0016) ──────────────────────────────────────────────────────

    // Cap on how many unconsolidated Recollections one run walks: keeps the proposer prompt
    // bounded on a huge backlog. The watermark advances only to the last *walked* edge, so
    // the remainder is picked up by the next run (gauge or button) — never silently skipped.
    private const int MaxRecollectionsPerRun = 50;

    public async Task<ConsolidationBatch> GetUnconsolidatedRecollectionsAsync(
        Guid partyId,
        Guid personaId,
        CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();

            // The watermark is the `ts` property on the Participant vertex (ADR 0016).
            // Read it first, then inline it as the walk's lower bound — ISO-8601 UTC strings
            // ("o" format) compare correctly as text, the same trick the recency ORDER BY
            // already leans on.
            DateTimeOffset? watermark = null;
            await using (var wmCmd = conn.CreateCommand())
            {
                wmCmd.CommandText = $$"""
                    SELECT * FROM cypher('memory', $cy$
                      MATCH (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
                      RETURN part.ts
                    $cy$) AS (ts text)
                    """;
                var raw = await wmCmd.ExecuteScalarAsync(ct);
                watermark = ParseIso(raw as string);
            }

            // Same "o"-with-offset shape the RECOLLECTS writes use ('…+00:00', never 'Z') so
            // the textual > stays a correct instant comparison.
            var where = watermark is DateTimeOffset wm
                ? $"WHERE r.ts > '{wm.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)}'"
                : string.Empty;
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MATCH (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(:Event)
                  {{where}}
                  RETURN r.id, r.snippet, r.ts, r.weight
                  ORDER BY r.ts ASC
                  LIMIT {{MaxRecollectionsPerRun.ToString(CultureInfo.InvariantCulture)}}
                $cy$) AS (id text, snippet text, ts text, weight text)
                """;

            var items = new List<UnconsolidatedRecollection>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = sql;
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    string? Get(int i) => reader.IsDBNull(i) ? null : reader.GetString(i);

                    var snippet = Get(1);
                    var ts = ParseIso(Get(2));
                    // Pre-ADR-0015 edges lack an id — they predate the watermark substrate
                    // too; per the no-migration-safety stance they are not consolidatable.
                    if (string.IsNullOrWhiteSpace(snippet) || ts is null
                        || !Guid.TryParse(Get(0), out var edgeId))
                    {
                        continue;
                    }
                    var weight = double.TryParse(Get(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                        ? Math.Clamp(w, 0.0, 1.0)
                        : SalienceMath.DefaultWeight;
                    items.Add(new UnconsolidatedRecollection(edgeId, snippet, ts.Value, weight));
                }
            }

            return new ConsolidationBatch(watermark, items);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public Task MarkConsolidatedAsync(Guid partyId, Guid personaId, ConsolidationBatch batch, CancellationToken ct)
        // The watermark is the last Recollection walked — not "now" — so in-flight captures survive.
        => batch.Items.Count == 0 ? Task.CompletedTask : AdvanceWatermarkAsync(partyId, personaId, batch.Items[^1].Ts, ct);

    private async Task AdvanceWatermarkAsync(
        Guid partyId,
        Guid personaId,
        DateTimeOffset watermark,
        CancellationToken ct)
    {
        // Keep the stored watermark in the exact format the RECOLLECTS `ts` writes use
        // (DateTimeOffset "o", '…+00:00') — the unconsolidated WHERE compares them as text.
        var iso = watermark.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            // MERGE so a first-ever consolidation of a Participant that only just appeared
            // doesn't depend on capture having materialised the vertex first. The CASE keeps
            // the watermark monotonic: two runs finishing out of order (e.g. across backend
            // instances, where the in-flight guard can't see each other) must not move it
            // backward and re-queue already-walked Recollections. ISO-"o" strings compare
            // correctly as text — same convention as the unconsolidated WHERE.
            var sql = $$"""
                SELECT * FROM cypher('memory', $cy$
                  MERGE (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})
                  SET part.ts = CASE
                    WHEN part.ts IS NULL OR part.ts < '{{iso}}' THEN '{{iso}}'
                    ELSE part.ts
                  END
                  RETURN part.persona_id
                $cy$) AS (persona_id ag_catalog.agtype)
                """;
            await ExecuteCypherAsync(db, sql, ct);
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
            var links = new List<MemoryGraphLink>();
            string PartNode(string personaId) => $"part:{personaId}:{partyId}";

            void AddNode(MemoryGraphNode node) => nodes.TryAdd(node.Id, node);

            async Task ReadAsync(string cypher, string columns, Action<Func<int, string?>> row)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT * FROM cypher('memory', $cy$ {cypher} $cy$) AS ({columns});";
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    row(i => reader.IsDBNull(i) ? null : reader.GetString(i));
                }
            }

            // One query per shape — the old single OPTIONAL MATCH chain multiplied concepts × recollections per Event.
            await ReadAsync(
                $"MATCH (e:Event {{party_id: '{partyId}'}}) " +
                "RETURN e.event_id, e.description, e.created_at, e.room_id, e.anchor_message_id, e.source, e.place_id",
                "event_id text, description text, created_at text, room_id text, anchor_message_id text, source text, place_id text",
                get =>
                {
                    if (get(0) is not { } eventId) return;
                    AddNode(new MemoryGraphNode(
                        $"event:{eventId}", "Event",
                        Description: get(1),
                        CreatedAt: get(2),
                        RoomId: get(3),
                        AnchorMessageId: int.TryParse(get(4), out var anchor) ? anchor : null,
                        Source: get(5),
                        PlaceId: get(6)));
                });

            await ReadAsync(
                $"MATCH (e:Event {{party_id: '{partyId}'}})-[:ABOUT]->(t) " +
                "RETURN e.event_id, t.name, t.display, t.persona_id, t.party_id",
                "event_id text, concept_name text, concept_display text, persona_id text, party_id text",
                get =>
                {
                    var eventNodeId = $"event:{get(0)}";
                    if (get(1) is { } conceptName)
                    {
                        var conceptNodeId = $"concept:{conceptName}";
                        AddNode(new MemoryGraphNode(conceptNodeId, "Concept", Display: get(2)));
                        links.Add(new MemoryGraphLink(eventNodeId, conceptNodeId, "ABOUT"));
                    }
                    else if (get(3) is { } personaId && get(4) == partyId.ToString())
                    {
                        AddNode(new MemoryGraphNode(PartNode(personaId), "Participant"));
                        links.Add(new MemoryGraphLink(eventNodeId, PartNode(personaId), "ABOUT"));
                    }
                });

            await ReadAsync(
                $"MATCH (p:Participant {{party_id: '{partyId}'}})-[r:RECOLLECTS]->(e:Event {{party_id: '{partyId}'}}) " +
                "RETURN p.persona_id, e.event_id, r.snippet, r.ts, r.weight, r.recall_count, r.last_recalled",
                "persona_id text, event_id text, snippet text, ts text, weight text, recall_count text, last_recalled text",
                get =>
                {
                    if (get(0) is not { } personaId || get(2) is not { } snippet) return;
                    AddNode(new MemoryGraphNode(PartNode(personaId), "Participant"));
                    links.Add(new MemoryGraphLink(
                        PartNode(personaId), $"event:{get(1)}", "RECOLLECTS",
                        Snippet: snippet, Ts: get(3),
                        Weight: double.TryParse(get(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : null,
                        RecallCount: int.TryParse(get(5), out var rc) ? rc : null,
                        LastRecalled: get(6)));
                });

            // Current belief per (author, target) — latest-wins, retract-current targets hold none (ADR 0016).
            var stances = new List<(string Author, StanceEdgeRow Row)>();
            await ReadAsync(
                $"MATCH (src:Participant {{party_id: '{partyId}'}})-[s:STANCE]->(tgt) " +
                "RETURN src.persona_id, s.valence, s.reasoning, s.ts, tgt.persona_id, tgt.name, tgt.display, s.origin, s.id",
                "author text, valence text, reasoning text, ts text, target_persona_id text, concept_name text, concept_display text, origin text, id text",
                get =>
                {
                    if (get(0) is not { } author) return;
                    var targetPersonaId = Guid.TryParse(get(4), out var tp) ? tp : (Guid?)null;
                    if (targetPersonaId is null && string.IsNullOrWhiteSpace(get(5))) return;
                    var valence = double.TryParse(get(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                        ? Math.Clamp(v, -1.0, 1.0)
                        : 0.0;
                    var origin = get(7) == "retract" ? StanceOrigin.Retract : StanceOrigin.Curator;
                    stances.Add((author, new StanceEdgeRow(
                        Guid.TryParse(get(8), out var id) ? id : Guid.Empty, valence, get(2) ?? string.Empty,
                        ParseIso(get(3)) ?? DateTimeOffset.MinValue, targetPersonaId, get(5), get(6), origin, null, null)));
                });

            foreach (var byAuthor in stances.GroupBy(s => s.Author))
            {
                var newestFirst = byAuthor.Select(s => s.Row).OrderByDescending(r => r.Ts);
                foreach (var (row, isCurrent) in StanceProjection.WithCurrent(newestFirst))
                {
                    if (!isCurrent || row.Origin == StanceOrigin.Retract) continue;
                    string targetNodeId;
                    if (row.TargetPersonaId is Guid targetPersona)
                    {
                        targetNodeId = PartNode(targetPersona.ToString());
                        AddNode(new MemoryGraphNode(targetNodeId, "Participant"));
                    }
                    else
                    {
                        targetNodeId = $"concept:{row.ConceptName}";
                        AddNode(new MemoryGraphNode(targetNodeId, "Concept", Display: row.ConceptDisplay));
                    }
                    AddNode(new MemoryGraphNode(PartNode(byAuthor.Key), "Participant"));
                    links.Add(new MemoryGraphLink(
                        PartNode(byAuthor.Key), targetNodeId, "STANCE",
                        Ts: row.Ts.ToString("o"), Valence: row.Valence, Reasoning: row.Reasoning));
                }
            }

            return new MemoryGraphDto(nodes.Values.ToList(), links);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // The persona's newest snippets — the recency-capped dedup pool for substituted drafts.
    private static async Task<List<string>> ListRecentSnippetsAsync(
        AppDbContext db, Guid partyId, Guid personaId, CancellationToken ct)
    {
        var sql = $$"""
            SELECT * FROM cypher('memory', $cy$
              MATCH (part:Participant {persona_id: '{{personaId}}', party_id: '{{partyId}}'})-[r:RECOLLECTS]->(:Event)
              RETURN r.snippet AS snippet
              ORDER BY r.ts DESC
              LIMIT 30
            $cy$) AS (snippet text)
            """;
        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = (NpgsqlTransaction?)db.Database.CurrentTransaction?.GetDbTransaction();

        var snippets = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
                snippets.Add(reader.GetString(0));
        }
        return snippets;
    }


}
