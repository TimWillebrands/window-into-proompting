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

    [Fact]
    public void People_FindsWhoAWantIsAbout_ByTargetOrName()
    {
        var (anne, justin, pieter, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var cast = new[] { (anne, "Anne de Castelnaudary"), (justin, "Justin"), (pieter, "Pieter Voss") };
        var wants = new[]
        {
            D("say to Anne and Justin's faces what you never said", daysAgo: 1),
            new Desire { Text = "a quiet word", Kind = DesireKind.Being, TargetId = other, Since = Now.ToUnixTimeMilliseconds() },
            D("Pieter Voss's trust", DesireKind.Having, daysAgo: 3),
        };
        var people = DesireRules.People(wants, cast);
        Assert.Equal(new HashSet<Guid> { anne, justin, other }, people.Keys.ToHashSet());
        Assert.Equal("say to Anne and Justin's faces what you never said", people[justin].Text);
    }

    [Fact]
    public void People_IgnoresNamesInsideOtherWords()
    {
        var ann = Guid.NewGuid();
        Assert.Empty(DesireRules.People(new[] { D("a new annex for the shed") }, new[] { (ann, "Ann") }));
    }

    [Fact]
    public void Toward_NotesTheWantOnAnOptionAboutThatPerson()
    {
        Assert.Equal("Send Pieter Voss a private message (toward what you want: Pieter Voss's trust)",
            DesireRules.Toward("Send Pieter Voss a private message", D("Pieter Voss's trust", DesireKind.Having)));
        Assert.Equal("Rest", DesireRules.Toward("Rest", null));
    }
}
