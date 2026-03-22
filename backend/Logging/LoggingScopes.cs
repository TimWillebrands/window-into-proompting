namespace PartyTown.Logging;

public static class LoggingScopes
{
    public static IDisposable? BeginGrainScope(this ILogger logger, string grainType, string grainKey)
        => logger.BeginScope("GrainType={GrainType} GrainKey={GrainKey}", grainType, grainKey);

    public static IDisposable? BeginPartyScope(this ILogger logger, Guid partyId)
        => logger.BeginScope("PartyId={PartyId}", partyId);

    public static IDisposable? BeginMessageScope(this ILogger logger, Guid chatGroupId, int messageId)
        => logger.BeginScope("ChatGroupId={ChatGroupId} MessageId={MessageId}", chatGroupId, messageId);

    public static IDisposable? BeginGenerationScope(this ILogger logger, string model, string provider, Guid? personaId = null)
        => personaId.HasValue
            ? logger.BeginScope("LlmModel={LlmModel} LlmProvider={LlmProvider} PersonaId={PersonaId}", model, provider, personaId.Value)
            : logger.BeginScope("LlmModel={LlmModel} LlmProvider={LlmProvider}", model, provider);
}
