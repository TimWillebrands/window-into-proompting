using System.Diagnostics;

namespace PartyTown.Logging;

public static class Tracing
{
    public const string PersonaSourceName = "Proompting.Persona";

    public static readonly ActivitySource Persona = new(PersonaSourceName);
}
