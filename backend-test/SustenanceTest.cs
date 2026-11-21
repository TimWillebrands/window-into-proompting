using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

public class SustenanceTest
{
    private static readonly Guid Denise = Guid.NewGuid();
    private static readonly Guid Pieter = Guid.NewGuid();
    private static readonly DateTimeOffset Lunchtime = new(2026, 11, 21, 12, 30, 0, TimeSpan.Zero);

    private static Place Public(string name, string? purpose = null, string? archetype = null, string? scene = null, params Furni[] furni) => new()
    {
        Id = Guid.NewGuid(), Name = name, Kind = PlaceKinds.Public, Purpose = purpose, Archetype = archetype, CustomScene = scene, Furni = furni.ToList(),
        Spots = new List<Spot> { new() { Id = "counter", Name = "the counter", Staff = true }, new() { Id = "table", Name = "the table", Seat = true } },
    };

    [Fact]
    public void AtHome_YouCookInTheKitchen_ForNothing()
    {
        var home = PlaceSeeds.Home(Denise, "Denise");

        var fare = Assert.Single(Sustenance.Of(home, Denise));

        Assert.Equal((Sustenance.Cook, Bio.Meal, 0, "kitchen", true), (fare.Key, fare.Feeds, fare.Cost, fare.SpotId, fare.Cooked));
        Assert.Equal("Make yourself lunch in the kitchen", Sustenance.Label(fare, Lunchtime, Array.Empty<string>()));
        Assert.Equal("Make lunch for you and Pieter, in the kitchen", Sustenance.Label(fare, Lunchtime, new[] { "Pieter Voss" }));
        Assert.Equal("Make yourself lunch in the kitchen (nothing since 08:10)", Sustenance.Label(fare, Lunchtime, Array.Empty<string>(), "nothing since 08:10"));
        Assert.Equal("makes lunch at home", Sustenance.Summary(fare, home, Denise, Lunchtime, Array.Empty<string>()));
        Assert.Equal("makes lunch at home, for Pieter and Hana too", Sustenance.Summary(fare, home, Denise, Lunchtime, new[] { "Pieter Voss", "Hana" }));
        Assert.Equal("making lunch", Sustenance.Busy(fare, Lunchtime));
        Assert.Equal("to make something to eat", Sustenance.Why(fare));
    }

    [Fact]
    public void SomeoneElsesHome_FeedsNobodyWhoDoesntLiveThere()
        => Assert.Empty(Sustenance.Of(PlaceSeeds.Home(Denise, "Denise"), Pieter));

    [Fact]
    public void WhatAPlaceIs_SaysWhatItSells()
    {
        Assert.Equal(Bio.LightMeal, Sustenance.Sells(Public("the Café", "where people get coffee and linger")));
        Assert.Equal(Bio.Meal, Sustenance.Sells(Public("Sora", "where people go out to eat", "restaurant")));
        Assert.Equal(Bio.Snack, Sustenance.Sells(Public("the Tuin", "where people go for a drink outside and unwind")));
        Assert.Equal(Bio.Snack, Sustenance.Sells(Public("De Zwarte Kat", null, "bar")));
        Assert.Null(Sustenance.Sells(Public("The Works", "where people work at desks and grab coffee between meetings", "office")));
        Assert.Null(Sustenance.Sells(Public("the Lobby", "where people come in, wait for someone and check the noticeboard")));
        Assert.Null(Sustenance.Sells(Public("the barbershop", "where people get a haircut")));
        Assert.Null(Sustenance.Sells(PlaceSeeds.Home(Denise, "Denise")));
    }

    [Theory]
    [InlineData("where people get coffee and linger", Bio.LightMeal)]
    [InlineData("where people go out to eat", Bio.Meal)]
    [InlineData("where people go for a drink", Bio.Snack)]
    [InlineData("where people go for a drink outside and unwind", Bio.Snack)]
    public void TheKitsPurposes_StillSell(string purpose, double sold)
        => Assert.Equal(sold, Sustenance.Sells(Public("the Corner", purpose)));

    [Theory]
    [InlineData("where people read quietly; no food or drinks")]
    [InlineData("where office workers eat their lunch on the grass")]
    [InlineData("where people grow their own food")]
    [InlineData("where neighbours meet, play cards and grab a coffee")]
    [InlineData("a roof terrace where people watch the sunset")]
    public void APurposeThatOnlyMentionsFood_SellsNothing(string purpose)
        => Assert.Null(Sustenance.Sells(Public("the Corner", purpose)));

    [Theory]
    [InlineData("Café Brecht", Bio.LightMeal)]
    [InlineData("the Bakkerij", Bio.LightMeal)]
    [InlineData("Restaurant Sora", Bio.Meal)]
    [InlineData("the Rooftop Bar", Bio.Snack)]
    [InlineData("the barbershop", null)]
    [InlineData("Barcelona Lounge", null)]
    public void WhatThePlaceIsCalled_SellsByWholeWords(string name, double? sold)
        => Assert.Equal(sold, Sustenance.Sells(Public(name, "where people come and go")));

