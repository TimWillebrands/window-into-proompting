using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Work.Cdu;
using PartyTown.World.Work.Companies;

namespace BackendTest;

/// <summary>Reaching past the arena (ADR 0047): felt rarely, voiced plainly, and on the Cytheurgists' desk.</summary>
public class LackTest
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    /// <summary>A steady judge: usual 0.35, spread 0.04, so a jump is 0.10 and up.</summary>
    private static readonly Lack.Baseline Usual = new(0.35, 8, 0.0016);

    [Theory]
    [InlineData(0.50, null, null, true)]
    [InlineData(0.42, null, null, false)]
    [InlineData(0.50, 1, null, false)]
    [InlineData(0.50, 7, null, true)]
    [InlineData(0.50, null, 30, false)]
    [InlineData(0.50, null, 90, true)]
    public void Felt_ASpikeAboveTheirUsual_NotLately(double p, int? voicedHoursAgo, int? askedMinutesAgo, bool felt)
        => Assert.Equal(felt, Lack.Felt(p, Usual,
            voicedHoursAgo is { } h ? Now.AddHours(-h).ToUnixTimeMilliseconds() : null, Now,
            askedMinutesAgo is { } m ? Now.AddMinutes(-m).ToUnixTimeMilliseconds() : null));

    [Fact]
    public void Felt_NeverBeforeTheirUsualIsKnown()
        => Assert.False(Lack.Felt(0.9, new Lack.Baseline(0.3, Lack.Settled - 1, 0.0016), null, Now));

    [Fact]
    public void Felt_AHighUsualIsNoLack()
        => Assert.False(Lack.Felt(0.6, new Lack.Baseline(0.55, 10, 0.0016), null, Now));

    /// <summary>Production, 10 Nov: Jev sat at 0.17 ± 0.04 and never reached the old 0.4 floor; d1 sat at 0.38 ± 0.055.</summary>
    [Theory]
    [InlineData(0.17, 0.037, 0.28, true)]
    [InlineData(0.17, 0.037, 0.24, false)]
    [InlineData(0.38, 0.055, 0.53, true)]
    [InlineData(0.38, 0.055, 0.45, false)]
    public void Felt_IsReadAgainstEachJudgesOwnSpread(double mean, double spread, double p, bool felt)
        => Assert.Equal(felt, Lack.Felt(p, new Lack.Baseline(mean, 20, spread * spread), null, Now));

    [Fact]
    public void Jump_HasAFloor_SoAFlatJudgeMakesNoLackOfNoise()
    {
        Assert.Equal(Lack.Surprise, Lack.Jump(new Lack.Baseline(0.2, 20, 0.0001)));
        Assert.False(Lack.Felt(0.24, new Lack.Baseline(0.2, 20, 0.0001), null, Now));
    }

    [Fact]
    public void Baseline_FollowsWhatTheyFeel()
    {
        var b = new Lack.Baseline().After(0.3);
        Assert.Equal(0.3, b.Mean, 3);
        Assert.Equal(0, b.Spread, 3);
        Assert.Equal(0.34, b.After(0.5).Mean, 3);
        Assert.Equal(0.08, b.After(0.5).Spread, 3);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("none", null)]
    [InlineData("None, the walk was it.", null)]
    [InlineData("\"send Mara the photo of the boat\"", "send Mara the photo of the boat")]
    public void Read_NoneIsNothing(string? said, string? read) => Assert.Equal(read, Lack.Read(said));

    [Fact]
    public void Of_OnePerPerson_NewestFirst()
    {
        var mara = Guid.NewGuid();
        var acts = new[]
        {
            new WorldActivity { PersonaId = mara, PersonaName = "Mara", Action = Lack.Action, Summary = "old", Ts = Now.AddHours(-3).ToUnixTimeMilliseconds() },
            new WorldActivity { PersonaId = mara, PersonaName = "Mara", Action = Lack.Action, Summary = "sell a painting", Ts = Now.ToUnixTimeMilliseconds() },
            new WorldActivity { PersonaId = Guid.NewGuid(), PersonaName = "Dan", Action = "post", Summary = "hi", Ts = Now.ToUnixTimeMilliseconds() },
        };
        Assert.Equal(new[] { ("Mara", "sell a painting") }, Lack.Of(acts));
    }

    [Fact]
    public void Desk_ShowsWhatPeopleFoundNoWayTo()
    {
        var shop = new Company { Id = Guid.NewGuid(), Name = "Vormwerk", Mandate = "keeps the Forms", Kind = CompanyKinds.Cdu };
        var acts = new[] { new WorldActivity { PersonaId = Guid.NewGuid(), PersonaName = "Mara", App = "life", Action = Lack.Action, Summary = "sell a painting to Pieter", Ts = Now.ToUnixTimeMilliseconds() } };
        var text = CduScreen.Render(new CduDesk(shop, new CduState(), null, CduScreen.Health(acts, Now), Guid.NewGuid()));
        Assert.Contains("found no way to here:\n- Mara: sell a painting to Pieter", text);
    }
}
