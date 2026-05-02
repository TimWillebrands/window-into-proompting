using System.Diagnostics;

namespace PartyTown.Logging;

/// <summary>
/// Custom <see cref="ActivitySource"/>s for Proompting. Spans emitted here are
/// exported to the Aspire dashboard via the OTLP exporter wired in Program.cs.
///
/// Keep tag names aligned with <see cref="LoggingScopes"/> so an operator filtering
/// the dashboard on <c>party.id=…</c> sees the same key in span attributes and log scopes.
/// </summary>
public static class Tracing
{
    public const string PersonaSourceName = "Proompting.Persona";

    public static readonly ActivitySource Persona = new(PersonaSourceName);
}
