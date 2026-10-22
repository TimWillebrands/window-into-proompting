using PartyTown.Model;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Chat attention: the live exchange is held, scrolled-up history isn't.</summary>
public class ChatAttentionTest
{
    private static readonly long T0 = 10_000_000_000;
    private static long Min(double m) => T0 + (long)TimeSpan.FromMinutes(m).TotalMilliseconds;

    private static List<ChatMessage> At(params double[] minutes)
        => minutes.Select((m, i) => new ChatMessage { MessageId = i + 1, Content = $"m{i + 1}", SendAt = Min(m) }).ToList();

    [Fact]
    public void FirstChatInAWhile_HoldsOnlyWhatIsOnScreen()
    {
        // Yesterday's long exchange, then one fresh line today.
        var history = At(0, 1, 2, 3, 4, 5, 6, 7, 1440);

        var (held, hidden) = ChatAttention.Window(history);

        Assert.Equal(new[] { 5, 6, 7, 8, 9 }, held.Select(m => m.MessageId));
        Assert.Equal(4, hidden);
    }

    [Fact]
    public void BackAndForth_HoldsTheWholeExchange()
    {
        var history = At(0, 1, 120, 125, 130, 138, 150, 165, 170, 171);

        var (held, hidden) = ChatAttention.Window(history);

        // Exchange starts at minute 120 (index 2); everything above it fits on screen.
        Assert.Equal(10, held.Count);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void LongExchange_IsCapped()
    {
        var history = At(Enumerable.Range(0, 60).Select(i => (double)i).ToArray());

        var (held, hidden) = ChatAttention.Window(history);

        Assert.Equal(ChatAttention.MaxHeld, held.Count);
        Assert.Equal(20, hidden);
        Assert.Equal(60, held[^1].MessageId);
    }

    [Fact]
    public void UnsentStubs_StayInTheExchange()
    {
        var history = At(0, 100, 101);
        history.Add(new ChatMessage { MessageId = 4, SendAt = null });

        Assert.Equal(1, ChatAttention.SessionStart(history));
    }

    [Fact]
    public void Empty_HoldsNothing()
    {
        var (held, hidden) = ChatAttention.Window(new List<ChatMessage>());

        Assert.Empty(held);
        Assert.Equal(0, hidden);
    }

    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Them = Guid.NewGuid();
    private static DateTimeOffset NowAt(double m) => DateTimeOffset.FromUnixTimeMilliseconds(Min(m));

    private static List<ChatMessage> Talk(params (double Min, Guid From)[] lines)
        => lines.Select((l, i) => new ChatMessage { MessageId = i + 1, Content = $"m{i + 1}", SendAt = Min(l.Min), SenderId = l.From }).ToList();

    [Fact]
    public void Glance_MidExchange_HoldsTheLiveLines()
    {
        var chat = Talk((0, Them), (1, Me), (100, Them), (101, Me), (102, Them));

        var glance = ChatAttention.Glance(chat, Me, NowAt(105));

        Assert.True(glance.InExchange);
        Assert.Equal(1, glance.Unread);
        Assert.Equal(5, glance.Held.Count);
    }

    [Fact]
    public void Glance_NewMessageAfterAWhile_IsANotificationOnly()
    {
        var chat = Talk((0, Them), (1, Me), (300, Them), (301, Them));

        var glance = ChatAttention.Glance(chat, Me, NowAt(305));

        Assert.False(glance.InExchange);
        Assert.Equal(2, glance.Unread);
        Assert.Empty(glance.Held);
        Assert.True(glance.MoreAbove);
        Assert.Equal(4, glance.Latest!.MessageId);
    }

    [Fact]
    public void Glance_ExchangeGoneQuiet_IsNoLongerHeld()
    {
        var chat = Talk((0, Them), (1, Me));

        Assert.False(ChatAttention.Glance(chat, Me, NowAt(60)).InExchange);
    }

    [Fact]
    public void ReadBack_ReachesAboveTheScreen_AndStaysInMindForAWhile()
    {
        var history = At(Enumerable.Range(0, 20).Select(i => (double)i).Append(1440).ToArray());

        var target = ChatAttention.ScrollTarget(history, null);
        var (held, hidden) = ChatAttention.Window(history, target);

        Assert.Equal(1, target);
        Assert.Equal(0, hidden);
        Assert.Equal(21, held.Count);
        Assert.Null(ChatAttention.ScrollTarget(history, target));

        var look = new AttentionLook { At = Min(1440), FromMessageId = target };
        Assert.Equal(target, ChatAttention.ReadFrom(look, NowAt(1450)));
        Assert.Null(ChatAttention.ReadFrom(look, NowAt(1460)));
    }
}
