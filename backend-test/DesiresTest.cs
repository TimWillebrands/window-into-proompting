using PartyTown.World.Desires;

namespace BackendTest;

/// <summary>Wanting (ADR 0026): what's on the mind, how it reads, when it fades, what the shops make of it.</summary>
public class DesiresTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static Desire D(string text, DesireKind kind = DesireKind.Being, int daysAgo = 0, DesireStatus status = DesireStatus.Active)
        => new() { Text = text, Kind = kind, Since = Now.AddDays(-daysAgo).ToUnixTimeMilliseconds(), Status = status };

    [Fact]
    public void Parse_MatchesCatalogItemsForHavingOnly()
    {
        var coat = DesireRules.Parse("a navy hoodie that actually fits", DesireKind.Having, DesireSource.Sheet, Now)!;
        Assert.Equal("top:hoodie:navy", coat.ItemKey);
        Assert.Null(DesireRules.Parse("get better at drawing hands", DesireKind.Being, DesireSource.Sheet, Now)!.ItemKey);
        Assert.Null(DesireRules.Parse("none", DesireKind.Having, DesireSource.Sheet, Now));
        Assert.Null(DesireRules.Parse("  ", DesireKind.Being, DesireSource.Sheet, Now));
    }

    [Fact]
    public void Render_IsPlainCappedAndSkipsSettledOnes()
    {
        var text = DesireRules.Render(new[] { D("get your sourdough right", daysAgo: 3), D("patch things up with Bo", daysAgo: 1), D("old", daysAgo: 0, status: DesireStatus.Fulfilled), D("oldest", daysAgo: 9) });
        Assert.Equal("# What you want lately\n- Patch things up with Bo.\n- Get your sourdough right.\n", text);
        Assert.Equal(string.Empty, DesireRules.Render(Array.Empty<Desire>()));
    }

    [Fact]
    public void Fades_OnlyAcquiredOnesLeftUntouched()
    {
        var stale = D("learn to skate", daysAgo: 15);
        Assert.True(DesireRules.Fades(stale, intrinsic: false, Now));
        Assert.False(DesireRules.Fades(stale, intrinsic: true, Now));
        stale.TouchedAt = Now.AddDays(-2).ToUnixTimeMilliseconds();
        Assert.False(DesireRules.Fades(stale, intrinsic: false, Now));
    }

    [Fact]
    public void Shopping_IsTheWantedItemYouDontOwnYet()
    {
        var hoodie = DesireRules.Parse("a navy hoodie", DesireKind.Having, DesireSource.Sheet, Now)!;
        Assert.Same(hoodie, DesireRules.Shopping(new[] { D("become braver"), hoodie }, Array.Empty<string>()));
        Assert.Null(DesireRules.Shopping(new[] { hoodie }, new[] { "top:hoodie:navy" }));
    }

    [Theory]
    [InlineData(0.8, 0.9, DesireStatus.Fulfilled)]
    [InlineData(0.3, 0.65, DesireStatus.Active)]
    [InlineData(0.3, 0.4, null)]
    public void Reviewed_GotItOrCloserOrNothing(double done, double closer, DesireStatus? expected)
        => Assert.Equal(expected, DesireReview.Reviewed(done, closer));
}
