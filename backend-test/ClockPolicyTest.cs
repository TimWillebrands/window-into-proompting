using PartyTown.Model;
using PartyTown.World;

namespace BackendTest;

/// <summary>World clock rules as pure functions — no cluster, no timers.</summary>
public class ClockPolicyTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(10_000_000_000);
    private static long Ago(TimeSpan t) => (Now - t).ToUnixTimeMilliseconds();

    [Fact]
    public void TickDue_RespectsInterval()
    {
        Assert.True(ClockPolicy.TickDue(null, 30, Now));
        Assert.False(ClockPolicy.TickDue(Ago(TimeSpan.FromSeconds(29)), 30, Now));
        Assert.True(ClockPolicy.TickDue(Ago(TimeSpan.FromSeconds(30)), 30, Now));
    }

    [Fact]
    public void Budget_PrunesActionsOlderThanAnHour()
    {
        var actions = new List<long> { Ago(TimeSpan.FromMinutes(61)), Ago(TimeSpan.FromMinutes(5)) };

        Assert.True(ClockPolicy.WithinBudget(actions, 2, Now));
        Assert.Single(actions);
        Assert.False(ClockPolicy.WithinBudget(actions, 1, Now));
    }

    [Fact]
    public void Ambient_FirstSightStartsInterval_ThenFiresWhenDue()
    {
        Assert.Equal((false, Now.ToUnixTimeMilliseconds()), ClockPolicy.Ambient(null, 30, Now));
        var recent = Ago(TimeSpan.FromMinutes(10));
        Assert.Equal((false, recent), ClockPolicy.Ambient(recent, 30, Now));
        Assert.Equal((true, Now.ToUnixTimeMilliseconds()), ClockPolicy.Ambient(Ago(TimeSpan.FromMinutes(31)), 30, Now));
        Assert.False(ClockPolicy.Ambient(Ago(TimeSpan.FromDays(1)), 0, Now).Due);
    }

    [Fact]
    public void LifeWeights_FavourChatty_Rested_AndFree()
    {
        var quiet = new LifeCandidate(Guid.NewGuid(), 0.1, Ago(TimeSpan.FromMinutes(5)), 1);
        var chatty = quiet with { Id = Guid.NewGuid(), Chattiness = 0.9 };
        var justActed = chatty with { Id = Guid.NewGuid(), LastActedAt = Ago(TimeSpan.FromSeconds(1)) };
        var atWork = chatty with { Id = Guid.NewGuid(), Availability = 0 };

        var w = ClockPolicy.LifeWeights(new[] { quiet, chatty, justActed, atWork }, 30, 4, Now);

        Assert.True(w[1] > w[0]);
        Assert.True(w[1] > w[2]);
        Assert.True(w[1] > w[3]);
        Assert.True(w[3] > 0, "unavailable still gets a sliver, never zero");
    }

    [Fact]
    public void Pick_DrawsByWeight()
    {
        var a = new LifeCandidate(Guid.NewGuid(), 0.5, null, 1);
        var b = a with { Id = Guid.NewGuid() };
        var weights = new[] { 1.0, 3.0 };

        Assert.Equal(a.Id, ClockPolicy.Pick(new[] { a, b }, weights, 0.2));
        Assert.Equal(b.Id, ClockPolicy.Pick(new[] { a, b }, weights, 0.3));
        Assert.Null(ClockPolicy.Pick(Array.Empty<LifeCandidate>(), Array.Empty<double>(), 0.5));
    }

    private static ChatMessage Line(int id, TimeSpan ago, int? replyTo = null)
        => new() { MessageId = id, Content = $"m{id}", SendAt = Ago(ago), TriggeredByMessageId = replyTo };

    [Fact]
    public void Lulled_SeedsOnFirstSight_WaitsWhileTalkIsFreshOrThin()
    {
        var talk = new[] { Line(1, TimeSpan.FromMinutes(5)), Line(2, TimeSpan.FromMinutes(4)), Line(3, TimeSpan.FromMinutes(3)), Line(4, TimeSpan.FromSeconds(10)) };

        Assert.Equal(new LullDecision.Seed(4), ClockPolicy.Lulled(talk, null, Now));
        Assert.IsType<LullDecision.Wait>(ClockPolicy.Lulled(talk, 0, Now));
        Assert.IsType<LullDecision.Wait>(ClockPolicy.Lulled(talk[..3], 1, Now));
        Assert.IsType<LullDecision.Wait>(ClockPolicy.Lulled(Array.Empty<ChatMessage>(), 0, Now));
    }

    [Fact]
    public void Lulled_CapturesMostAnsweredLine_WithItsLeadUp()
    {
        var talk = new[]
        {
            Line(1, TimeSpan.FromMinutes(9)),
            Line(2, TimeSpan.FromMinutes(8)),
            Line(3, TimeSpan.FromMinutes(7), replyTo: 2),
            Line(4, TimeSpan.FromMinutes(6), replyTo: 2),
            Line(5, TimeSpan.FromMinutes(5)),
        };

        var capture = Assert.IsType<LullDecision.Capture>(ClockPolicy.Lulled(talk, 1, Now));

        Assert.Equal(5, capture.Cursor);
        Assert.Equal(2, capture.Anchor.MessageId);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, capture.Context.Select(m => m.MessageId));
    }
}
