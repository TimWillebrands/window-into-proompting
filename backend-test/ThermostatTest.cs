using Moq;
using Orleans.TestKit;
using PartyTown.World.Self;

namespace BackendTest;

public class ThermostatTest
{
    [Fact]
    public void Streak_GrowsOnHeavy_ResetsOnLight()
    {
        var s = 0;
        foreach (var p in new[] { 0.9, 0.7, 0.8 }) s = Thermostat.NextStreak(s, p);
        Assert.Equal(3, s);
        Assert.Equal(0, Thermostat.NextStreak(s, 0.3));
    }

    [Fact]
    public void Cue_OnlyWhenSustained_StrongerWhenCircling()
    {
        Assert.Null(Thermostat.Cue(Thermostat.SustainedStreak - 1, 0.5));
        Assert.Contains("circles", Thermostat.Cue(Thermostat.SustainedStreak, 0.8));
        Assert.Contains("intense", Thermostat.Cue(Thermostat.SustainedStreak, 0.1));
    }
}

public class WindingDownTest
{
    private static readonly ThermostatRead Fizzling = new(0, 0, 0.9, 0, true);

    [Fact]
    public void Fresh_AReadOfTalkThatHasGoneQuiet_IsNoRead()
    {
        var now = DateTimeOffset.UtcNow;
        var line = now.AddMinutes(-5).ToUnixTimeMilliseconds();

        Assert.Equal(Fizzling, Thermostat.Fresh(Fizzling, line, now, TimeSpan.FromMinutes(20)));
        Assert.Equal(ThermostatRead.None, Thermostat.Fresh(Fizzling, line, now.AddMinutes(30), TimeSpan.FromMinutes(20)));
        Assert.Equal(ThermostatRead.None, Thermostat.Fresh(Fizzling, 0, now, TimeSpan.FromMinutes(20)));
    }

    [Fact]
    public void Fizzling_WinsOverHeavy_AndNeedsNoStreak()
    {
        Assert.Contains("winding down", Thermostat.Cue(0, 0, fizzling: 0.8));
        Assert.Contains("winding down", Thermostat.Cue(Thermostat.SustainedStreak, 0.9, fizzling: 0.8));
        Assert.Null(Thermostat.Cue(0, 0, fizzling: 0.3));
    }
}

public class CirclingTest
{
    [Fact]
    public void Circling_AloneCuesMovingOn()
    {
        Assert.Contains("same thing", Thermostat.Cue(0, 0.8));
        Assert.Null(Thermostat.Cue(0, 0.6));
    }
}

public class ReconfirmingTest
{
    [Fact]
    public void Reconfirming_SettledPlansCuesMovingOn()
    {
        Assert.Contains("already settled", Thermostat.Cue(0, 0, reconfirming: 0.7));
        Assert.Contains("winding down", Thermostat.Cue(0, 0, fizzling: 0.8, reconfirming: 0.7));
        Assert.Null(Thermostat.Cue(0, 0, reconfirming: 0.4));
    }

    [Fact]
    public void Reconfirming_WindsTheConversationDown()
    {
        Assert.True(Thermostat.WindingDown(0, 0, 0.7));
        Assert.False(Thermostat.WindingDown(0.5, 0.3, 0.4));
    }
}

public class ThermostatGrainTest : Orleans.TestKit.TestKitBase
{
    private sealed class Fizzles : PartyTown.Services.SystemOne.ISystemOneJudge
    {
        public Task<PartyTown.Services.SystemOne.SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, PartyTown.Services.SystemOne.SystemOneQuestion> questions, CancellationToken ct)
        {
            var answers = new PartyTown.Services.SystemOne.SystemOneAnswers();
            answers.Nouls["fizzling"] = 0.9;
            return Task.FromResult(answers);
        }
    }

    [Fact]
    public async Task AWindingDownRead_DoesntOutliveTheTalk()
    {
        var room = Guid.NewGuid();
        var party = Guid.NewGuid();
        var time = new PlacesTest.ManualTime();
        var who = Guid.NewGuid();
        var lines = Enumerable.Range(1, 5).Select(i => new PartyTown.Model.ChatMessage
        {
            MessageId = i, SenderId = who, SenderType = "assistant", Content = "ok", SendAt = time.GetUtcNow().ToUnixTimeMilliseconds(),
        }).ToList();
        var chat = Silo.AddProbe<PartyTown.Grains.IChatGroupGrain>(room);
        chat.Setup(c => c.GetPartyIdAsync()).ReturnsAsync(party);
        chat.Setup(c => c.GetRecentMessagesAsync(Moq.It.IsAny<int>())).ReturnsAsync(() => lines.ToList());
        Silo.AddProbe<PartyTown.Grains.IPartyGrain>(party).Setup(p => p.GetCastAsync())
            .ReturnsAsync((IReadOnlyList<PartyTown.Model.CastMember>)new List<PartyTown.Model.CastMember> { new() { Id = who, Name = "Bo" } });
        Silo.AddService<PartyTown.Services.SystemOne.ISystemOneJudge>(new Fizzles());
        Silo.AddService<TimeProvider>(time);
        var thermostat = await Silo.CreateGrainAsync<ConversationThermostatGrain>(room);

        Assert.True(await thermostat.IsWindingDownAsync());
        Assert.True((await thermostat.PeekAsync()).Stale);

        time.Advance(ConversationThermostatGrain.ReadLasts + TimeSpan.FromMinutes(1));
        Assert.Equal(ThermostatRead.None, await thermostat.PeekAsync());

        lines = lines.Take(2).ToList();
        Assert.False(await thermostat.IsWindingDownAsync());
        Assert.Equal(ThermostatRead.None, await thermostat.GetReadAsync());
    }
}
