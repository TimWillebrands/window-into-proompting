using System.Text.Json;
using PartyTown.Model;
using PartyTown.Services.Realtime;
using PartyTown.Services.Streaming;

namespace BackendTest;

/// <summary>Pure stream-event → envelope translation (no socket, no cluster).</summary>
public class RealtimeTranslationTest
{
    private static readonly Guid Party = Guid.NewGuid();
    private static readonly Guid Room = Guid.NewGuid();

    [Fact]
    public void Message_BecomesMessageCreated()
    {
        var message = new ChatMessage { MessageId = 4, ChatGroupId = Room, Content = "hi" };

        var frame = Assert.Single(RealtimeTranslation.Party(Party, new PartyStreamEvent { Type = "message", ChatGroupId = Room, Message = message }));

        Assert.Equal("party.message.created", frame.Type);
        var data = Assert.IsType<PartyMessageCreatedData>(frame.Data);
        Assert.Same(message, data.Message);
    }

    [Fact]
    public void FinalDelta_AlsoEmitsGenerationCompleted()
    {
        PartyStreamEvent Delta(bool done) => new()
        {
            Type = "messageEvent", ChatGroupId = Room, MessageId = 7,
            MessageEvent = new MessageStreamEvent { Event = MessageStreamEvent.ContentChunk, Data = "x", Done = done, ChatGroupId = Room },
        };

        Assert.Equal(new[] { "party.generation.delta" }, RealtimeTranslation.Party(Party, Delta(false)).Select(f => f.Type));
        Assert.Equal(new[] { "party.generation.delta", "party.generation.completed" },
            RealtimeTranslation.Party(Party, Delta(true)).Select(f => f.Type));
    }

    [Fact]
    public void World_IsNamespacedByKind_WithParsedPayload()
    {
        var frame = Assert.Single(RealtimeTranslation.Party(Party,
            new PartyStreamEvent { Type = "world", WorldKind = "body", WorldJson = """{"spot":"bar"}""" }));

        Assert.Equal("world.body", frame.Type);
        var data = Assert.IsType<PartyWorldEventData>(frame.Data);
        Assert.Equal("bar", data.Payload.GetProperty("spot").GetString());
    }

    [Fact]
    public void Unknown_FallsBackToPartyEvent_AndMalformedDeltaToo()
    {
        Assert.Equal("party.event", Assert.Single(RealtimeTranslation.Party(Party, new PartyStreamEvent { Type = "mystery" })).Type);
        Assert.Equal("party.event", Assert.Single(RealtimeTranslation.Party(Party, new PartyStreamEvent { Type = "messageEvent" })).Type);
    }
}
