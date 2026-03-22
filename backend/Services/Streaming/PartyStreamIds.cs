namespace PartyTown.Services.Streaming;

/// <summary>
/// Stream namespace and key identifiers for Orleans streaming.
/// Used by <see cref="PartyGrain"/> to publish events and by <see cref="PartyRealtimeHub"/> to subscribe.
/// </summary>
public static class PartyStreamIds
{
    /// <summary>Orleans stream provider name registered in silo configuration.</summary>
    public const string Provider = "party-streams";

    /// <summary>Namespace for party-level events (messages created, deleted, generation started).</summary>
    public const string PartyEventsNamespace = "party-events";

    /// <summary>Generates the stream key for a party's event stream.</summary>
    public static string PartyEventId(Guid partyId) => partyId.ToString("N");
}
