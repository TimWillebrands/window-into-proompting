using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

public class BearingTest
{
    private static Reserves Felt(double fed = 1, double rested = 1, bool ailing = false) => new(fed, rested, 0, 0, false, true, false, ailing);

    [Fact]
    public void AFineBody_AndOrdinaryHungerOrTiredness_GiveNothingAway()
    {
        Assert.Null(Bearing.Of(Felt()));
        Assert.Null(Bearing.Of(Felt(fed: 0.2)));
        Assert.Null(Bearing.Of(Felt(rested: 0.2)));
        Assert.Null(Bearing.Of(Reserves.Unfelt));
        Assert.Null(Bearing.Of(Felt(fed: 0.05, rested: 0.05, ailing: true) with { Felt = false }));
    }

    [Fact]
    public void Distress_AndAiling_ShowEachInTheirOwnWords()
    {
        Assert.Equal(Bearing.Famished, Bearing.Of(Felt(fed: 0.1)));
        Assert.Equal(Bearing.Spent, Bearing.Of(Felt(rested: 0.1)));
        Assert.Equal(Bearing.Unwell, Bearing.Of(Felt(ailing: true)));
    }

    [Fact]
    public void Ailing_WinsOverSpent_AndSpentWinsOverFamished()
    {
        Assert.Equal(Bearing.Unwell, Bearing.Of(Felt(fed: 0.1, rested: 0.1, ailing: true)));
        Assert.Equal(Bearing.Spent, Bearing.Of(Felt(fed: 0.1, rested: 0.1)));
    }

    [Fact]
    public void ABearing_IsShownOnlyUntilItExpires()
    {
        var body = new Body { Bearing = Bearing.Spent, BearingUntil = 1_000 };

        Assert.Equal(Bearing.Spent, Bearing.Shown(body, 999));
        Assert.Null(Bearing.Shown(body, 1_000));
        Assert.Null(Bearing.Shown(new Body { Bearing = Bearing.Spent }, 0));
    }

    [Fact]
    public void ALifeTurn_WritesTheBearing_OnlyWhenItChangesOrIsAboutToRunOut()
    {
        var half = (long)Bio.FeltFor.TotalMilliseconds / 2;
        var showing = new Body { Bearing = Bearing.Spent, BearingUntil = 10 * half };

        Assert.False(Bearing.Due(new Body(), null, 0));
        Assert.False(Bearing.Due(new Body { Bearing = Bearing.Spent, BearingUntil = 5 }, null, 10));
        Assert.True(Bearing.Due(new Body(), Bearing.Spent, 0));
        Assert.False(Bearing.Due(showing, Bearing.Spent, 8 * half));
        Assert.True(Bearing.Due(showing, Bearing.Spent, 9 * half + 1));
        Assert.True(Bearing.Due(showing, Bearing.Famished, 0));
        Assert.True(Bearing.Due(showing, null, 0));
    }
}
