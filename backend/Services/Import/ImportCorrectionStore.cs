using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using PartyTown.Data;

namespace PartyTown.Services.Import;

/// <summary>
/// One correction-ledger entry (ADR 0017): what the human changed between the extractor's
/// suggestion and the committed final, plus regeneration notes. Capture only — the first
/// consumer is the Bench (corrections as extraction ground truth), nothing learns online.
/// </summary>
public sealed record ImportCorrection
{
    public Guid Id { get; init; }
    public Guid SessionId { get; init; }
    public Guid SceneId { get; init; }
    public Guid PartyId { get; init; }
    public Guid RoomId { get; init; }

    /// <summary>Null for scene-level entries (regenerated-with-note).</summary>
    public Guid? ItemId { get; init; }

    /// <summary>One of <see cref="CorrectionKinds"/>.</summary>
    public string Kind { get; init; } = string.Empty;

    public List<int> ChunkRefs { get; init; } = new();

    /// <summary>JSON snapshot {summary, weight, routing, routingReason} as suggested.</summary>
    public string? Suggested { get; init; }

    /// <summary>JSON snapshot of the human-final values at commit.</summary>
    public string? Final { get; init; }

    /// <summary>The operator's regeneration note, when the kind carries one.</summary>
    public string? Note { get; init; }

    public DateTimeOffset CommittedAt { get; init; }
}

/// <summary>Durable correction ledger. Lives in a plain Postgres table — the import
/// session grain is disposable, the labels are not.</summary>
public interface IImportCorrectionStore
{
    Task AppendAsync(IReadOnlyList<ImportCorrection> corrections, CancellationToken ct);
    Task<IReadOnlyList<ImportCorrection>> ListAsync(Guid sessionId, CancellationToken ct);
}

/// <summary>
/// Postgres-backed store. Schema is ensured lazily (CREATE TABLE IF NOT EXISTS) so tests
/// and bare containers work without an initdb script; appends are idempotent by
/// deterministic id (ON CONFLICT DO NOTHING) so a retried commit never double-books.
/// Plain SQL with Npgsql parameters — the cypher() bare-Param footgun does not apply here.
/// </summary>
public sealed class ImportCorrectionStore(
    IDbContextFactory<AppDbContext> dbFactory,
    ILogger<ImportCorrectionStore> logger) : IImportCorrectionStore
{
    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS import_corrections (
            id uuid PRIMARY KEY,
            session_id uuid NOT NULL,
            scene_id uuid NOT NULL,
            party_id uuid NOT NULL,
            room_id uuid NOT NULL,
            item_id uuid,
            kind text NOT NULL,
            chunk_refs integer[] NOT NULL DEFAULT '{}',
            suggested jsonb,
            final jsonb,
            note text,
            committed_at timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_import_corrections_session ON import_corrections (session_id);
        """;

    private readonly SemaphoreSlim _schemaGate = new(1, 1);
    private bool _schemaEnsured;

    public async Task AppendAsync(IReadOnlyList<ImportCorrection> corrections, CancellationToken ct)
    {
        if (corrections.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await EnsureSchemaAsync(conn, ct);

            foreach (var c in corrections)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO import_corrections
                        (id, session_id, scene_id, party_id, room_id, item_id, kind,
                         chunk_refs, suggested, final, note, committed_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
                    ON CONFLICT (id) DO NOTHING
                    """;
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.Id });
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.SessionId });
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.SceneId });
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.PartyId });
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.RoomId });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)c.ItemId ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Uuid });
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.Kind });
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.ChunkRefs.ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)c.Suggested ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)c.Final ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Jsonb });
                cmd.Parameters.Add(new NpgsqlParameter { Value = (object?)c.Note ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
                cmd.Parameters.Add(new NpgsqlParameter { Value = c.CommittedAt });
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        logger.LogInformation("Correction ledger: appended {Count} entries", corrections.Count);
    }

    public async Task<IReadOnlyList<ImportCorrection>> ListAsync(Guid sessionId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var conn = (NpgsqlConnection)db.Database.GetDbConnection();
            await EnsureSchemaAsync(conn, ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, session_id, scene_id, party_id, room_id, item_id, kind,
                       chunk_refs, suggested::text, final::text, note, committed_at
                  FROM import_corrections
                 WHERE session_id = $1
                 ORDER BY committed_at, kind
                """;
            cmd.Parameters.Add(new NpgsqlParameter { Value = sessionId });

            var result = new List<ImportCorrection>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new ImportCorrection
                {
                    Id = reader.GetGuid(0),
                    SessionId = reader.GetGuid(1),
                    SceneId = reader.GetGuid(2),
                    PartyId = reader.GetGuid(3),
                    RoomId = reader.GetGuid(4),
                    ItemId = reader.IsDBNull(5) ? null : reader.GetGuid(5),
                    Kind = reader.GetString(6),
                    ChunkRefs = reader.GetFieldValue<int[]>(7).ToList(),
                    Suggested = reader.IsDBNull(8) ? null : reader.GetString(8),
                    Final = reader.IsDBNull(9) ? null : reader.GetString(9),
                    Note = reader.IsDBNull(10) ? null : reader.GetString(10),
                    CommittedAt = reader.GetFieldValue<DateTimeOffset>(11),
                });
            }
            return result;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task EnsureSchemaAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (_schemaEnsured) return;
        await _schemaGate.WaitAsync(ct);
        try
        {
            if (_schemaEnsured) return;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = Ddl;
            await cmd.ExecuteNonQueryAsync(ct);
            _schemaEnsured = true;
        }
        finally
        {
            _schemaGate.Release();
        }
    }
}
