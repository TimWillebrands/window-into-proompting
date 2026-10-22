using PartyTown.World.Growth;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>ADR 0027 footfall: one Persona can't change the world; shared life over days can.</summary>
public class FootfallTest
{
    private static readonly Guid Office = Guid.NewGuid();
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static FootfallSignal S(Guid who, string source, int daysAgo)
        => new()
        {
            LocaleId = Office, ParticipantId = who, Source = source, Weight = FootfallSources.WeightOf(source),
            At = Now - (long)TimeSpan.FromDays(daysAgo).TotalMilliseconds, Day = $"d{daysAgo}",
        };

    [Fact]
    public void OnePersona_CantOpenAPlace_HoweverMuchTheyGoOn()
    {
        var ana = Guid.NewGuid();
        var signals = Enumerable.Range(0, 5).SelectMany(d => new[] { S(ana, "day", d), S(ana, "talk", d), S(ana, "anchor", d) }).ToList();

        var reading = Assert.Single(FootfallMath.Read(signals, Now));

        Assert.Equal(FootfallMath.PerParticipantCap, reading.Footfall);
        Assert.Equal("needs 1 more person", reading.Missing);
    }

    [Fact]
    public void TwoPeople_ThereAndTalkingAboutIt_ForThreeDays_Open()
    {
        var (ana, bo) = (Guid.NewGuid(), Guid.NewGuid());
        List<FootfallSignal> Days(int n) => Enumerable.Range(0, n)
            .SelectMany(d => new[] { S(ana, "day", d), S(ana, "talk", d), S(bo, "day", d), S(bo, "talk", d) }).ToList();

        Assert.Equal("needs 1 more day", FootfallMath.Read(Days(1), Now)[0].Missing);
        Assert.Equal("needs 0.5 more footfall", FootfallMath.Read(Days(2), Now)[0].Missing);
        var reading = FootfallMath.Read(Days(3), Now)[0];

        Assert.Null(reading.Missing);
        Assert.Equal(2, reading.People);
    }

    [Fact]
    public void WorkingThere_AndTheDaySayingSo_IsOnePresence()
    {
        var ana = Guid.NewGuid();
        var ledger = new List<FootfallSignal> { S(ana, "anchor", 0) };
        Assert.True(FootfallMath.Duplicate(ledger, S(ana, "day", 0)));
        Assert.False(FootfallMath.Duplicate(ledger, S(ana, "talk", 0)));
    }

    [Fact]
    public void Talk_FadesWithAThreeDayHalfLife()
    {
        var old = S(Guid.NewGuid(), "talk", 3);
        Assert.Equal(0.1, FootfallMath.Decayed(old, Now), 3);
        Assert.Empty(FootfallMath.Read(new[] { S(Guid.NewGuid(), "talk", 30) }, Now)[0].Contributors);
    }

    [Fact]
    public void TheSameSignal_OnTheSameDay_IsADuplicate()
    {
        var ana = Guid.NewGuid();
        var ledger = new List<FootfallSignal> { S(ana, "talk", 0) };
        Assert.True(FootfallMath.Duplicate(ledger, S(ana, "talk", 0)));
        Assert.False(FootfallMath.Duplicate(ledger, S(ana, "day", 0)));
        Assert.False(FootfallMath.Duplicate(ledger, S(ana, "talk", 1)));
        Assert.False(FootfallMath.Duplicate(ledger, S(Guid.NewGuid(), "talk", 0)));
    }

    [Theory]
    [InlineData("we should try the climbing gym sometime", "the climbing gym", true)]
    [InlineData("Climbing Gym? no thanks", "the climbing gym", true)]
    [InlineData("the climbing gyms are full", "the climbing gym", false)]
    [InlineData("see you at work", "Ana's work", false)]
    [InlineData("the bar", "the bar", false)]
    public void Mentions_AreWholeWords_AndNotForGenericPlaces(string content, string locale, bool expected)
        => Assert.Equal(expected, PlacesGrain.Mentioned(content, locale));
}
