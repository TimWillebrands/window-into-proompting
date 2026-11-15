namespace PartyTown.Services.Streaming;

public static class PartyStreamIds
{
    public const string Provider = "party-streams";

    public const string PartyEventsNamespace = "party-events";

    public static string PartyEventId(Guid partyId) => partyId.ToString("N");
}
