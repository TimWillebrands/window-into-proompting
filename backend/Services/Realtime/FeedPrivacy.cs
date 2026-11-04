using System.Text.Json;
using PartyTown.Services.Auth;
using PartyTown.World;

namespace PartyTown.Services.Realtime;

/// <summary>Decides per frame who on the shared party feed may receive it.</summary>
public static class FeedPrivacy
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static bool CanSee(Viewer viewer, RealtimeFrame frame, IReadOnlyCollection<Guid>? privateMembers, IReadOnlySet<Guid> humans) => frame.Data switch
    {
        IRoomFrame => AccessRules.CanSeeRoom(viewer, privateMembers),
        PartyWorldEventData { Kind: AccessRules.DiaryApp } => viewer.SignedIn,
        PartyWorldEventData { Kind: "activity" } world => AccessRules.CanSeeActivity(viewer, ActivityOf(world), humans),
        _ => true,
    };

    public static WorldActivity ActivityOf(PartyWorldEventData world)
        => world.Payload.Deserialize<WorldActivity>(Web) ?? new WorldActivity();
}
