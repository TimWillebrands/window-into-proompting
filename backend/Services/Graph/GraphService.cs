using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace PartyTown.Services.Graph;

public sealed partial class GraphService(GraphDbContext db) : IGraphService
{
    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex IdentifierRegex();

    public async Task EnsureGraphAsync(string graphName, CancellationToken ct = default)
    {
        ValidateIdentifier(graphName);
        var conn = await OpenAsync(ct);
        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT 1 FROM ag_catalog.ag_graph WHERE name = @n";
        AddText(check, "n", graphName);
        if (await check.ExecuteScalarAsync(ct) is not null) return;

        await using var create = conn.CreateCommand();
        create.CommandText = "SELECT ag_catalog.create_graph(@n)";
        AddText(create, "n", graphName);
        await create.ExecuteNonQueryAsync(ct);
    }

    public async Task DropGraphAsync(string graphName, CancellationToken ct = default)
    {
        ValidateIdentifier(graphName);
        var conn = await OpenAsync(ct);
        await using var check = conn.CreateCommand();
        check.CommandText = "SELECT 1 FROM ag_catalog.ag_graph WHERE name = @n";
        AddText(check, "n", graphName);
        if (await check.ExecuteScalarAsync(ct) is null) return;

        await using var drop = conn.CreateCommand();
        drop.CommandText = "SELECT ag_catalog.drop_graph(@n, true)";
        AddText(drop, "n", graphName);
        await drop.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> ExecuteAsync(
        string graphName, string cypher, object? parameters = null, CancellationToken ct = default)
    {
        var sql = BuildCypherSql(graphName, cypher, ["v"], parameters is not null);
        var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (parameters is not null) AddParamsArg(cmd, parameters);
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    public async IAsyncEnumerable<Agtype[]> QueryAsync(
        string graphName,
        string cypher,
        IReadOnlyList<string> columns,
        object? parameters = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (columns.Count == 0)
            throw new ArgumentException("At least one return column is required.", nameof(columns));
        foreach (var c in columns) ValidateIdentifier(c);

        var sql = BuildCypherSql(graphName, cypher, columns, parameters is not null);
        var conn = await OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (parameters is not null) AddParamsArg(cmd, parameters);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            var row = new Agtype[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                row[i] = rdr.IsDBNull(i) ? Agtype.Null : new Agtype(rdr.GetString(i));
            yield return row;
        }
    }

    private async Task<DbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        return conn;
    }

    private static string BuildCypherSql(
        string graphName, string cypher, IReadOnlyList<string> columns, bool withParams)
    {
        ValidateIdentifier(graphName);

        // Use a tag the cypher body is unlikely to contain. Reject if it does.
        const string tag = "ageq";
        if (cypher.Contains($"${tag}$", StringComparison.Ordinal))
            throw new ArgumentException(
                $"Cypher body contains the reserved dollar-quote tag '${tag}$'.", nameof(cypher));

        var paramsArg = withParams ? ", @p::agtype" : "";
        var asList = string.Join(", ", columns.Select(c => $"{c} agtype"));
        var selectList = string.Join(", ", columns.Select(c => $"{c}::text"));

        return $"SELECT {selectList} FROM ag_catalog.cypher('{graphName}', " +
               $"${tag}${cypher}${tag}${paramsArg}) AS ({asList})";
    }

    private static void AddParamsArg(DbCommand cmd, object parameters)
    {
        var json = parameters as string ?? JsonSerializer.Serialize(parameters);
        AddText(cmd, "p", json);
    }

    private static void AddText(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = DbType.String;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static void ValidateIdentifier(string name)
    {
        if (!IdentifierRegex().IsMatch(name))
            throw new ArgumentException(
                $"Invalid AGE identifier '{name}'. Must match [a-zA-Z_][a-zA-Z0-9_]*.", nameof(name));
    }
}
