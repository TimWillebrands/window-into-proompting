using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Self;

namespace BackendTest;

public class LifeMenuTest
{
    private static readonly Affordance Lunch = new("places", "Make yourself lunch in the kitchen", 0, Guid.NewGuid(), "eat:cook", Meets: Bio.Food);

    private static List<IEnumerable<Affordance>> Apps(params Affordance[] extra)
    {
        IEnumerable<Affordance> App(string id, int priority, int count)
            => Enumerable.Range(0, count).Select(i => new Affordance(id, $"{id} {i}", priority, Guid.NewGuid(), "go"));
        return new List<IEnumerable<Affordance>>
        {
            App("rooms", 9, 4), App("messenger", 8, 4), App("board", 7, 4), App("places", 6, 4).Concat(extra).ToList(),
        };
    }

    private static List<Affordance> Seat(string? distress, params Affordance[] extra)
        => LifeMenu.Seat(Apps(extra), a => a.Priority, distress, 10, 3);

    [Fact]
    public void InDistress_WhatWouldRelieveItIsSeated_AndTheMenuNarrows()
    {
        var menu = Seat(Bio.Food, Lunch);

        Assert.Equal(Lunch, menu[0]);
        Assert.True(menu.Count <= Bio.Narrowed);
    }

    [Fact]
    public void InDistress_WithNothingToRelieveIt_TheMenuKeepsItsSeats()
    {
        var menu = Seat(Bio.Rest, Lunch);

        Assert.Equal(10, menu.Count);
        Assert.DoesNotContain(Lunch, menu);
    }

    [Fact]
    public void WithoutDistress_NothingChanges_AndNoAppTakesMoreThanItsShare()
    {
        var menu = Seat(null, Lunch);

        Assert.Equal(10, menu.Count);
        Assert.DoesNotContain(Lunch, menu);
        Assert.All(menu.GroupBy(a => a.AppId), app => Assert.True(app.Count() <= 3));
        Assert.Equal(menu.OrderByDescending(a => a.Priority), menu);
    }
}
