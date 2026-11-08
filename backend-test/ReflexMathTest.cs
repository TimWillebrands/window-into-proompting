using PartyTown.World.Reflex;

namespace BackendTest;

/// <summary>Reflex's pure core (ADR 0031): attention filter, landscape, conflict, timing, prospective memory.</summary>
public class ReflexMathTest
{
    private static readonly Guid Someone = Guid.NewGuid();

    private static Percept P(PerceptKind kind, bool human = false, bool named = false, bool near = true)
        => new(kind, Someone, "Hana", human, Named: named, Near: near);

    private static Landscape L(double impulsivity = 0.5, double chattiness = 0.5, bool human = false) => new(impulsivity, chattiness, human);

    [Fact]
    public void Load_DemandsCombine_AndStayBelowTheCap()
    {
        var shift = ReflexMath.Load(1, onShift: true, away: false, occupied: false, talkingElsewhere: false);
        var shiftAndTalking = ReflexMath.Load(1, onShift: true, away: false, occupied: false, talkingElsewhere: true);

        Assert.Equal(0, ReflexMath.Load(1, false, false, false, false));
        Assert.True(shiftAndTalking > shift);
        Assert.Equal(ReflexMath.MaxLoad, ReflexMath.Load(0, true, true, true, true));
    }

    [Fact]
    public void Phone_CostsTheRoomAroundYou_NotTheNextMessage()
    {
        Assert.False(ReflexMath.Occupies("on their phone, texting", onDevice: true, PerceptKind.Direct));
        Assert.True(ReflexMath.Occupies("on their phone, texting", onDevice: true, PerceptKind.Arrival));
        Assert.True(ReflexMath.Occupies("working", onDevice: false, PerceptKind.Direct));
        Assert.False(ReflexMath.Occupies(null, onDevice: false, PerceptKind.Arrival));
    }

    [Fact]
    public void Surprise_SomeoneJustTalkedTo_IsNoNews_StrangerIsFull()
    {
        Assert.True(ReflexMath.Surprise(0.1) < 0.02);
        Assert.Equal(1, ReflexMath.Surprise(null));
        Assert.True(ReflexMath.Surprise(24) > 0.9);
    }

    [Fact]
    public void Arrival_OfColleagueJustSeen_IsBarelyNoticedNextToAStranger()
    {
        var colleague = ReflexMath.Strength(P(PerceptKind.Arrival), new Bond(30, 0, 0.1), 0);
        var stranger = ReflexMath.Strength(P(PerceptKind.Arrival), Bond.None, 0);

        Assert.True(stranger > colleague + 0.2);
    }

    [Fact]
    public void Busy_RoomChatter_IsFilteredOut_ButOwnNameGetsThrough()
    {
        var chatter = ReflexMath.Strength(P(PerceptKind.Line), Bond.None, 0);
        var named = ReflexMath.Strength(P(PerceptKind.Line, named: true), Bond.None, 0);

        Assert.False(ReflexMath.Noticed(chatter, load: 0.8, unit: 0.99));
        Assert.True(ReflexMath.Noticed(named, load: 0.8, unit: 0));
    }

    [Fact]
    public void Idle_EvenRoomChatter_IsNoticed()
        => Assert.True(ReflexMath.Noticed(ReflexMath.Strength(P(PerceptKind.Line), Bond.None, 0), load: 0, unit: 0.5));

    [Fact]
    public void Repetition_Habituates_AndRecoversWithTime()
    {
        var fresh = ReflexMath.Strength(P(PerceptKind.Speech), Bond.None, 0);
        var worn = ReflexMath.Strength(P(PerceptKind.Speech), Bond.None, 4);

        Assert.True(worn < fresh / 2);
        Assert.Equal(2, ReflexMath.Recover(4, ReflexMath.HabitHalfLifeMinutes), 6);
    }

    [Theory]
    [InlineData(PerceptKind.Direct)]
    [InlineData(PerceptKind.Knock)]
    public void BeingAddressed_DoesNotHabituateAway(PerceptKind kind)
    {
        var nagged = ReflexMath.Strength(P(kind), Bond.None, 50);

        Assert.True(ReflexMath.Noticed(nagged, load: 0.3, unit: 0));
    }

    [Fact]
    public void Human_IsNeverHabituatedAway()
    {
        var pestered = ReflexMath.Strength(P(PerceptKind.Direct, human: true), Bond.None, 50);

        Assert.True(ReflexMath.Noticed(pestered, load: 0.3, unit: 0));
    }

    [Fact]
    public void Landscape_AverageTraits_LeaveSystemOnesWeighingAlone()
    {
        var weighed = new Dictionary<string, double> { [ReflexOptions.Now] = 0.19, [ReflexOptions.Brief] = 0.43, [ReflexOptions.Soon] = 0.37, [ReflexOptions.Leave] = 0.01 };

        var shaped = ReflexMath.Shape(weighed, L());

        foreach (var (option, p) in weighed) Assert.Equal(p, shaped[option], 6);
    }

    [Fact]
    public void Landscape_ImpulsivePeopleAnswer_DeliberatePeopleFinishFirst()
    {
        var weighed = new Dictionary<string, double> { [ReflexOptions.Now] = 0.5, [ReflexOptions.Soon] = 0.5 };

        Assert.True(ReflexMath.Shape(weighed, L(impulsivity: 0.9))[ReflexOptions.Now] > 0.6);
        Assert.True(ReflexMath.Shape(weighed, L(impulsivity: 0.1))[ReflexOptions.Soon] > 0.6);
    }