    [Fact]
    public void WhereFoodIsSold_YouOrder_AndItCosts()
    {
        var cafe = Public("the Café", "where people get coffee and linger");

        var fare = Assert.Single(Sustenance.Of(cafe, Denise));

        Assert.Equal((Sustenance.Order, Bio.LightMeal, Sustenance.LightMealCosts, false), (fare.Key, fare.Feeds, fare.Cost, fare.Cooked));
        Assert.Null(fare.SpotId);
        Assert.Equal("Order lunch at the counter (6 credits)", Sustenance.Label(fare, Lunchtime, Array.Empty<string>()));
        Assert.Equal("Order lunch at the counter (6 credits; you haven't eaten yet today)", Sustenance.Label(fare, Lunchtime, Array.Empty<string>(), "you haven't eaten yet today"));
        Assert.Equal("has lunch at the Café (6 credits)", Sustenance.Summary(fare, cafe, Denise, Lunchtime, Array.Empty<string>()));
        Assert.Equal("having lunch", Sustenance.Busy(fare, Lunchtime));
        Assert.Equal("to get something to eat", Sustenance.Why(fare));

        var bar = Sustenance.Of(Public("the Tuin", "where people go for a drink outside and unwind"), Denise).Single();
        Assert.Equal("Get something small to eat at the counter (3 credits)", Sustenance.Label(bar, Lunchtime, Array.Empty<string>()));
    }

    [Fact]
    public void AThingThatFeeds_IsAFreeSnack_WhetherSeededOrPinnedByAScene()
    {
        var offer = Library.Offers["herring-cart"];
        var seeded = new Furni { Id = "herring", Kind = FurniKinds.Feature, Name = "the herring cart", SpotId = "cart", Do = offer.Do, Gives = offer.Gives };
        var centre = Public("the city centre", "where people pass through", null, null, seeded);
        centre.At = "in";

        var fare = Assert.Single(Sustenance.Of(centre, Denise));
        Assert.Equal(("thing:herring", 0.4, 0, "cart"), (fare.Key, fare.Feeds, fare.Cost, fare.SpotId));
        Assert.Equal("Get something to eat at the herring cart", Sustenance.Label(fare, Lunchtime, Array.Empty<string>()));
        Assert.Equal("Get something to eat at the herring cart (nothing since 08:10)", Sustenance.Label(fare, Lunchtime, Array.Empty<string>(), "nothing since 08:10"));
        Assert.Equal("gets something to eat at the herring cart in the city centre", Sustenance.Summary(fare, centre, Denise, Lunchtime, Array.Empty<string>()));
        Assert.Equal(0.4, Sustenance.Feeds(centre, seeded)!.Amount);

        const string scene = """{"furni": {"ices": {"p": "ice-cream-cart"}}, "props": []}""";
        var pinned = new Furni { Id = "ices", Kind = FurniKinds.Feature, Name = "Gino's cart", SpotId = "table", Do = "Ask Gino for the flavour of the day" };
        var square = Public("the square", "where people sit in the sun", null, scene, pinned, new Furni { Id = "plant", Kind = FurniKinds.Plant, Name = "the plant" });
        Assert.Equal("thing:ices", Assert.Single(Sustenance.Of(square, Denise)).Key);
        Assert.Null(Sustenance.Feeds(square, square.Furni[1]));
    }

    [Fact]
    public void AKitchenSomewherePublic_IsThereToCookIn()
    {
        const string scene = """{"props": [{"p": "k-stove", "spot": "table"}, {"p": "sofa"}]}""";
        var commons = Public("the commons", "where the building shares a kitchen", null, scene);

        var fare = Assert.Single(Sustenance.Of(commons, Pieter));

        Assert.Equal((Sustenance.Cook, 0, "table", true), (fare.Key, fare.Cost, fare.SpotId, fare.Cooked));
        Assert.Equal("Make yourself lunch at the table", Sustenance.Label(fare, Lunchtime, Array.Empty<string>()));
    }

    [Fact]
    public void WhereFoodIsSold_NobodyCooksForFree_KitchenOrNot()
    {
        const string scene = """{"props": [{"p": "k-stove", "spot": "table"}]}""";

        var fare = Assert.Single(Sustenance.Of(Public("the Café", "where people get coffee and linger", null, scene), Pieter));

        Assert.Equal((Sustenance.Order, Sustenance.LightMealCosts), (fare.Key, fare.Cost));
    }

    [Fact]
    public void AKitchenBar_IsNotAKitchen()
    {
        const string scene = """{"props": [{"p": "k-kitchen-bar", "spot": "table"}]}""";

        Assert.False(Library.Feeds.ContainsKey("k-kitchen-bar"));
        Assert.Empty(Sustenance.Of(Public("the commons", "where the building meets", null, scene), Pieter));
    }

    [Fact]
    public void Best_IsWhatFillsMostOfWhatTheyCanPay()
    {
        var fares = new List<Fare>
        {
            new("thing:cart", 0.4, 2, null, false, "at the cart"),
            new(Sustenance.Order, Bio.Meal, 9, null, false, "at the pass"),
        };

        Assert.Equal(Sustenance.Order, Sustenance.Best(fares, 20)!.Key);
        Assert.Equal("thing:cart", Sustenance.Best(fares, 5)!.Key);
        Assert.Null(Sustenance.Best(fares, 1));
    }

    [Fact]
    public void Meal_GoesByTheHour()
    {
        string At(int hour) => Sustenance.Meal(new DateTimeOffset(2026, 11, 21, hour, 0, 0, TimeSpan.Zero));
        Assert.Equal(new[] { "a late bite", "breakfast", "lunch", "something to eat", "dinner", "a late bite" }, new[] { At(2), At(8), At(13), At(16), At(19), At(23) });
    }

    [Fact]
    public void ALocale_HasNoKitchenOfItsOwn()
        => Assert.Empty(Sustenance.Of(PlaceSeeds.Locale("the office in Zuid"), Denise));
}
