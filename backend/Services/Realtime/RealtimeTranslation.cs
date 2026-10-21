using System.Text.Json;
using PartyTown.Services.Streaming;

namespace PartyTown.Services.Realtime;

/// <summary>One outgoing frame before sequencing: envelope type + payload.</summary>
public readonly record struct RealtimeFrame(string Type, IPartyRealtimeData Data);

/// <summary>Pure party stream-event → wire-envelope translation; the hub only sequences and sends.</summary>
public static class RealtimeTranslation
{
    public static IReadOnlyList<RealtimeFrame> Party(Guid partyId, PartyStreamEvent evt) => evt.Type switch
    {
        "message" => One("party.message.created", new PartyMessageCreatedData(partyId, evt.ChatGroupId, evt.Message)),
        "deleteMessage" => One("party.message.deleted", new PartyMessageDeletedData(partyId, evt.ChatGroupId, evt.MessageId)),
        "deleteMessagesAfter" => One("party.messages.truncated", new PartyMessagesTruncatedData(partyId, evt.ChatGroupId, evt.MessageId)),
        "messageEvent" when evt is { MessageId: { } id, MessageEvent: { } delta } => Delta(partyId, evt.ChatGroupId, id, delta),
        "raceEvaluation" when evt.RaceEvaluation is { } race =>
            One("party.race.evaluation", new PartyRaceEvaluationData(partyId, evt.ChatGroupId, race)),
        "world" when evt.WorldKind is { } kind => One($"world.{kind}", new PartyWorldEventData(partyId, kind, Json(evt.WorldJson))),
        _ => One("party.event", new PartyUnknownEventData(partyId, evt.ChatGroupId, evt.Type, evt.Message, evt.MessageId)),
    };

    private static IReadOnlyList<RealtimeFrame> Delta(Guid partyId, Guid chatGroupId, int messageId, MessageStreamEvent delta)
    {
        var frames = new List<RealtimeFrame>
        {
            new("party.generation.delta", new PartyGenerationDeltaData(partyId, chatGroupId, messageId, delta.Event, delta.Data, delta.Done)),
        };
        if (delta.Done)
            frames.Add(new("party.generation.completed", new PartyGenerationCompletedData(partyId, chatGroupId, messageId)));
        return frames;
    }

    private static IReadOnlyList<RealtimeFrame> One(string type, IPartyRealtimeData data) => new[] { new RealtimeFrame(type, data) };

    private static JsonElement Json(string? json)
    {
        using var doc = JsonDocument.Parse(json ?? "null");
        return doc.RootElement.Clone();
    }
}
