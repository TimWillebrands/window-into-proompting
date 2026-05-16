using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    private const string Graph = "memory";

    public async Task<MemoryCaptureResult> CaptureMomentAsync(
        Guid partyId,
        Guid roomId,
        int messageId,
        IReadOnlyList<ParticipantSnapshot> presentParticipants,
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

        var recollectionTargets = presentParticipants.Where(p => !p.IsUser).ToList();
        var recollectionTasks = recollectionTargets.Select(p =>
            extractor.ExtractRecollectionAsync(p.Name, sourceMessage, sourceAuthor, recentContext, ResolveName, ct)
                .ContinueWith(t => (Participant: p, Snippet: t.Result), ct, TaskContinuationOptions.None, TaskScheduler.Default));
        var recollections = (await Task.WhenAll(recollectionTasks))
            .Where(r => !string.IsNullOrWhiteSpace(r.Snippet))
            .ToList();

        var eventId = Guid.NewGuid();
        var nowIso = DateTimeOffset.UtcNow.ToString("o");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // AGE registers operators per session; subsequent statements in this transaction
        // (Cypher cast through agtype) rely on it. The 05-age-setup.sql init script sets
        // search_path at the database level so qualified calls keep working without a re-LOAD.
        await db.Database.ExecuteSqlRawAsync("LOAD 'age';", ct);

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

    private static Task CreateEventAsync(
        AppDbContext db,
        Guid partyId, Guid roomId, int messageId,
        Guid eventId, string description, string nowIso,
        CancellationToken ct)
    {
        var paramsJson = JsonSerializer.Serialize(new
        {
            partyId = partyId.ToString(),
            roomId = roomId.ToString(),
            messageId,
            eventId = eventId.ToString(),
            description,
            createdAt = nowIso,
        });

        const string sql = """
            SELECT * FROM cypher('memory', $$
              MERGE (party:Party {id: $partyId})
              MERGE (room:Room {id: $roomId})
              MERGE (msg:Message {room_id: $roomId, id: $messageId})
              CREATE (e:Event {event_id: $eventId, description: $description, created_at: $createdAt, anchor_message_id: $messageId})
              CREATE (e)-[:ANCHORED_TO]->(msg)
              RETURN e.event_id
            $$, {0}::agtype) AS (event_id ag_catalog.agtype)
            """;
        return db.Database.ExecuteSqlRawAsync(sql, new object[] { paramsJson }, ct);
    }

    private static Task AddConceptEdgeAsync(AppDbContext db, Guid eventId, ConceptTag concept, CancellationToken ct)
    {
        var paramsJson = JsonSerializer.Serialize(new
        {
            eventId = eventId.ToString(),
            name = concept.Name,
            display = concept.Display,
        });

        const string sql = """
            SELECT * FROM cypher('memory', $$
              MATCH (e:Event {event_id: $eventId})
              MERGE (c:Concept {name: $name})
              ON CREATE SET c.display = $display
              CREATE (e)-[:ABOUT]->(c)
              RETURN c.name
            $$, {0}::agtype) AS (concept_name ag_catalog.agtype)
            """;
        return db.Database.ExecuteSqlRawAsync(sql, new object[] { paramsJson }, ct);
    }

    private static Task AddAboutParticipantEdgeAsync(AppDbContext db, Guid eventId, Guid partyId, Guid participantPersonaId, CancellationToken ct)
    {
        var paramsJson = JsonSerializer.Serialize(new
        {
            eventId = eventId.ToString(),
            partyId = partyId.ToString(),
            personaId = participantPersonaId.ToString(),
        });

        const string sql = """
            SELECT * FROM cypher('memory', $$
              MATCH (e:Event {event_id: $eventId})
              MERGE (party:Party {id: $partyId})
              MERGE (p:Participant {persona_id: $personaId, party_id: $partyId})
              MERGE (p)-[:IN_PARTY]->(party)
              CREATE (e)-[:ABOUT]->(p)
              RETURN p.persona_id
            $$, {0}::agtype) AS (persona_id ag_catalog.agtype)
            """;
        return db.Database.ExecuteSqlRawAsync(sql, new object[] { paramsJson }, ct);
    }

    private static Task AddRecollectionAsync(
        AppDbContext db,
        Guid eventId, Guid partyId, Guid personaId, string snippet, string nowIso,
        CancellationToken ct)
    {
        var paramsJson = JsonSerializer.Serialize(new
        {
            eventId = eventId.ToString(),
            partyId = partyId.ToString(),
            personaId = personaId.ToString(),
            snippet,
            ts = nowIso,
        });

        const string sql = """
            SELECT * FROM cypher('memory', $$
              MATCH (e:Event {event_id: $eventId})
              MERGE (party:Party {id: $partyId})
              MERGE (persona:Persona {id: $personaId})
              MERGE (part:Participant {persona_id: $personaId, party_id: $partyId})
              MERGE (persona)-[:HAS_PARTICIPANT]->(part)
              MERGE (part)-[:IN_PARTY]->(party)
              CREATE (part)-[:RECOLLECTS {snippet: $snippet, ts: $ts}]->(e)
              RETURN part.persona_id
            $$, {0}::agtype) AS (persona_id ag_catalog.agtype)
            """;
        return db.Database.ExecuteSqlRawAsync(sql, new object[] { paramsJson }, ct);
    }
}
