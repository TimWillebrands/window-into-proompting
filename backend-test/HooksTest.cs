using PartyTown.Grains;
using PartyTown.World.Apps;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Every channel's options carry the persona's own material, so none wins on vividness alone.</summary>
public class HooksTest
{
    private const string Hat = "Floor sent me a photo of Mum's new church hat and I haven't stopped thinking about it, it's enormous and she's so proud";

    [Fact]
    public void AStory_ReadsTheSameOnEveryChannel()
    {
        var like = $"(like: {Hooks.Like(Hat)})";
        Assert.EndsWith(like, Hooks.Post(Hat));
        Assert.EndsWith(like, Hooks.Message("Denise", Hat));
        Assert.EndsWith(like, Hooks.Tell("Justin", Hat));
        Assert.StartsWith("Tell Justin, right here,", Hooks.Tell("Justin", Hat));
    }

    [Fact]
    public void WithNothingToTell_OptionsStayPlain()
    {
        Assert.Equal("Write a new post on Partyboard", Hooks.Post(null));
        Assert.Equal("Send Denise a private message", Hooks.Message("Denise", null));
        Assert.Equal("Write in your diary and take some time to reflect", Hooks.Diary(null));
        Assert.Equal("Do nothing for now", Hooks.Rest(null));
        Assert.Equal("Talk to Pieter, right here", Hooks.Ask("Pieter", null));
    }

    [Fact]
    public void SomeoneNextToYou_IsConcrete()
    {
        Assert.Equal("Pick up what Medevortius said, right here (\"How does the work fare?\")", Hooks.PickUp("Medevortius", "How does the work fare?"));
        Assert.Equal("Ask Pieter what they're up to, right here (on their phone, texting)", Hooks.Ask("Pieter", "on their phone, texting"));
    }

    [Fact]
    public void AToldStory_IsToldWhicheverChannelItWentOut()
    {
        var openers = new[] { Hat, "the heater still isn't fixed" };
        Assert.Equal(new[] { Hat }, Hooks.Told(Hooks.Message("Denise", Hat), openers));
        Assert.Equal(new[] { Hat }, Hooks.Told(Hooks.Tell("Justin", Hat), openers));
        Assert.Empty(Hooks.Told("Go to the Café", openers));
    }

    [Fact]
    public void DoingNothing_IsWhatTheDayHasThemDoing()
    {
        var day = new SelfState { Beats = SelfMath.ParseBeats("08:00 coffee\n20:00 a documentary on the sofa with Anne") };
        Assert.Equal("a documentary on the sofa with Anne", SelfRender.Beat(day, new DateTimeOffset(2026, 11, 7, 21, 30, 0, TimeSpan.Zero)));
        Assert.Null(SelfRender.Beat(day, new DateTimeOffset(2026, 11, 7, 6, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Do nothing in particular, just carry on with your day (a documentary on the sofa with Anne)", Hooks.Rest("a documentary on the sofa with Anne"));
    }
}

/// <summary>Someone in your vicinity is the natural thing to respond to.</summary>
public partial class PlacesTest
{
    private async Task<(Guid PartyId, Guid Denise, Guid Pieter, Place Cafe)> SideBySideAsync()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(denise, cafe.Id, cafe.Spots[0].Id);
        await places.MoveAsync(pieter, cafe.Id, cafe.Spots[0].Id);
        await BodyOf(partyId, denise);
        return (partyId, denise, pieter, await Fresh(partyId, cafe.Id));
    }

    [Fact]
    public async Task SomeoneNextToYouWhoSpoke_IsWhatYoudPickUp()
    {
        var (partyId, denise, pieter, cafe) = await SideBySideAsync();
        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(cafe.TalkRoomId!.Value).SendNewMessageAsync(pieter, "the oat milk is gone again", null, null);

        // Chat lines carry the wall clock; the world here runs on a manual one.
        var life = Life(partyId, denise, (denise, "Denise"), (pieter, "Pieter")) with { Now = DateTimeOffset.UtcNow };
        var seen = (await new PlacesApp(_fx.GrainFactory).PerceiveAsync(life, default))!;

        var talk = Assert.Single(seen.Affordances, a => a.Verb == "say");
        Assert.Equal("Pick up what Pieter said, right here (\"the oat milk is gone again\")", talk.Label);
        Assert.True(talk.Priority >= 2);
    }

    [Fact]
    public async Task SomeoneNextToYouBusyWithSomething_IsWhatYoudAskAbout()
    {
        var (partyId, denise, pieter, _) = await SideBySideAsync();
        await _fx.Places(partyId).SetActivityAsync(pieter, "reading the paper", null);

        var seen = (await new PlacesApp(_fx.GrainFactory).PerceiveAsync(Life(partyId, denise, (denise, "Denise"), (pieter, "Pieter")), default))!;

        Assert.Contains(seen.Affordances, a => a.Verb == "say" && a.Label == "Ask Pieter what they're up to, right here (reading the paper)");
    }
}
