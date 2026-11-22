using PartyTown.World.Self;

namespace BackendTest;

public class BreakingDownTest
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.FromHours(1));

    private static long Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    private static SelfState Lived(DateTimeOffset at, double fed = 1, double rested = 1, double strain = 0) => new()
    {
        Fed = fed, Rested = rested, BodyAt = Ms(at), LivedAt = Ms(at), UpSince = Ms(at.AddHours(-4)), Strain = strain,
    };

    [Fact]
    public void Strain_GrowsOnlyInDistress_AndOnlyWithLivedTime()
    {
        var famished = Lived(Noon, fed: 0.1, rested: 0.9);
        Bio.Settle(famished, Noon.AddMinutes(30), away: false);
        Assert.Equal(0.5 * Bio.StrainPerHour, famished.Strain, 6);

        var spent = Lived(Noon, fed: 0.9, rested: 0.1);
        Bio.Settle(spent, Noon.AddHours(10), away: false);
        Assert.Equal(1.25 * Bio.StrainPerHour, spent.Strain, 6);

        var hungry = Lived(Noon, fed: 0.4, rested: 0.3);
        Bio.Settle(hungry, Noon.AddMinutes(30), away: false);
        Assert.Equal(0, hungry.Strain);

        var unlived = Lived(Noon, fed: 0.1, rested: 0.1, strain: 0.3);
        Bio.Now(unlived, Noon.AddHours(5));
        Assert.Equal(0.3, unlived.Strain);
    }

    [Fact]
    public void Strain_Eases_WhileTheyAreLookedAfter_ButNotWhileAiling()
    {
        var fine = Lived(Noon, fed: 0.9, rested: 0.9, strain: 0.4);
        Bio.Settle(fine, Noon.AddMinutes(30), away: false);
        Bio.Settle(fine, Noon.AddMinutes(60), away: false);
        Assert.Equal(0.4 - Bio.StrainEasesPerHour, fine.Strain, 6);

        var ailing = Lived(Noon, fed: 0.9, rested: 0.9, strain: 0.4);
        Bio.Ail(ailing, Noon);
        Bio.Settle(ailing, Noon.AddMinutes(60), away: false);
        Assert.Equal(Bio.AfterAiling, ailing.Strain, 6);

        var none = Lived(Noon, fed: 0.9, rested: 0.9);
        Bio.Settle(none, Noon.AddMinutes(60), away: false);
        Assert.Equal(0, none.Strain);
    }

    [Fact]
    public void AShortNight_AddsStrain_AndAFullNightHalvesIt()
    {
        var morning = Noon.AddHours(19);

        var shortNight = Lived(Noon.AddHours(10), strain: 0.1);
        Bio.Rise(shortNight, 4, morning);
        Assert.Equal(0.1 + 2 * Bio.StrainPerHourShort, shortNight.Strain, 6);

        var ordinary = Lived(Noon.AddHours(10), strain: 0.4);
        Bio.Rise(ordinary, 6.5, morning);
        Assert.Equal(0.4, ordinary.Strain, 6);

        var full = Lived(Noon.AddHours(10), strain: 0.4);
        Bio.Rise(full, 8, morning);
        Assert.Equal(0.2, full.Strain, 6);
    }

    [Fact]
    public void NightsInAWorldNobodyLivesIn_LeaveStrainAlone()
    {
        var s = Lived(Noon, strain: 0.4);

        for (var day = 2; day <= 5; day++) Bio.Rise(s, day % 2 == 0 ? 3 : 9, Noon.AddDays(day).AddHours(-5));

        Assert.Equal(0.4, s.Strain);
    }

    [Fact]
    public void Breaking_NeedsStrain_AndThreeDaysSinceTheyLastGotWell()
    {
        Assert.True(Bio.Breaking(Lived(Noon, strain: Bio.Breaks), Noon));
        Assert.False(Bio.Breaking(Lived(Noon, strain: 0.49), Noon));
        Assert.False(Bio.Breaking(new SelfState { Strain = 0.9 }, Noon));

        var recent = Lived(Noon, strain: 0.8);
        recent.AiledAt = Ms(Noon.AddDays(-2));
        Assert.False(Bio.Breaking(recent, Noon));
        Assert.True(Bio.Breaking(recent, Noon.AddDays(1)));

        var ailing = Lived(Noon, strain: 0.8);
        Bio.Ail(ailing, Noon);
        ailing.Strain = 0.8;
        Assert.False(Bio.Breaking(ailing, Noon.AddHours(1)));
    }

    [Fact]
    public void Ailing_DropsStrain_AndCapsHowRestedTheyFeel()
    {
        var s = Lived(Noon, fed: 0.9, rested: 0.9, strain: 0.7);

        Assert.True(Bio.Ail(s, Noon));
        Assert.False(Bio.Ail(s, Noon.AddMinutes(5)));

        var body = Bio.Now(s, Noon.AddMinutes(5));
        Assert.Equal((Ms(Noon), Bio.AfterAiling, 0.9), (s.AilingSince, s.Strain, s.Rested));
        Assert.True(body.Ailing);
        Assert.Equal(Bio.AilingRested, body.Rested);
        Assert.Equal(Tiredness.Flagging, body.Tiredness);
        Assert.Equal("low", Bio.Energy("high", body));
        Assert.Null(Bio.Distress(body));

        Bio.PerkUp(s, Noon);
        Assert.Equal(Bio.AilingRested, Bio.Now(s, Noon.AddMinutes(5)).Rested);
    }

    [Fact]
    public void Ailing_TiresThemHalfAgainAsFast()
    {
        var well = Lived(Noon, rested: 0.9);
        var ill = Lived(Noon, rested: 0.9);
        Bio.Ail(ill, Noon);

        Bio.Settle(well, Noon.AddHours(1), away: false);
        Bio.Settle(ill, Noon.AddHours(1), away: false);

        Assert.Equal(0.9 - Bio.TiringPerHour, well.Rested, 6);
        Assert.Equal(0.9 - Bio.TiringPerHour * Bio.AilingTires, ill.Rested, 6);
    }

    [Fact]
    public void GettingWell_TakesAFullNight_AndTwelveHours()
    {
        var s = Lived(Noon);
        Bio.Ail(s, Noon);

        Bio.Rise(s, 8, Noon.AddHours(6));
        Assert.NotEqual(0, s.AilingSince);

        s.LivedAt = Ms(Noon.AddHours(10));
        Bio.Rise(s, 5, Noon.AddHours(19));
        Assert.NotEqual(0, s.AilingSince);

        Bio.Rise(s, 8, Noon.AddHours(19));
        Assert.Equal((0L, Ms(Noon.AddHours(19))), (s.AilingSince, s.AiledAt));
        Assert.False(Bio.Now(s, Noon.AddHours(19)).Ailing);
    }

    [Fact]
    public void AnAilment_EndsByItselfAfterThreeDays()
    {
        var s = Lived(Noon);
        s.AilingSince = Ms(Noon - Bio.AilsAtMost - TimeSpan.FromMinutes(1));

        Assert.False(Bio.Now(s, Noon).Ailing);

        Bio.Settle(s, Noon.AddMinutes(10), away: false);

        Assert.Equal((0L, Ms(Noon.AddMinutes(10))), (s.AilingSince, s.AiledAt));
        s.Strain = 0.9;
        Assert.False(Bio.Breaking(s, Noon.AddDays(1)));
        Assert.True(Bio.Breaking(s, Noon.AddMinutes(10) + Bio.Mended));
    }

    [Fact]
    public void Lines_SayItPlainly_WhileTheyAreNotWell()
    {
        var s = Lived(Noon, fed: 0.9, rested: 0.9);
        Assert.Empty(Bio.Lines(Bio.Now(s, Noon), Noon));

        Bio.Ail(s, Noon);
        var lines = Bio.Lines(Bio.Now(s, Noon), Noon);

        Assert.Equal("You're not well today: everything takes more out of you.", Assert.Single(lines));
    }

    [Fact]
    public void ALieDownOrACoffee_StandsOutAtAnyHour_WhileAiling()
    {
        var s = Lived(Noon, rested: 0.9);
        Bio.Ail(s, Noon);
        var night = new DateTimeOffset(2026, 11, 21, 22, 0, 0, TimeSpan.FromHours(1));

        Assert.True(Bio.Helps(Bio.Now(s, Noon), night));
        Assert.False(Bio.Helps(Bio.Now(Lived(Noon, rested: 0.3), Noon), night));
        Assert.False(Bio.Helps(Bio.Now(s, Noon + Bio.FeltFor + TimeSpan.FromMinutes(1)), night));
    }

    [Fact]
    public void ABodyNobodyLivedIn_NeitherBreaksNorAils()
    {
        var never = new SelfState { Strain = 0.9 };

        Assert.False(Bio.Breaking(never, Noon));
        Assert.False(Bio.Ail(never, Noon));
        Assert.Null(Bio.Lately(never));
        Assert.Equal((0L, 0.9), (never.AilingSince, never.Strain));
        Assert.False(Bio.Now(never, Noon).Felt);

        var unfelt = Lived(Noon.AddHours(-5), rested: 0.9);
        Assert.True(Bio.Ail(unfelt, Noon));
        var body = Bio.Now(unfelt, Noon);
        Assert.False(body.Felt);
        Assert.Equal(Tiredness.Fresh, body.Tiredness);
        Assert.Empty(Bio.Lines(body, Noon));
        Assert.False(Bio.Helps(body, Noon));
        Assert.Equal(Ms(Noon.AddHours(-5)), unfelt.LivedAt);
    }

    [Fact]
    public void Lately_SaysHowTheyHaveTreatedTheirBody_OrNothing()
    {
        Assert.Null(Bio.Lately(Lived(Noon, strain: 0.1)));
        Assert.Equal("they've been wearing themselves thin", Bio.Lately(Lived(Noon, strain: 0.35)));
        Assert.Equal("they've been running themselves down, going hungry for hours and short on sleep",
            Bio.Lately(Lived(Noon, fed: 0.2, rested: 0.3, strain: 0.6)));
        Assert.Equal("they've been running themselves down, short on sleep", Bio.Lately(Lived(Noon, rested: 0.1, strain: 0.6)));
    }

    [Fact]
    public void OwnHappenings_GoFirstToABodyThatIsBreaking_AndAreAboutTheBody()
    {
        var now = Ms(Noon);
        var (never, breaking, resting) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var awake = new List<(Guid, long)>
        {
            (never, 0),
            (breaking, now - (long)TimeSpan.FromHours(8).TotalMilliseconds),
            (resting, now - (long)TimeSpan.FromHours(1).TotalMilliseconds),
        };
        var random = new Random(5);

        for (var i = 0; i < 20; i++) Assert.Equal(breaking, OwnHappenings.Pick(awake, now, random, new HashSet<Guid> { breaking, resting }));
        Assert.Equal(never, OwnHappenings.Pick(awake, now, random, new HashSet<Guid> { resting }));
        Assert.Equal(never, OwnHappenings.Pick(awake, now, random));
        Assert.Equal(new List<Guid> { never, breaking }, OwnHappenings.Due(awake, now));

        Assert.StartsWith("their body", OwnHappenings.Kinds[OwnHappenings.Body]);
        var lately = new List<int> { OwnHappenings.Body };
        Assert.Equal(OwnHappenings.Body, OwnHappenings.KindFor(true, lately, random));
        for (var i = 0; i < 20; i++) Assert.NotEqual(OwnHappenings.Body, OwnHappenings.KindFor(false, lately, random));
    }

    [Fact]
    public void InTheThreeDaysAfterGettingWell_TheyCannotAilAgain_AndNoBodyHappeningIsDrawnForThem()
    {
        var s = Lived(Noon);
        s.AiledAt = Ms(Noon.AddHours(-1));

        Assert.True(Bio.Mending(s, Ms(Noon)));
        Assert.False(Bio.Ail(s, Noon));
        Assert.Equal(0L, s.AilingSince);
        Assert.False(Bio.Mending(s, Ms(Noon.AddHours(-1) + Bio.Mended)));
        Assert.True(Bio.Ail(s, Noon.AddHours(-1) + Bio.Mended));

        var random = new Random(3);
        var none = new List<int>();
        var all = Enumerable.Range(0, OwnHappenings.Kinds.Length).ToList();
        for (var i = 0; i < 200; i++)
        {
            Assert.NotEqual(OwnHappenings.Body, OwnHappenings.KindFor(false, none, random, mending: true));
            Assert.NotEqual(OwnHappenings.Body, OwnHappenings.KindFor(false, all, random, mending: true));
        }
        Assert.Contains(OwnHappenings.Body, Enumerable.Range(0, 200).Select(_ => OwnHappenings.KindFor(false, none, random)));
    }

    [Fact]
    public void OwnHappenings_Material_CarriesHowTheyHaveTreatedTheirBody()
    {
        var day = Lived(Noon, fed: 0.2, strain: 0.6);

        var material = OwnHappenings.Material("Justin", "A notary.", day, Noon, Bio.Lately(day));

        Assert.Contains("How they've treated their body lately: they've been running themselves down, going hungry for hours.", material);
        Assert.DoesNotContain("their body lately", OwnHappenings.Material("Justin", "A notary.", day, Noon));
    }
}
