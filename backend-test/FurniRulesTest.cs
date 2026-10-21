using PartyTown.World.Places;

namespace BackendTest;

/// <summary>Furni decay and actions are pure: condition from timestamps, care capped to what's needed.</summary>
public class FurniRulesTest
{
    private const long Hour = 3_600_000;

    private static Furni Plant(long tendedAt) => new() { Id = "plant", Kind = FurniKinds.Plant, Name = "the plant", TendedAt = tendedAt };

    [Fact]
    public void Plant_DriesOverADay_AndOnlyThenAsksForWater()
    {
        var plant = Plant(0);

        Assert.Null(FurniRules.CareFor(plant, 2 * Hour));
        Assert.Null(FurniRules.Notable(plant, 2 * Hour));
        Assert.Equal(1, FurniRules.CareFor(plant, 10 * Hour)!.Priority);
        Assert.Equal("the plant looks a bit dry", FurniRules.Notable(plant, 10 * Hour));
        Assert.Equal(2, FurniRules.CareFor(plant, 20 * Hour)!.Priority);
        Assert.Equal("the plant is wilting", FurniRules.Notable(plant, 20 * Hour));

        FurniRules.Tend(plant, 20 * Hour);
        Assert.Null(FurniRules.CareFor(plant, 21 * Hour));
    }

    [Fact]
    public void Coffee_DirtiesTheCups_UntilSomeoneWashesUp()
    {
        var coffee = new Furni { Id = "espresso", Kind = FurniKinds.Coffee, Name = "the espresso machine" };
        var cups = new Furni { Id = "cups", Kind = FurniKinds.Cups, Name = "the cups" };

        for (var i = 0; i < 3; i++) FurniRules.Use(coffee, cups, 0, "Hana", null);

        Assert.Equal("dirty cups are piling up", FurniRules.Notable(cups, 0));
        Assert.Equal("washes up the cups", FurniRules.Tend(cups, 0));
        Assert.Equal(0, cups.Dirty);
    }

    [Fact]
    public void BrokenMachine_OffersFixingInsteadOfCoffee()
    {
        var coffee = new Furni { Id = "espresso", Kind = FurniKinds.Coffee, Name = "the espresso machine", Broken = true };

        Assert.Null(FurniRules.UseOf(coffee, 0));
        Assert.Equal("Try to fix the espresso machine", FurniRules.CareFor(coffee, 0)!.Label);
        Assert.Null(FurniRules.Use(coffee, null, 0, "Hana", null));
    }

    [Fact]
    public void Jukebox_PlaysTheQuotedSong_ForAWhile()
    {
        var jukebox = new Furni { Id = "jukebox", Kind = FurniKinds.Jukebox, Name = "the jukebox" };

        FurniRules.Use(jukebox, null, 0, "Denise", "put on 'Heroes' and dance badly");

        Assert.Equal("the jukebox is playing \"Heroes\" (Denise put it on)", FurniRules.Notable(jukebox, 60_000));
        Assert.Null(FurniRules.UseOf(jukebox, 60_000));
        Assert.Null(FurniRules.Notable(jukebox, FurniRules.SongLasts.Ticks / TimeSpan.TicksPerMillisecond + 1));
    }
}
