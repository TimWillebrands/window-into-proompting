using System.Net.WebSockets;
using PartyTown.Services.Auth;

namespace PartyTown.Services.Realtime;

public interface IPartyRealtimeHub
{
    Task HandleConnectionAsync(Guid partyId, WebSocket socket, Viewer viewer, CancellationToken cancellationToken);
}
