using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PartyTown.Data;

using static PartyTown.Services.Memory.MemoryGraphWrites;

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
/// Scene-commit seeder: writes the same graph shapes as Capture via <see cref="MemoryGraphWrites"/>,
/// but with the reviewed draft's ids, timestamps and weights — no LLM anywhere.
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

            await CreateEventAsync(db, new EventVertex(ev.EventId, ev.Description, atIso, partyId, roomId,
                MomentSources.Import, ev.AnchorOrdinal), ct);

            foreach (var personaId in ev.RecollectorPersonaIds)
            {
                // Import has no "about" extraction: the recollectors are who the episode is about.
                await AddAboutParticipantEdgeAsync(db, ev.EventId, partyId, personaId, ct);
                // The imported snippet is the description itself — commit has no per-persona perspective call.
                await AddRecollectionAsync(db, ev.EventId, partyId, personaId,
                    RecollectionId(ev.EventId, personaId), ev.Description, atIso, ev.Weight, ct);
                recollections++;
            }

            foreach (var concept in ev.Concepts)
            {
                var name = MemoryExtractor.NormaliseConceptName(concept.Name);
                if (name.Length == 0) continue;
                await AddConceptEdgeAsync(db, ev.EventId, name, concept.Display, ct);
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
}
