using PartyTown.World.Self;

namespace BackendTest;

public class BioEconomyTest
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(1));

    private static long Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    private static SelfState Lived(DateTimeOffset at, double fed = 1, double rested = 1, DateTimeOffset? ate = null, DateTimeOffset? up = null) => new()
    {
        Fed = fed, Rested = rested, BodyAt = Ms(at), LivedAt = Ms(at), AteAt = ate is { } a ? Ms(a) : 0, UpSince = Ms(up ?? at.AddHours(-4)),
    };

    [Fact]
    public void ABodyNobodyLivedIn_IsNotFelt()
    {
        var body = Bio.Now(new SelfState(), Noon);

        Assert.False(body.Felt);
        Assert.Equal(Hunger.None, body.Hunger);
        Assert.Equal(Tiredness.Fresh, body.Tiredness);
        Assert.Null(Bio.Distress(body));
        Assert.Empty(Bio.Lines(body, Noon));
        Assert.Equal("low", Bio.Energy("low", body));
    }

    private static Reserves LivedUntil(SelfState s, DateTimeOffset from, int minutes)
    {
        for (var t = 30; t < minutes; t += 30) Bio.Settle(s, from.AddMinutes(t), away: false);
        return Bio.Settle(s, from.AddMinutes(minutes), away: false);
    }

    [Fact]
    public void Hunger_ComesWithLivedTime_BandByBand()
    {
        Assert.Equal(Hunger.None, LivedUntil(Lived(Noon), Noon, 60).Hunger);
        Assert.Equal(Hunger.Peckish, LivedUntil(Lived(Noon), Noon, 240).Hunger);
        Assert.Equal(Hunger.Hungry, LivedUntil(Lived(Noon), Noon, 310).Hunger);
        Assert.Equal(Hunger.VeryHungry, LivedUntil(Lived(Noon), Noon, 400).Hunger);
        Assert.Equal(Hunger.Famished, LivedUntil(Lived(Noon), Noon, 480).Hunger);
    }

    [Fact]
    public void ABodyNobodyLivedIn_DoesNotRise()
    {
        var s = new SelfState();

        Bio.Rise(s, 8, Noon);

        Assert.False(Bio.Now(s, Noon).Felt);
        Assert.Equal((1, 1, 0L, 0L, 0L), (s.Fed, s.Rested, s.UpSince, s.BodyAt, s.LivedAt));
    }

    [Fact]
    public void BetweenLifeTurns_NothingDrains_AndAfterTwoHoursItIsNoLongerFelt()
    {
        var s = Lived(Noon, fed: 0.9, rested: 0.9);

        var soon = Bio.Now(s, Noon.AddMinutes(90));
        var later = Bio.Now(s, Noon + Bio.FeltFor + TimeSpan.FromMinutes(1));

        Assert.Equal((0.9, 0.9, true), (soon.Fed, soon.Rested, soon.Felt));
        Assert.Equal((0.9, 0.9, false), (later.Fed, later.Rested, later.Felt));
        Assert.Empty(Bio.Lines(later, Noon.AddHours(3)));
        Assert.Equal(Ms(Noon), s.BodyAt);
    }

    [Fact]
    public void ALifeTurnAfterAGap_DrainsAtMostSeventyFiveMinutes()
    {
        var s = Lived(Noon, fed: 0.9, rested: 0.9);

        var body = Bio.Settle(s, Noon.AddHours(10), away: false);

        Assert.Equal(0.9 - 1.25 * Bio.HungerPerHour, body.Fed, 6);
        Assert.Equal(0.9 - 1.25 * Bio.TiringPerHour, body.Rested, 6);
        Assert.True(body.Felt);
        Assert.Equal(Ms(Noon.AddHours(10)), s.LivedAt);
    }

    [Fact]
    public void ABodyUnlivedForMoreThanADay_BeginsOrdinaryAgain()
    {
        var s = Lived(Noon, fed: 0.1, rested: 0.1, ate: Noon.AddHours(-1));
        s.Energy = "medium";
        var next = Noon.AddHours(27);

        var body = Bio.Settle(s, next, away: false);

        Assert.Equal((0.7, 0L), (body.Fed, s.AteAt));
        Assert.Equal(Bio.Cap("medium") - 7.5 * Bio.TiringPerHour, body.Rested, 6);
        Assert.Equal(Ms(new DateTimeOffset(2026, 11, 22, 7, 30, 0, TimeSpan.FromHours(1))), s.UpSince);
        Assert.Null(Bio.Distress(body));
    }

    [Fact]
    public void Settle_OnAFirstTurn_StartsSomewhereOrdinary()
    {
        var s = new SelfState { Energy = "medium" };

        var body = Bio.Settle(s, Noon, away: false);

        Assert.True(body.Felt);
        Assert.Equal(Hunger.None, body.Hunger);
        Assert.Equal(Tiredness.Fine, body.Tiredness);
        Assert.Equal(Ms(Noon.AddHours(-4.5)), s.UpSince);
    }

    [Fact]
    public void Eating_FillsUp_AndSaysHowHungryTheyWere()
    {
        var s = Lived(Noon, fed: 0.4);

        var before = Bio.Eat(s, Bio.Snack, Noon);
        Assert.Equal(0.4, before, 6);
        Assert.Equal(0.75, s.Fed, 6);
        Assert.Equal(Ms(Noon), s.AteAt);

        Bio.Eat(s, Bio.Meal, Noon);
        Assert.Equal(1, s.Fed);
    }

    [Fact]
    public void Relief_IsFeltByHowMuchItWasNeeded()
    {
        Assert.Equal(0.5, Bio.Relief(1, Bio.Meal), 6);
        Assert.Equal(0.68, Bio.Relief(0.4, Bio.Meal), 6);
        Assert.True(Bio.Relief(0.1, Bio.Meal) > Bio.Relief(0.4, Bio.Meal));
        Assert.True(Bio.Relief(0.1, Bio.Snack) < Bio.Relief(0.1, Bio.Meal));
    }

    [Fact]
    public void Away_OffscreenLifeFeedsThem()
    {
        var s = Lived(Noon, fed: 0.2);

        var body = Bio.Settle(s, Noon.AddMinutes(10), away: true);

        Assert.Equal(Bio.AwayFed, body.Fed);
        Assert.Equal(Ms(Noon.AddMinutes(10)), s.AteAt);
    }

    [Fact]
    public void Rising_RestoresByHoursSlept_AndLeavesThemWantingBreakfast()
    {
        var evening = Noon.AddHours(10);
        var morning = Noon.AddHours(19);
        var full = Lived(evening, fed: 0.95, rested: 0.2, ate: evening.AddHours(-1));
        var empty = Lived(evening, fed: 0.05, rested: 0.2);
        var shortNight = Lived(evening, fed: 0.5, rested: 0.2);

        Bio.Rise(full, 8, morning);
        Bio.Rise(empty, 8, morning);
        Bio.Rise(shortNight, 4, morning);

        Assert.Equal(1, full.Rested);
        Assert.Equal(Bio.WakesAtMost, full.Fed);
        Assert.Equal(Bio.WakesAtLeast, empty.Fed);
        Assert.Equal(Ms(morning), full.UpSince);
        Assert.Equal(0.2 + 4 * Bio.SleepPerHour, shortNight.Rested, 6);
        var up = Bio.Settle(full, morning.AddMinutes(1), away: false);
        Assert.Equal(Hunger.Hungry, up.Hunger);
        Assert.Equal(Bio.WakesAtMost - Bio.HungerPerHour / 60, up.Fed, 6);
        Assert.Equal("you haven't eaten yet today", Bio.Since(up, morning));
    }

    [Fact]
    public void MorningsInAWorldWithNoLifeTurns_NeverWakeTheBody_OrDrainIt()
    {
        var never = new SelfState();
        var once = Lived(Noon, fed: 0.9, rested: 0.5);

        for (var day = 1; day <= 5; day++)
        {
            var morning = Noon.AddDays(day).AddHours(-5);
            Bio.Rise(never, 8, morning);
            Bio.Rise(once, 8, morning);
            Assert.False(Bio.Now(never, morning.AddMinutes(1)).Felt);
            Assert.False(Bio.Now(once, morning.AddMinutes(1)).Felt);
            Assert.Equal((1, 1), (never.Fed, never.Rested));
            Assert.Equal((Bio.WakesAtMost, 1), (once.Fed, once.Rested));
        }

        Assert.Equal(0.7, Bio.Settle(once, Noon.AddDays(5), away: false).Fed);
    }

    [Fact]
    public void HowTheyWoke_BindsTheBody()
    {
        var morning = Noon.AddHours(-5);
        var s = Lived(morning.AddHours(-9), rested: 0.3);
        s.Energy = "low";
        s.DayKey = "2026-11-21";

        Bio.Rise(s, 8, morning);
        Assert.Equal(Bio.Cap("low"), s.Rested);

        var late = Lived(morning.AddHours(-9), rested: 0.3);
        Bio.Rise(late, 8, morning);
        Assert.Equal(1, late.Rested);
        late.Energy = "low";
        Bio.Morning(late, morning.AddMinutes(20));
        Assert.Equal(Bio.Cap("low"), late.Rested);

        var longUp = Lived(Noon, rested: 0.9, up: Noon.AddHours(-6));
        longUp.Energy = "low";
        Bio.Morning(longUp, Noon);
        Assert.Equal(0.9, longUp.Rested);
    }

    [Fact]
    public void AMissedRise_IsMadeUpForAfterADay_WithoutTouchingFed()
    {
        var s = Lived(Noon, fed: 0.9, rested: 0.05, up: Noon.AddHours(-27));

        var body = Bio.Settle(s, Noon.AddMinutes(5), away: false);

        Assert.Equal(Tiredness.Fresh, body.Tiredness);
        Assert.Equal(Ms(Noon.AddMinutes(5)), s.UpSince);
        Assert.Equal(0.9 - Bio.HungerPerHour / 12, body.Fed, 6);
        Assert.Equal(Hunger.None, body.Hunger);
    }

    [Fact]
    public void Energy_IsTheLowerOfHowTheyWokeAndHowTheDayHasGone()
    {
        Assert.Equal("low", Bio.Energy("low", Bio.Now(Lived(Noon, rested: 0.9), Noon)));
        Assert.Equal("medium", Bio.Energy("medium", Bio.Now(Lived(Noon, rested: 0.9), Noon)));
        Assert.Equal("high", Bio.Energy("high", Bio.Now(Lived(Noon, rested: 0.9), Noon)));
        Assert.Equal("medium", Bio.Energy("high", Bio.Now(Lived(Noon, rested: 0.5), Noon)));
        Assert.Equal("low", Bio.Energy("high", Bio.Now(Lived(Noon, rested: 0.3), Noon)));
        Assert.Equal("none left", Bio.Energy("high", Bio.Now(Lived(Noon, rested: 0.1), Noon)));
    }

    [Fact]
    public void Walking_Costs_ALieDownHelpsOnce_AndCoffeeOnlyBorrows()
    {
        var s = Lived(Noon, rested: 0.3);

        Bio.Exert(s, Bio.Tram, Noon);
        Assert.Equal(0.28, s.Rested, 6);

        Assert.True(Bio.LieDown(s, Noon));
        Assert.Equal(0.58, s.Rested, 6);
        Assert.True(Bio.Now(s, Noon).Napped);
        Assert.False(Bio.LieDown(s, Noon.AddHours(1)));

        Bio.PerkUp(s, Noon);
        var perked = Bio.Now(s, Noon.AddMinutes(1));
        Assert.True(perked.Perked);
        Assert.True(perked.Rested > s.Rested);
        var worn = Bio.Now(s, Noon.AddHours(2).AddMinutes(1));
        Assert.False(worn.Perked);

        Bio.Rise(s, 8, Noon.AddHours(20));
        Assert.False(Bio.Now(s, Noon.AddHours(20)).Napped);
    }

    [Fact]
    public void ACoffee_DoesNotLiftABodyThatIsSpent()
    {
        var s = Lived(Noon, rested: 0.1);

        Bio.PerkUp(s, Noon);
        var after = Bio.Now(s, Noon.AddMinutes(1));

        Assert.False(after.Perked);
        Assert.Equal((0.1, Tiredness.Spent), (after.Rested, after.Tiredness));
    }

    [Fact]
    public void ALieDownOrACoffee_IsForTheDay_UnlessTheyAreSpent()
    {
        DateTimeOffset At(int hour) => new(2026, 11, 21, hour, 0, 0, TimeSpan.FromHours(1));

        Reserves Felt(double rested) => new(1, rested, 0, 0, false, true, false);

        Assert.True(Bio.Helps(Felt(0.3), At(14)));
        Assert.False(Bio.Helps(Felt(0.3), At(21)));
        Assert.False(Bio.Helps(Felt(0.3), At(2)));
        Assert.True(Bio.Helps(Felt(0.1), At(21)));
        Assert.False(Bio.Helps(Felt(0.5), At(14)));
    }

    [Fact]
    public void Distress_IsNamed_AndOnlyReliefNarrowsTheMenu()
    {
        var famished = Bio.Now(Lived(Noon, fed: 0.1), Noon);
        var spent = Bio.Now(Lived(Noon, rested: 0.1), Noon);
        var hungry = Bio.Now(Lived(Noon, fed: 0.4), Noon);

        Assert.Equal(Bio.Food, Bio.Distress(famished));
        Assert.Equal(Bio.Rest, Bio.Distress(spent));
        Assert.Null(Bio.Distress(hungry));
        Assert.Null(Bio.Distress(Bio.Now(Lived(Noon, fed: 0.1), Noon + Bio.FeltFor + TimeSpan.FromMinutes(1))));
        Assert.Equal(Bio.Narrowed, Bio.Seats(10, relief: true));
        Assert.Equal(10, Bio.Seats(10, relief: false));
        Assert.Equal(4, Bio.Seats(4, relief: true));
    }

    [Fact]
    public void Pull_GrowsWithTheNeed_AndAFullStomachSeesNoFood()
    {
        Assert.Equal(0, Bio.Pull(Hunger.None));
        Assert.Equal(0, Bio.Pull(Hunger.Peckish));
        Assert.Equal(2, Bio.Pull(Hunger.Hungry));
        Assert.Equal(3, Bio.Pull(Hunger.VeryHungry));
        Assert.Equal(Bio.Seated, Bio.Pull(Hunger.Famished));
        Assert.Equal(0, Bio.Pull(Tiredness.Fine));
        Assert.Equal(1, Bio.Pull(Tiredness.Flagging));
        Assert.Equal(Bio.Seated, Bio.Pull(Tiredness.Spent));
    }

    [Fact]
    public void Lines_SayItAsFact_AndOnlyOnceItHasLeftTheMiddle()
    {
        var up = Noon.AddHours(-5);
        Assert.Empty(Bio.Lines(Bio.Now(Lived(Noon, fed: 0.55, rested: 0.3, up: up), Noon), Noon));

        var hungry = Bio.Lines(Bio.Now(Lived(Noon, fed: 0.4, ate: Noon.AddHours(-4), up: up), Noon), Noon);
        Assert.Equal("You're hungry: nothing since 08:00.", Assert.Single(hungry));

        var really = Bio.Lines(Bio.Now(Lived(Noon, fed: 0.2, ate: Noon.AddHours(-30), up: Noon.AddHours(-40)), Noon), Noon);
        Assert.Equal("You're really hungry: nothing since yesterday.", Assert.Single(really));

        var worst = Bio.Lines(Bio.Now(Lived(Noon, fed: 0.05, rested: 0.05, up: up), Noon), Noon);
        Assert.Equal(2, worst.Count);
        Assert.StartsWith("You're famished: it's been a good while since you ate. It's hard to keep your mind on anything else", worst[0]);
        Assert.StartsWith("You're dead on your feet, up since 07:00.", worst[1]);
    }
}
