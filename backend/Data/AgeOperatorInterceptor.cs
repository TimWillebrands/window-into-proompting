using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PartyTown.Data;

/// <summary>
/// Loads the Apache AGE extension into every freshly-opened database session.
/// AGE registers its <c>cypher()</c> function and the <c>agtype</c> operators as a
/// <em>session-local</em> side effect of <c>LOAD 'age';</c> — running it once per
/// connection (or per pooled-connection checkout) keeps all downstream Cypher callers
/// honest without each having to remember to load AGE themselves.
/// </summary>
/// <remarks>
/// Registered as a <see cref="DbConnectionInterceptor"/> on <see cref="AppDbContext"/>
/// (see <c>Program.cs</c>). LOAD is idempotent and cheap (microseconds), so re-running
/// it on every pool checkout is fine. Connection lifecycle (open/close/keep-open across
/// commands) stays the caller's concern — this interceptor only owns the AGE bring-up.
/// The <c>search_path</c> needed to resolve <c>ag_catalog.agtype</c> casts is set at the
/// database level by <c>docker-entrypoint-initdb.d/05-age-setup.sql</c>, so it survives
/// across sessions without us re-setting it here.
/// </remarks>
public sealed class AgeOperatorInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "LOAD 'age';";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "LOAD 'age';";
        cmd.ExecuteNonQuery();
    }
}
