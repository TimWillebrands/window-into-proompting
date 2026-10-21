using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>Earshot keys off spots: talk = your spot, shout = the Place, whisper = one addressee.</summary>
public class EarshotTest
{
    private static readonly Guid Cafe = Guid.NewGuid();
    private static Body At(string name, string spot) => new() { ParticipantId = Guid.NewGuid(), Name = name, PlaceId = Cafe, SpotId = spot };

    private readonly Body _denise = At("Denise", "counter");
    private readonly Body _hana = At("Hana", "counter");
    private readonly Body _pieter = At("Pieter", "booth");

    private List<Body> All => new() { _denise, _hana, _pieter };

    [Fact]
    public void Talk_ReachesYourSpotOnly()
        => Assert.Equal(new[] { _denise.ParticipantId, _hana.ParticipantId }.Order(),
            Earshot.Hearers(All, _denise, Volumes.Talk, null).Order());

    [Fact]
    public void Shout_ReachesTheWholePlace()
        => Assert.Equal(3, Earshot.Hearers(All, _denise, Volumes.Shout, null).Count);

    [Fact]
    public void Whisper_ReachesOnlyTheAddresseeNextToYou()
    {
        Assert.Equal(new[] { _hana.ParticipantId, _denise.ParticipantId }.Order(),
            Earshot.Hearers(All, _denise, Volumes.Whisper, _hana.ParticipantId).Order());
        // Whispering to someone across the room just reaches your spot.
        Assert.DoesNotContain(_pieter.ParticipantId, Earshot.Hearers(All, _denise, Volumes.Whisper, _pieter.ParticipantId));
    }

    [Fact]
    public void Witnesses_AreTheHearers_ForPlaceTalkOnly()
    {
        var present = All.Select(b => new ParticipantView(b.ParticipantId, b.Name, DriverKind.LLM)).ToList();
        var roomSpan = new List<ChatMessage> { new() { MessageId = 1, SenderId = _denise.ParticipantId } };
        var placeSpan = new List<ChatMessage>
        {
            new() { MessageId = 1, SenderId = _denise.ParticipantId, HeardBy = new List<Guid> { _denise.ParticipantId, _hana.ParticipantId } },
        };

        Assert.Equal(3, ClockPolicy.Witnesses(roomSpan, present).Count);
        Assert.Equal(new[] { "Denise", "Hana" }, ClockPolicy.Witnesses(placeSpan, present).Select(p => p.Name).Order());
    }

    [Fact]
    public void Overheard_ReadsAsToneAndMaybeYourName_NeverTheWords()
    {
        var place = new Place { Id = Cafe, Name = "the Café", Spots = new List<Spot> { new() { Id = "counter", Name = "the counter" } } };
        var heard = new Overheard
        {
            PlaceId = Cafe, SpeakerName = "Denise", SpotId = "counter", Tone = "arguing", At = 1,
            Listeners = new List<Guid> { _pieter.ParticipantId }, NameHeardBy = new List<Guid> { _pieter.ParticipantId },
            WithNames = new List<string> { "Hana" },
        };

        var (line, _, name) = Assert.Single(Overhearing.Lines(new[] { heard }, _pieter.ParticipantId, place));

        Assert.True(name);
        Assert.Equal("Over at the counter, Denise and Hana are arguing, by the sound of it; you can't make out the words. You think you heard your name.", line);
    }

    [Fact]
    public void PlaceGathering_WalksAwakeLlmAttendeesOnly()
    {
        var (llm, human, sleeper) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var cast = new List<CastMember>
        {
            new() { Id = llm, Name = "Denise", DefaultDriver = DriverKind.LLM },
            new() { Id = human, Name = "Tim", DefaultDriver = DriverKind.User },
            new() { Id = sleeper, Name = "Pieter", DefaultDriver = DriverKind.LLM },
        };

        var going = ClockPolicy.WalkToGathering(new[] { llm, human, sleeper }, cast, new HashSet<Guid> { sleeper });

        Assert.Equal(new[] { llm }, going);
    }
}
