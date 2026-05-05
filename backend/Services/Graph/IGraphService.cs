namespace PartyTown.Services.Graph;

/// <summary>
/// Thin Cypher runner over Apache AGE. The <paramref name="cypher"/> body is
/// embedded verbatim in the SQL via dollar-quoting — never concatenate
/// untrusted input into it. Pass user values through <c>parameters</c>
/// instead and reference them in the body as <c>$name</c>.
/// </summary>
public interface IGraphService
{
    /// <summary>Creates the named graph if it doesn't already exist.</summary>
    Task EnsureGraphAsync(string graphName, CancellationToken ct = default);

    /// <summary>Drops the named graph (and all its data) if it exists.</summary>
    Task DropGraphAsync(string graphName, CancellationToken ct = default);

    /// <summary>
    /// Runs a write-only Cypher statement. The query must still <c>RETURN</c>
    /// at least one column (AGE constraint); the returned rows are discarded.
    /// </summary>
    Task<int> ExecuteAsync(
        string graphName,
        string cypher,
        object? parameters = null,
        CancellationToken ct = default);

    /// <summary>
    /// Runs a Cypher query and streams rows. <paramref name="columns"/> must
    /// match the names listed after <c>RETURN</c>; each result column comes
    /// back as a single <see cref="Agtype"/> wrapping its JSON text form.
    /// </summary>
    IAsyncEnumerable<Agtype[]> QueryAsync(
        string graphName,
        string cypher,
        IReadOnlyList<string> columns,
        object? parameters = null,
        CancellationToken ct = default);
}
