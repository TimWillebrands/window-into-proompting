using System.Text.Json.Nodes;
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

    private static Place Judged(string name, string? purpose, string? fare, string? archetype = null)
    {
        var place = Public(name, purpose, archetype);
        place.Fare = fare;
        return place;
    }

    [Theory]
    [InlineData("Tante Sjaan", "a ramen counter under the tracks", Fares.Meal, Bio.Meal)]
    [InlineData("het Wafelhuis", "where people queue for stroopwafels", Fares.Light, Bio.LightMeal)]
    [InlineData("the Tuin", "where people go for a drink outside and unwind", Fares.Snack, Bio.Snack)]
    [InlineData("the Bar Association", "where lawyers meet", Fares.None, null)]
    [InlineData("Café Brecht", "where people get coffee and linger", Fares.None, null)]
    [InlineData("the reading room", "where people read quietly; no food or drinks", Fares.Meal, Bio.Meal)]
    public void AKnownFare_DecidesWhatIsSold_OverWhatTheWordsSay(string name, string purpose, string fare, double? sold)
        => Assert.Equal(sold, Sustenance.Sells(Judged(name, purpose, fare)));

    [Fact]
    public void AnUnknownFare_FallsBackToTheArchetypeAndTheWords()
    {
        Assert.Equal(Bio.LightMeal, Sustenance.Sells(Judged("Café Brecht", "where people come and go", null)));
        Assert.Equal(Bio.Meal, Sustenance.Sells(Judged("Sora", null, null, "restaurant")));
        Assert.Equal(Bio.Snack, Sustenance.Sells(Judged("the Rooftop Bar", "where people come and go", "maybe")));
        Assert.Null(Sustenance.Sells(Judged("Tante Sjaan", "a counter under the tracks", null)));
    }

    [Fact]
    public void AFareIsAboutPublicPlacesOnly()
    {
        var home = PlaceSeeds.Home(Denise, "Denise");
        home.Fare = Fares.Meal;

        Assert.Null(Sustenance.Sells(home));
        Assert.False(Fares.Due(home));
        Assert.False(Fares.Due(PlaceSeeds.Locale("the office in Zuid")));
        var closed = Public("the studio", "where art gets made");
        closed.Private = true;
        Assert.False(Fares.Due(closed));
    }

    [Theory]
    [InlineData("restaurant", Fares.Meal)]
    [InlineData("cafe", Fares.Light)]
    [InlineData("bar", Fares.Snack)]
    [InlineData("office", Fares.None)]
    [InlineData("park", Fares.None)]
    [InlineData("place", null)]
    [InlineData(null, null)]
    public void AKitArchetype_SettlesTheFare_WithoutAJudge(string? archetype, string? fare)
        => Assert.Equal(fare, Fares.OfKit(Public("the Corner", null, archetype)));

    [Fact]
    public void AFareIsDue_UntilJudgedForThisNameAndPurpose()
    {
        var place = Public("Tante Sjaan", "a ramen counter under the tracks");
        Assert.True(Fares.Due(place));

        (place.Fare, place.FareFor) = (Fares.Meal, Fares.For(place));
        Assert.False(Fares.Due(place));

        place.Purpose = "a soup kitchen under the tracks";
        Assert.True(Fares.Due(place));
        place.FareFor = Fares.For(place);
        place.Name = "the Arch";
        Assert.True(Fares.Due(place));

        var kit = Public("Sora", null, "restaurant");
        Assert.True(Fares.Due(kit));
        (kit.Fare, kit.FareFor) = (Fares.Meal, Fares.For(kit));
        Assert.False(Fares.Due(kit));
    }

    [Fact]
    public void TheJudgesFare_IsTheLikeliestOption_WhenItIsLikelyEnough()
    {
        PartyTown.Services.SystemOne.SystemOneAnswers Said(params (string Key, double P)[] probs)
        {
            var answers = new PartyTown.Services.SystemOne.SystemOneAnswers();
            answers.Choices[Fares.Key] = probs.ToDictionary(p => p.Key, p => p.P);
            return answers;
        }

        Assert.Equal(Fares.Light, Fares.OfKit(new Place { Kind = PlaceKinds.Public, Name = "the Café", Scene = "cafe" }));
        Assert.Equal((Fares.Meal, 0.7), Fares.Read(Said((Fares.Meal, 0.7), (Fares.None, 0.3))));
        Assert.Equal(Fares.Light, Fares.Read(Said((Fares.Light, 0.46), (Fares.Snack, 0.43), (Fares.Meal, 0.02), (Fares.None, 0.09)))!.Value.Fare);
        Assert.Equal(Fares.Light, Fares.Read(Said((Fares.Light, 0.4), (Fares.Snack, 0.25), (Fares.None, 0.35)))!.Value.Fare);
        Assert.Equal((Fares.None, 0.55), Fares.Read(Said((Fares.Light, 0.3), (Fares.Snack, 0.15), (Fares.None, 0.55))));
        Assert.Equal(Fares.None, Fares.Read(Said((Fares.Light, 0.3), (Fares.None, 0.3)))!.Value.Fare);
        Assert.Null(Fares.Read(new PartyTown.Services.SystemOne.SystemOneAnswers()));
    }

    [Fact]
    public void TheQuestion_NamesThePlace_AndSaysWhatItIsFor()
    {
        var place = Public("the Tuin", "where people go for a drink outside and unwind");
        place.Description = "a beer garden behind the old bathhouse";

        Assert.Equal("A place in town: the Tuin, where people go for a drink outside and unwind.\nWhat it's like: a beer garden behind the old bathhouse.", Fares.State(place));
        var question = Assert.IsType<PartyTown.Services.SystemOne.ChoiceQuestion>(Fares.Question(place)[Fares.Key]);
        Assert.Equal("Can anyone who comes in buy something to eat or drink at the Tuin?", question.Instructions);
        Assert.Equal(new[] { Fares.Meal, Fares.Light, Fares.Snack, Fares.None }, question.Options.Keys);
    }

    [Fact]
    public void AThingTheDesignerSaysFeeds_IsAFreeFare_OverTheCatalogue()
    {
        var soup = new Furni { Id = "soup", Kind = FurniKinds.Feature, Name = "the soup pot", SpotId = "table", Do = "Get a bowl of soup from the pot", Feeds = Bio.Meal };
        var offer = Library.Offers["herring-cart"];
        var cart = new Furni { Id = "herring", Kind = FurniKinds.Feature, Name = "the herring cart", SpotId = "counter", Do = offer.Do, Gives = offer.Gives, Feeds = Bio.Snack };
        var market = Public("the market", "where people wander between the stalls", null, null, soup, cart);

        var fares = Sustenance.Of(market, Denise);

        Assert.Equal(new (string, double, int, string?)[] { ("thing:soup", Bio.Meal, 0, "table"), ("thing:herring", Bio.Snack, 0, "counter") },
            fares.Select(f => (f.Key, f.Feeds, f.Cost, f.SpotId)));
        Assert.Equal(Bio.Meal, Sustenance.Feeds(market, soup)!.Amount);
        Assert.Equal(Bio.Snack, Sustenance.Feeds(market, cart)!.Amount);
        Assert.Equal("thing:soup", Sustenance.Best(fares, 0)!.Key);
    }

    [Fact]
    public void Featurize_CarriesFeeds_AndTheCheckerDropsNonsense()
    {
        var spots = new List<Spot> { new() { Id = "pot", Name = "the soup pot" }, new() { Id = "bar", Name = "the bar" } };
        var scene = (JsonObject)JsonNode.Parse("""
            {"props": [
              {"p": "k-stove", "at": [1, 1], "name": "the soup pot", "do": "Get a bowl of soup from the pot", "gives": "what soup it is today", "feeds": "meal", "spot": "pot"},
              {"p": "counter", "at": [3, 1], "name": "the crisps", "do": "Take a bag of crisps", "feeds": "Snack", "spot": "bar"},
              {"p": "plant", "at": [5, 1], "name": "the fern", "do": "Water the fern", "feeds": "lots"},
              {"p": "sofa", "at": [6, 3], "feeds": "meal"}
            ]}
            """)!;
        var notes = new List<string>();

        var features = PlaceDesign.Featurize(scene, spots, notes);

        Assert.Equal(new double?[] { Bio.Meal, Bio.Snack, null }, features.Select(f => f.Feeds));
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Contains("'plant'") && n.Contains("\"lots\""));
        Assert.Contains(notes, n => n.Contains("'sofa'"));
        Assert.DoesNotContain("feeds", scene.ToJsonString());

        var place = Public("the canteen", "where people meet");
        Features.Apply(place, features, 0);
        Assert.Equal(Bio.Meal, place.Furni.Single(f => f.Name == "the soup pot").Feeds);
    }

    [Fact]
    public void AMadeThing_CanFeed_LikeAPlacedOne()
    {
        Assert.Equal(Bio.Snack, Library.OfferOf(new JsonObject { ["do"] = "Grab a stroopwafel from the tin", ["feeds"] = "snack" })!.Feeds);
        Assert.Null(Library.OfferOf(new JsonObject { ["do"] = "Grab a stroopwafel from the tin", ["feeds"] = "a lot" })!.Feeds);
        Assert.Null(Library.OfferOf(new JsonObject { ["do"] = "Wind up the music box" })!.Feeds);
    }
}
