using System.Net.WebSockets;

namespace PartyTown.Services.Realtime;

/// <summary>
/// Manages realtime connections for a specific party, fanning out grain events to all connected websocket clients.
/// </summary>
public interface IPartyRealtimeHub
{
    /// <summary>
    /// Handles a new websocket connection for the given party.
    /// The hub manages subscription lifecycle, broadcasts events to all clients, and cleans up on disconnect.
    /// </summary>
    Task HandleConnectionAsync(Guid partyId, WebSocket socket, CancellationToken cancellationToken);
}
