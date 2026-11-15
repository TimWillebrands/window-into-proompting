using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;

namespace PartyTown.World;

public sealed class LullCaptureGrain(
    [PersistentState(stateName: "lullcapture", storageName: "parties")]
    IPersistentState<LullCaptureState> state,
    WorldMemory memory,
    ISystemOneJudge judge,
    ILogger<LullCaptureGrain> logger)
    : Grain, ILullCaptureGrain
{
    private const double MemorableThreshold = 0.35;

    private readonly HashSet<Guid> _capturing = new();

    private Guid PartyId => this.GetPrimaryKey();

    public async Task AdoptCursorsAsync(Dictionary<Guid, int> cursors)
    {
        foreach (var (thread, cursor) in cursors)
            state.State.Cursors.TryAdd(thread, cursor);
        await state.WriteStateAsync();
    }

    public async Task SweepAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var threads = await GrainFactory.GetGrain<IPartyGrain>(PartyId).GetChatGroups();
        var moved = false;
        foreach (var thread in threads)
        {
            if (_capturing.Contains(thread.Id)) continue;
            var room = GrainFactory.GetGrain<IChatGroupGrain>(thread.Id);
            var cursor = state.State.Cursors.TryGetValue(thread.Id, out var c) ? c : (int?)null;
            switch (ClockPolicy.Lulled(await room.GetRecentMessagesAsync(14), cursor, now))
            {
                case LullDecision.Seed seed:
                    state.State.Cursors[thread.Id] = seed.Cursor;
                    moved = true;
                    break;
                case LullDecision.Capture capture:
                    state.State.Cursors[thread.Id] = capture.Cursor;
                    moved = true;
                    _capturing.Add(thread.Id);
                    _ = CaptureAsync(thread, capture.Anchor, capture.Context, ClockPolicy.Witnesses(capture.Context, (await room.GetCastAsync()).Views()));
                    break;
            }
        }
        if (moved) await state.WriteStateAsync();
    }

    private async Task CaptureAsync(ChatGroupInfo thread, ChatMessage anchor, List<ChatMessage> context, List<ParticipantView> present)
    {
        try
        {
            if (!await IsMemorableAsync(context, present))
                return;
            var placeId = thread.Kind == ChatGroupKinds.Place
                ? await GrainFactory.GetGrain<Places.IPlacesGrain>(PartyId).GetPlaceOfTalkRoomAsync(thread.Id)
                : null;
            var result = await memory.CaptureAsync(
                Moment.FromMessages(PartyId, MomentSources.OfKind(thread.Kind), thread.Id, context, anchor.MessageId, present, placeId),
                CancellationToken.None);
            if (result is { EventCreated: true, RecollectionsCreated: > 0 })
            {
                var where = thread.Kind switch
                {
                    ChatGroupKinds.Direct => $"the chat {thread.Name}",
                    ChatGroupKinds.Place => $"talk at {thread.Name}",
                    _ => $"#{thread.Name}",
                };
                await GrainFactory.GetGrain<IWorldClockGrain>(PartyId).RecordAsync(new WorldActivity
                {
                    App = "memory", Action = "capture", PersonaName = "World", TargetId = thread.Id,
                    Summary = $"a moment in {where} became a memory for {result.RecollectionsCreated} people",
                });
            }
        }
        finally
        {
            _capturing.Remove(thread.Id);
        }
    }

    private async Task<bool> IsMemorableAsync(List<ChatMessage> context, List<ParticipantView> present)
    {
        var names = present.ToDictionary(p => p.Id, p => p.Name);
        var transcript = string.Join("\n", context.Select(m => $"{names.GetValueOrDefault(m.SenderId, "someone")}: {m.Content}"));
        try
        {
            var answers = await judge.AskAsync(transcript, new Dictionary<string, SystemOneQuestion>
            {
                ["memorable"] = new NoulQuestion("Something happened in this conversation that the people in it would still remember a week from now."),
            }, CancellationToken.None);
            var p = answers.Noul("memorable", 1);
            logger.LogInformation("Capture gate p(memorable)={P:F2}", p);
            return p >= MemorableThreshold;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "System One capture gate failed; capturing anyway");
            return true;
        }
    }
}

[Alias("PartyTown.World.ILullCaptureGrain")]
public interface ILullCaptureGrain : IGrainWithGuidKey
{
    [Alias("SweepAsync")] Task SweepAsync();
    [Alias("AdoptCursorsAsync")] Task AdoptCursorsAsync(Dictionary<Guid, int> cursors);
}

[GenerateSerializer, Alias("PartyTown.World.LullCaptureState")]
public sealed class LullCaptureState
{
    [Id(0)] public Dictionary<Guid, int> Cursors { get; set; } = new();
}
