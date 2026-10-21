using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Stale-word habituation on recall (ADR 0021).</summary>
public class StaleRecallTest
{
    private static readonly string[] Stale = { "tin", "lid" };

    [Fact]
    public void Demote_KeepsOneStaleWhenEnoughFresh()
    {
        var items = new[] { "the chipped tin on his shelf", "Lena's lease is due Friday", "the lid never fit", "Eiko's roof estimate", "soba at the station" };
        var result = StaleRecall.Demote(items, s => s, Stale);
        Assert.Equal(new[] { "Lena's lease is due Friday", "Eiko's roof estimate", "soba at the station", "the chipped tin on his shelf" }, result);
    }

    [Fact]
    public void Demote_KeepsAllWhenLittleElseToRemember()
    {
        var items = new[] { "the tin", "the lid", "Eiko's roof" };
        Assert.Equal(new[] { "Eiko's roof", "the tin", "the lid" }, StaleRecall.Demote(items, s => s, Stale));
    }

    [Fact]
    public void Demote_WholeWordsOnly()
        => Assert.Equal(new[] { "tiny lido" }, StaleRecall.Demote(new[] { "tiny lido" }, s => s, Stale));
}
