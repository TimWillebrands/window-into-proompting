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
    public void Hungry_OneWayToEatAlwaysHasASeat_AndTheMenuStaysWhole()
    {
        var home = Lunch with { Label = "Head home (to make something to eat)", Verb = "go" };
        var cafe = Lunch with { Label = "Go to the Café (to get something to eat)", Verb = "go", Priority = 1 };

        var menu = LifeMenu.Seat(Apps(home, cafe), a => a.Priority, null, 10, 3, new[] { Bio.Food });

        Assert.Equal(cafe, menu[0]);
        Assert.DoesNotContain(home, menu);
        Assert.Equal(10, menu.Count);
        Assert.Equal(9, menu.Count(a => a.Meets is null));
    }

    [Fact]
    public void TheLongerDistressGoesUnanswered_TheLessElseThereIs()
    {
        int Seats(double strain) => LifeMenu.Seat(Apps(Lunch), a => a.Priority, Bio.Food, 10, 3, new[] { Bio.Food }, strain).Count;

        Assert.Equal(new[] { Bio.Narrowed, Bio.NarrowedAfterAnHour, Bio.NarrowedWhenBreaking }, new[] { Seats(0), Seats(0.3), Seats(0.6) });
        Assert.Equal(Lunch, LifeMenu.Seat(Apps(Lunch), a => a.Priority, Bio.Food, 10, 3, new[] { Bio.Food }, 0.6)[0]);
        Assert.Equal(10, LifeMenu.Seat(Apps(), a => a.Priority, Bio.Food, 10, 3, new[] { Bio.Food }, 0.9).Count);
    }

    [Fact]
    public void ANeedNothingMeets_ChangesNothing()
    {
        var plain = LifeMenu.Seat(Apps(), a => a.Priority, null, 10, 3);
        var needy = LifeMenu.Seat(Apps(), a => a.Priority, null, 10, 3, new[] { Bio.Food, Bio.Rest });

        Assert.Equal(plain.Select(a => a.Label), needy.Select(a => a.Label));
    }

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
