using PartyTown.Model;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Ties (ADR 0021): closeness from shared talk calibrates intimacy.</summary>
public class TiesTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Lena = Guid.NewGuid();
    private static readonly Guid Tim = Guid.NewGuid();

    private static ChatMessage Line(Guid who, long ts) => new() { SenderId = who, Content = "hi", SendAt = ts };

    [Fact]
    public void Seed_CountsMutualTalk_DirectWeighsMoreThanRooms()
    {
        var ties = new Dictionary<Guid, Tie>();
        Ties.Seed(ties, Me, direct: true, new[] { Line(Me, 1), Line(Lena, 2), Line(Me, 3), Line(Lena, 9) });
        Ties.Seed(ties, Me, direct: false, new[] { Line(Me, 4), Line(Tim, 5), Line(Lena, 6) });

        Assert.Equal(2 + Ties.RoomWeight, ties[Lena].Exchanges, 3);
        Assert.Equal(9, ties[Lena].LastAt);
        Assert.Equal(Ties.RoomWeight, ties[Tim].Exchanges, 3);
    }

    [Fact]
    public void Seed_IgnoresRoomsIHaveNotSpokenIn()
    {
        var ties = new Dictionary<Guid, Tie>();
        Ties.Seed(ties, Me, direct: false, new[] { Line(Lena, 1), Line(Tim, 2) });
        Assert.Empty(ties);
    }

    [Theory]
    [InlineData(0.5, "barely")]
    [InlineData(5, "a few times")]
    [InlineData(20, "fairly well")]
    [InlineData(100, "know each other well")]
    public void Closeness_Buckets(double exchanges, string expected)
        => Assert.Contains(expected, Ties.Closeness(exchanges));

    [Fact]
    public void Render_StrangersAndFriends()
    {
        var now = DateTimeOffset.UtcNow;
        var text = Ties.Render(new (string, Tie?)[]
        {
            ("Lena", new Tie { Exchanges = 50, LastAt = now.AddHours(-3).ToUnixTimeMilliseconds() }),
            ("Tim", null),
        }, now);
        Assert.Contains("- Lena: you know each other well; last talked 3 h ago", text);
        Assert.Contains("- Tim: you've never really talked", text);
        Assert.Equal(string.Empty, Ties.Render(Array.Empty<(string, Tie?)>(), now));
    }
}
