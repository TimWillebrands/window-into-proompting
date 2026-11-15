using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PartyTown.Data;

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
