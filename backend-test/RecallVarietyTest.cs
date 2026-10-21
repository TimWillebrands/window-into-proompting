using PartyTown.Services.Memory;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>One story per recall slot, and "you told X" memories say so (ADR 0021).</summary>
public class RecallVarietyTest
{
    private static readonly string[] Names = { "Lena Brandt", "Denise", "Pieter Voss", "Eiko Tanaka" };

    [Fact]
    public void Collapse_KeepsOneRetellingOfTheSameStory()
    {
        var items = new[]
        {
            "You told Denise you once stayed in empty rooms at your coldest address; someone who stays anyway is the only structure you trust.",
            "Pieter's VAT correction is still unopened in the tax folder.",
            "You told Denise to say it to your face if your name was around the booth; you stayed at your coldest address longer than sensible.",
            "Eiko measured the lid of the chipped tin before anyone else did.",
            "Lena called Eiko the one who stays, like the cold rooms, and asked for a slow walk to the tram.",
        };
        var kept = RecallVariety.Collapse(items, s => s, Names);
        Assert.Equal(new[] { items[0], items[1], items[3], items[4] }, kept);
    }

    [Fact]
    public void Collapse_KeepsDifferentStories()
    {
        var items = new[]
        {
            "Denise fixed the client's API docs in four places.",
            "Pieter still hasn't bought a gift for his aunt's birthday.",
            "Eiko's sake supplier put her on hold for eleven minutes.",
        };
        Assert.Equal(items, RecallVariety.Collapse(items, s => s, Names));
    }

    [Theory]
    [InlineData("You told Denise you once stayed in empty rooms.", "Denise")]
    [InlineData("Over coffee you told Pieter Voss and Eiko about the lease.", "Pieter Voss and Eiko")]
    [InlineData("Lena told you she once stayed in empty rooms.", null)]
    [InlineData("You told them nothing about the lease.", null)]
    public void Told_FindsWhoHeardIt(string snippet, string? who) => Assert.Equal(who, RecallVariety.Told(snippet));

    [Fact]
    public void Annotate_MarksStoriesYouAlreadyTold()
    {
        var memory = new RecalledMemory(Guid.NewGuid(), "You told Denise about the radiator.", 0.8, 0, null, DateTimeOffset.UtcNow, 0.8);
        Assert.Equal("You told Denise about the radiator. (Denise already heard this from you)", RecallVariety.Annotate(memory).Snippet);
    }
}
