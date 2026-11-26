using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World.Reflex;

namespace BackendTest;

public class ReflexRoomTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ada = Guid.NewGuid();
    private static readonly Guid Bo = Guid.NewGuid();
    private static readonly Guid Sam = Guid.NewGuid();

    private static RoomCast Cast(string kind = ChatGroupKinds.Group) => new()
    {
        Kind = kind,
        Members = new List<CastMember>
        {
            new() { Id = Me, Name = "Mara", DefaultDriver = DriverKind.LLM },
            new() { Id = Ada, Name = "Ada", DefaultDriver = DriverKind.LLM },
            new() { Id = Bo, Name = "Bo", DefaultDriver = DriverKind.LLM },
            new() { Id = Sam, Name = "Sam", DefaultDriver = DriverKind.User },
        },
    };

    private static List<ChatMessage> Talk(params (Guid From, string Text)[] lines)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return lines.Select((l, i) => new ChatMessage
        {
            MessageId = i + 1, SenderId = l.From, Content = l.Text,
            SenderType = l.From == Sam ? "user" : "assistant",
            SendAt = now - (lines.Length - i) * 20_000,
        }).ToList();
    }

    private static Percept Of(List<ChatMessage> history, string kind = ChatGroupKinds.Group, PartyTown.World.Self.ThermostatRead? thermostat = null, bool named = false)
        => ResponsePipeline.PerceptOf(history[^1], Cast(kind), Me, history, new ResponseUrge(0.5, named ? 1 : 0, 0, 0, 0, 0), Guid.NewGuid(), thermostat);

    [Fact]
    public void PerceptOf_CountsTheLinesTradedSinceSomeoneElseSpoke_AndWhoTradedThem()
    {
        var history = Talk((Sam, "morning"), (Me, "hey"), (Ada, "seen the show?"), (Bo, "yes!"), (Ada, "the receptionist"), (Bo, "haha right?"));

        var p = Of(history);

        Assert.Equal(5, p.Traded);
        Assert.Equal(new[] { "Mara", "Ada", "Bo" }, p.Traders);
        Assert.Equal(1, p.Own);
        Assert.Equal(6, p.Recent);
        Assert.Equal(PerceptKind.Line, p.Kind);
    }

    [Fact]
    public void PerceptOf_AHumansLine_TradesNothing()
    {
        var p = Of(Talk((Ada, "hi"), (Sam, "goedemorgen allemaal")));

        Assert.Equal(0, p.Traded);
        Assert.True(p.FromHuman);
    }

    [Fact]
    public void PerceptOf_CarriesTheThermostatsReadAndTheName()
    {
        var p = Of(Talk((Ada, "Mara, saturday then?")), thermostat: new PartyTown.World.Self.ThermostatRead(0, 0, 0.9, 0, true), named: true);

        Assert.True(p.Named);
        Assert.True(p.WindingDown);
        Assert.Equal(PartyTown.World.Self.Thermostat.Cue(0, 0, 0.9), p.Thermostat);
    }

    [Fact]
    public void PerceptOf_AQuietThermostat_SaysNothing()
    {
        var p = Of(Talk((Ada, "ok")), thermostat: PartyTown.World.Self.ThermostatRead.None);

        Assert.Null(p.Thermostat);
        Assert.False(p.WindingDown);
    }

    [Fact]
    public void PerceptOf_InADm_NamingIsntAFact()
        => Assert.False(Of(Talk((Ada, "Mara?")), ChatGroupKinds.Direct, named: true).Named);

    private static readonly Sense Mara = new("Mara", "Works the counter.", "working", "good", 0.2, 0.9, new Bond(30, 0.2, 1), Array.Empty<string>());

    [Fact]
    public void State_SaysWhatTheRoomHasBeenLike_AsFacts()
    {
        var p = new Percept(PerceptKind.Line, Bo, "Bo", false, "haha right?", Guid.NewGuid(), Named: true,
            Traded: 5, Traders: new List<string> { "Ada", "Bo" }, Own: 0, Recent: 8, Thermostat: "This conversation is winding down.");

        var state = ReflexWeighing.State(p, Mara);

        Assert.Contains("Ada and Bo have traded the last 5 lines here, with nobody else in between.", state);
        Assert.Contains("None of the last 8 lines here were Mara's.", state);
        Assert.Contains("How the conversation is going: This conversation is winding down.", state);
        Assert.Contains("Bo mentions Mara by name.", state);
        Assert.True(state.IndexOf("have traded", StringComparison.Ordinal) < state.IndexOf("What just happened", StringComparison.Ordinal));
    }

    [Fact]
    public void State_OneVoiceAndTheirOwnShare()
    {
        var p = new Percept(PerceptKind.Line, Bo, "Bo", false, "and another thing", Guid.NewGuid(),
            Traded: 3, Traders: new List<string> { "Bo" }, Own: 3, Recent: 8);

        var state = ReflexWeighing.State(p, Mara);

        Assert.Contains("Bo has written the last 3 lines here, with nobody else in between.", state);
        Assert.Contains("3 of the last 8 lines here were Mara's.", state);
        Assert.DoesNotContain("mentions Mara", state);
        Assert.DoesNotContain("How the conversation is going", state);
    }

    [Fact]
    public void State_AQuietRoomSaysNothingAboutIt()
    {
        var state = ReflexWeighing.State(new Percept(PerceptKind.Line, Sam, "Sam", true, "hiya", Guid.NewGuid(), Recent: 1), Mara);

        Assert.DoesNotContain("traded", state);
        Assert.DoesNotContain("lines here were", state);
    }

    [Fact]
    public void Options_StayAsTheyWere_ForANamedHumansLine()
    {
        var options = ReflexWeighing.Options(new Percept(PerceptKind.Line, Sam, "Sam", true, "Mara?", Guid.NewGuid(), Named: true, Traded: 4), "Mara");

        Assert.Equal(new[] { ReflexOptions.Now, ReflexOptions.Brief, ReflexOptions.Soon, ReflexOptions.Leave }, options.Keys.ToArray());
    }
}
