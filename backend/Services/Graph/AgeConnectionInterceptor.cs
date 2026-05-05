using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PartyTown.Services.Graph;

/// <summary>
/// Loads the AGE extension and pins <c>ag_catalog</c> on the search path for
/// every pooled connection. Both must be set per-session: <c>LOAD 'age'</c>
/// because Npgsql pooling reuses backends across requests, and search_path
/// because some Aspire/Npgsql defaults reset it.
/// </summary>
public sealed class AgeConnectionInterceptor : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "LOAD 'age'; SET search_path = ag_catalog, \"$user\", public;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "LOAD 'age'; SET search_path = ag_catalog, \"$user\", public;";
        cmd.ExecuteNonQuery();
    }
}
