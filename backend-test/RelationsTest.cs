using PartyTown.World.Places;
using PartyTown.World.Reflex;
using PartyTown.World.Relations;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Relations (ADR 0035): who may ask what, who knows what, and how it reads.</summary>
public class RelationsTest
{
    private static readonly Guid Justin = Guid.NewGuid();
    private static readonly Guid Anne = Guid.NewGuid();
    private static readonly Guid Denise = Guid.NewGuid();
    private static readonly Guid Mara = Guid.NewGuid();

    private static readonly TieRead Close = new(30, 0.5);
    private static readonly TieRead Warm = new(30, 0.3);
    private static readonly TieRead Cool = new(30, 0);
    private static readonly TieRead Sour = new(30, -0.3);
    private const long Now = 100_000_000_000;
    private static long DaysAgo(double days) => Now - (long)TimeSpan.FromDays(days).TotalMilliseconds;

    private static Relation Rel(Guid a, Guid b, RelationKind kind, long since = 0, long? ended = null, string? story = null, bool live = false)
        => new() { AId = a, BId = b, Kind = kind, Since = since, EndedAt = ended, Story = story, LiveTogether = live };

    private static RelationBook Book(params Relation[] relations) => new() { Relations = relations.ToList() };

    private static string? First(Guid id) => id == Justin ? "Justin" : id == Anne ? "Anne" : id == Denise ? "Denise" : null;

    private static List<AskKind> Askable(RelationBook book, TieRead mine, TieRead theirs, bool human = false, bool mutual = true)
        => RelationRules.Askable(book, Justin, Anne, mine, theirs, human, mutual, Now);

    [Fact]
    public void AskingOut_TakesMutualClosenessBothWays()
    {
        Assert.Equal(new[] { AskKind.GoOut }, Askable(Book(), Close, Close));
        Assert.Empty(Askable(Book(), Close, Cool));
        Assert.Empty(Askable(Book(), Close, Close, mutual: false));
    }

    [Fact]
    public void NobodyAsksAHuman() => Assert.Empty(Askable(Book(), Close, Close, human: true));

    [Fact]
    public void SomeoneTaken_IsNotAskedOut()
    {
        Assert.Empty(Askable(Book(Rel(Anne, Mara, RelationKind.Married)), Close, Close));
        Assert.Empty(Askable(Book(Rel(Justin, Denise, RelationKind.Together)), Close, Close));
    }

    [Fact]
    public void Exes_AreFreeAgain() => Assert.Contains(AskKind.GoOut, Askable(Book(Rel(Justin, Anne, RelationKind.Together, ended: 5)), Close, Close));

    [Fact]
    public void FriendsCanBecomeMore_FamilyCannot()
    {
        Assert.Contains(AskKind.GoOut, Askable(Book(Rel(Justin, Anne, RelationKind.Friends)), Close, Close));
        Assert.Empty(Askable(Book(Rel(Justin, Anne, RelationKind.Family)), Close, Close));
    }

    [Fact]
    public void MovingIn_ComesAfterAFewDays_AndWarmth()
    {
        Assert.Empty(Askable(Book(Rel(Justin, Anne, RelationKind.Together, DaysAgo(1))), Warm, Warm));
        Assert.Equal(new[] { AskKind.MoveIn }, Askable(Book(Rel(Justin, Anne, RelationKind.Together, DaysAgo(4))), Warm, Warm));
        Assert.Empty(Askable(Book(Rel(Justin, Anne, RelationKind.Together, DaysAgo(4))), Warm, Cool));
    }

    [Fact]
    public void MovingIn_NeedsBothFree()
    {
        var couple = Rel(Justin, Anne, RelationKind.Together, DaysAgo(4));
        Assert.Empty(Askable(Book(couple, Rel(Anne, Mara, RelationKind.Family, live: true)), Warm, Warm));
    }

    [Fact]
    public void Marrying_ComesLater_AndOnlyOnce()
    {
        Assert.Contains(AskKind.Marry, Askable(Book(Rel(Justin, Anne, RelationKind.Together, DaysAgo(15), live: true)), Warm, Warm));
        Assert.DoesNotContain(AskKind.Marry, Askable(Book(Rel(Justin, Anne, RelationKind.Together, DaysAgo(10))), Warm, Warm));
        Assert.DoesNotContain(AskKind.Marry, Askable(Book(Rel(Justin, Anne, RelationKind.Married, DaysAgo(15))), Warm, Warm));
    }

    [Fact]
    public void AnOpenAsk_BlocksAskingAnythingElse()
    {
        var book = Book(Rel(Justin, Anne, RelationKind.Together, DaysAgo(20)));
        book.Asks.Add(new RelationAsk { FromId = Anne, ToId = Justin, Kind = AskKind.Marry, At = DaysAgo(0.1) });
        Assert.Empty(Askable(book, Warm, Warm));
    }

    [Fact]
    public void ANo_HoldsForAFewDays_ThenItCanComeUpAgain()
    {
        var book = Book();
        var ask = new RelationAsk { FromId = Justin, ToId = Anne, Kind = AskKind.GoOut, At = DaysAgo(2), Yes = false };
        book.Asks.Add(ask);
        Assert.Empty(Askable(book, Close, Close));
        ask.At = DaysAgo(4);
        Assert.Contains(AskKind.GoOut, Askable(book, Close, Close));
    }

    [Fact]
    public void AnAsk_LapsesIfNobodyAnswers()
    {
        var book = Book();
        book.Asks.Add(new RelationAsk { FromId = Justin, ToId = Anne, At = DaysAgo(2) });
        Assert.Empty(book.OpenTo(Anne, Now));
    }