    [Fact]
    public void Landscape_ChattyPeopleGreet_QuietPeopleNod()
    {
        var weighed = new Dictionary<string, double> { [ReflexOptions.Greet] = 0.5, [ReflexOptions.Nod] = 0.5 };

        Assert.True(ReflexMath.Shape(weighed, L(chattiness: 0.9))[ReflexOptions.Greet] > 0.6);
        Assert.True(ReflexMath.Shape(weighed, L(chattiness: 0.1))[ReflexOptions.Nod] > 0.6);
    }

    [Fact]
    public void Landscape_AHumanIsNeverLeftOnRead()
    {
        var weighed = new Dictionary<string, double> { [ReflexOptions.Soon] = 0.2, [ReflexOptions.Leave] = 0.8 };

        Assert.Equal(0, ReflexMath.Shape(weighed, L(human: true))[ReflexOptions.Leave]);
    }

    [Fact]
    public void Landscape_EvenIfSystemOneSaysLeaveOutright_AHumanStillGetsAnAnswer()
    {
        var weighed = new Dictionary<string, double> { [ReflexOptions.Soon] = 0, [ReflexOptions.Leave] = 1 };

        var shaped = ReflexMath.Shape(weighed, L(human: true));

        Assert.Equal(1, shaped[ReflexOptions.Soon], 6);
    }

    [Fact]
    public void Torn_OnlyWhenNoClearWinner()
    {
        Assert.True(ReflexMath.Torn(new Dictionary<string, double> { ["a"] = 0.46, ["b"] = 0.44, ["c"] = 0.1 }));
        Assert.False(ReflexMath.Torn(new Dictionary<string, double> { ["a"] = 0.7, ["b"] = 0.3 }));
    }

    [Theory]
    [InlineData(0.0001, 0.0)]
    [InlineData(0.5, 0.25)]
    [InlineData(0.9999, 0.5)]
    public void Timing_StaysInBounds(double u1, double u2)
    {
        Assert.InRange(ReflexMath.ReplyLag(PerceptKind.Speech, 0, u1, u2).TotalSeconds, 0.2, 2);
        Assert.InRange(ReflexMath.ReplyLag(PerceptKind.Direct, 1, u1, u2).TotalSeconds, 0.2, 12);
        Assert.InRange(ReflexMath.PutOff(1, owed: true, u1, u2).TotalMinutes, 0.75, ReflexMath.OwedWithin.TotalMinutes);
        Assert.InRange(ReflexMath.PutOff(1, owed: false, u1, u2).TotalMinutes, 0.75, 45);
    }

    private static Intention Held(long due, long? owed = null, long forget = long.MaxValue)
        => new() { DueAt = due, OwedBy = owed, ForgetAt = forget, FromName = "Hana", Gist = "lunch?" };

    [Fact]
    public void Intention_Waits_UntilDue()
        => Assert.Equal(IntentionStep.Wait, Intentions.Step(Held(100), 50, 0));

    [Fact]
    public void Intention_WhenBusy_WaitsForABreakpoint()
    {
        Assert.Equal(IntentionStep.Postpone, Intentions.Step(Held(100), 100, 0.9));
        Assert.Equal(IntentionStep.Surface, Intentions.Step(Held(100), 100, 0.2));
    }

    [Fact]
    public void Intention_OwedToAHuman_HappensByTheDeadlineHoweverBusy()
    {
        Assert.Equal(IntentionStep.Surface, Intentions.Step(Held(100, owed: 200), 200, 0.95));
        Assert.Equal(IntentionStep.Postpone, Intentions.Step(Held(100, owed: 200), 150, 0.95));
    }

    [Fact]
    public void Intention_NobodyOwed_SlipsTheirMind()
        => Assert.Equal(IntentionStep.Forget, Intentions.Step(Held(100, forget: 300), 300, 0.1));

    [Fact]
    public void Intention_FollowUp_ComesToMindSooner_KeepingWhatTheyMeantToAnswer()
    {
        var i = Held(1000);
        var asked = i.Gist;

        Intentions.Renew(i, "Fine. Take the night.", 200);

        Assert.Equal(600, i.DueAt);
        Assert.Equal(asked, i.Gist);
    }

    [Fact]
    public void Verdict_PastTheGatesDeadline_IsLate()
    {
        var p = new Percept(PerceptKind.Direct, Guid.NewGuid(), "Ana", true, "hi", Deadline: 1000);

        Assert.False(ReflexMath.Late(p, 1000));
        Assert.True(ReflexMath.Late(p, 1001));
        Assert.False(ReflexMath.Late(p with { Deadline = null }, 99_999));
    }

    [Fact]
    public void Intention_NextLook_IsTheEarliest_CountingDeadlines()
    {
        Assert.Null(Intentions.NextLook(Array.Empty<Intention>()));
        Assert.Equal(150, Intentions.NextLook(new[] { Held(300), Held(400, owed: 150) }));
    }

    [Fact]
    public void Intention_Render_NamesWhoTheyMeantToGetBackTo()
    {
        Assert.Equal(string.Empty, Intentions.Render(Array.Empty<Intention>()));
        Assert.Contains("get back to Hana", Intentions.Render(new[] { Held(1) }));
    }
}