    [Fact]
    public void BreakingUp_OnlyOnceItsSour_AndOnlyPartners()
    {
        var couple = Book(Rel(Justin, Anne, RelationKind.Married));
        Assert.True(RelationRules.MayBreakUp(couple, Justin, Anne, Sour));
        Assert.False(RelationRules.MayBreakUp(couple, Justin, Anne, Cool));
        Assert.False(RelationRules.MayBreakUp(Book(Rel(Justin, Anne, RelationKind.Family)), Justin, Anne, Sour));
        Assert.False(RelationRules.MayBreakUp(Book(Rel(Justin, Anne, RelationKind.Together, ended: 1)), Justin, Anne, Sour));
    }

    private static readonly RelationBook Example = Book(
        Rel(Justin, Anne, RelationKind.Married, live: true),
        Rel(Justin, Denise, RelationKind.Together, ended: 1, story: "you dated in high school"));

    private static string Seen(Guid viewer, Guid other, string name, Tie? tie = null)
        => Ties.Render(new[] { RelationRules.Seen(Example, viewer, other, name, tie, First) }, DateTimeOffset.UtcNow);

    [Fact]
    public void AMarriage_IsPublic() => Assert.Contains("- Anne: married to Justin", Seen(Denise, Anne, "Anne"));

    [Fact]
    public void AnOldFlame_StaysBetweenThePair()
    {
        Assert.Contains("you dated in high school", Seen(Denise, Justin, "Justin"));
        Assert.Contains("you dated in high school", Seen(Justin, Denise, "Denise"));
        Assert.DoesNotContain("high school", Seen(Anne, Justin, "Justin"));
        Assert.DoesNotContain("high school", Seen(Anne, Denise, "Denise"));
        Assert.DoesNotContain("Justin", Seen(Anne, Denise, "Denise"));
    }

    [Fact]
    public void Spouses_NeverReadAsStrangers()
    {
        var text = Seen(Justin, Anne, "Anne");
        Assert.Contains("- Anne: you're married; you live together", text);
        Assert.DoesNotContain("never really talked", text);
        Assert.DoesNotContain("you barely know them", Seen(Justin, Anne, "Anne", new Tie { Exchanges = 1, LastAt = 1 }));
    }

    [Fact]
    public void AnOldFlame_WithNoWordSince_IsNotAStranger()
    {
        var text = Seen(Justin, Denise, "Denise");
        Assert.Contains("you've barely spoken since", text);
        Assert.DoesNotContain("never really talked", text);
    }

    [Fact]
    public void ThePairsHistory_ReadsCurrentFirst()
    {
        var book = Book(
            Rel(Justin, Anne, RelationKind.Together, ended: 5),
            Rel(Justin, Anne, RelationKind.Together, ended: 9),
            Rel(Justin, Anne, RelationKind.Friends));
        Assert.Equal("you're close friends; you used to be together", RelationRules.Standing(book, Justin, Anne));
    }

    [Fact]
    public void SomeoneLeftTheWorld_IsNotNamed()
    {
        var book = Book(Rel(Anne, Mara, RelationKind.Married));
        var seen = RelationRules.Seen(book, Denise, Anne, "Anne", null, First);
        Assert.Null(seen.Known);
    }

    [Fact]
    public void NoRelation_ReadsExactlyAsBefore()
    {
        var text = Ties.Render(new (string, Tie?)[] { ("Hana", null) }, DateTimeOffset.UtcNow);
        Assert.Contains("- Hana: you've never really talked\n", text);
    }

    [Fact]
    public void Partners_HoldHandsWithoutTheTalkingCount()
    {
        var pair = new[] { Poses.HoldHands, Poses.SlowDance };
        var stranger = new TieRead(0, 0);
        Assert.Equal(pair, Touch.Offered(pair, stranger, stranger, false, false, partners: true));
        Assert.Empty(Touch.Offered(pair, stranger, stranger, false, false));
    }

    [Fact]
    public void Partners_StillDontWhenItsSour_OrInitiateTowardAHuman()
    {
        var pair = new[] { Poses.HoldHands };
        Assert.Empty(Touch.Offered(pair, Sour, Close, false, true, partners: true));
        Assert.Empty(Touch.Offered(pair, Close, Close, otherIsHuman: true, true, partners: true));
    }

    [Fact]
    public void Reflex_KnowsYourSpouseEvenBeforeYouTalked()
    {
        var percept = new Percept(PerceptKind.Arrival, Anne, "Anne", false);
        var sense = new Sense("Justin", null, null, null, 0, 1, new Bond(0, 0, null), Array.Empty<string>(), "you're married");
        var state = ReflexWeighing.State(percept, sense);
        Assert.Contains("Justin and Anne: you're married.", state);
        Assert.DoesNotContain("doesn't really know", state);
    }

    [Fact]
    public void AHome_IsLivedInByItsHousemates_NotAMovedOutOwner()
    {
        var home = new Place { Kind = PlaceKinds.Home, OwnerId = Justin, Housemates = new List<Guid> { Anne } };
        var left = new Place { Kind = PlaceKinds.Home, OwnerId = Anne, Vacated = true };
        var places = new[] { left, home };

        Assert.True(home.LivesIn(Justin) && home.LivesIn(Anne));
        Assert.False(left.LivesIn(Anne));
        Assert.Same(home, places.HomeOf(Anne));
        Assert.False(new Place { Kind = PlaceKinds.Public, OwnerId = Anne }.LivesIn(Anne));
    }

    [Fact]
    public void AHome_IsNamedForWhoLivesThere()
    {
        Assert.Equal("Mara's place", Homes.Named(new[] { "Mara" }));
        Assert.Equal("Mara and Lena's place", Homes.Named(new[] { "Mara", "Lena Brandt" }));
        Assert.Equal("Mara, Lena and Eiko's place", Homes.Named(new[] { "Mara", "Lena", "Eiko" }));
    }
}
