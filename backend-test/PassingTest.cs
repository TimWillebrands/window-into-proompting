using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

public class PassingTest
{
    [Theory]
    [InlineData("(pass)")]
    [InlineData("(Pass)")]
    [InlineData("(PASS)")]
    [InlineData(" (pass). ")]
    [InlineData("(pass)!")]
    [InlineData("*(pass)*")]
    [InlineData("**(pass)**")]
    [InlineData("`(pass)`")]
    [InlineData("\"(pass)\"")]
    [InlineData("[pass]")]
    [InlineData("( pass )")]
    [InlineData("*shrugs* (pass)")]
    public void TheWholeLineIsTheMark_ItIsAPass(string text)
        => Assert.True(Passing.IsMark(text));

    [Theory]
    [InlineData("Sure (pass)")]
    [InlineData("(pass) Actually wait, I'm in!")]
    [InlineData("Math (passed), physics was the hard one.")]
    [InlineData("Passed!")]
    [InlineData("Pass.")]
    [InlineData("pass")]
    [InlineData("(passing)")]
    [InlineData("(passes)")]
    [InlineData("I'll pass on the coffee")]
    [InlineData("passport's expired")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnythingElse_IsALine(string text)
        => Assert.False(Passing.IsMark(text));

    [Theory]
    [InlineData("Sure (pass)", "Sure")]
    [InlineData("(pass) Actually wait, I'm in!", "Actually wait, I'm in!")]
    [InlineData("Sure (PASS), why not", "Sure, why not")]
    [InlineData("Math (passed), physics was the hard one.", "Math (passed), physics was the hard one.")]
    public void TheMarkAmongOtherWords_IsStripped_AndTheRestPublished(string raw, string published)
        => Assert.Equal(published, SpeechCleanup.Clean(raw, "Denise"));

    [Theory]
    [InlineData("", true)]
    [InlineData("(", true)]
    [InlineData("(pa", true)]
    [InlineData("*(PASS", true)]
    [InlineData("(pass)", true)]
    [InlineData("*shr", true)]
    [InlineData("*shrugs* (pa", true)]
    [InlineData("(pau", false)]
    [InlineData("ok", false)]
    [InlineData("(pass) Act", false)]
    [InlineData("pass", false)]
    public void MayBe_HoldsOnlyWhatCouldStillBecomeTheWholeMark(string prefix, bool held)
        => Assert.Equal(held, Passing.MayBe(prefix));

    private static (Passing.Gate Gate, List<string> Released) Gate()
    {
        var released = new List<string>();
        return (new Passing.Gate(c => { released.Add(c); return Task.CompletedTask; }), released);
    }

    [Fact]
    public async Task Gate_SwallowsTheWholeMark_EvenAfterAStageDirection()
    {
        var (gate, released) = Gate();
        foreach (var chunk in new[] { "*shr", "ugs* (Pa", "ss)." }) await gate.ChunkAsync(chunk);
        await gate.EndAsync();

        Assert.Empty(released);
    }

    [Fact]
    public async Task Gate_NeverShowsAMarkAmongWords()
    {
        var (gate, released) = Gate();
        foreach (var chunk in new[] { "Sure (pa", "ss) why", " not" }) await gate.ChunkAsync(chunk);
        await gate.EndAsync();

        var shown = string.Concat(released);
        Assert.DoesNotContain("(pass)", shown, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Sure  why not", shown);
    }

    [Fact]
    public async Task Gate_ALineThatOnlyLookedLikeTheMark_IsReleasedWhole()
    {
        var (gate, released) = Gate();
        foreach (var chunk in new[] { "(pa", "uses) ok", " fine" }) await gate.ChunkAsync(chunk);
        await gate.EndAsync();

        Assert.Equal("(pauses) ok fine", string.Concat(released));
    }

    [Fact]
    public async Task Gate_PassedInsideALine_IsALine()
    {
        var (gate, released) = Gate();
        foreach (var chunk in new[] { "Math (pass", "ed), physics" }) await gate.ChunkAsync(chunk);
        await gate.EndAsync();

        Assert.Equal("Math (passed), physics", string.Concat(released));
    }

    [Fact]
    public async Task Gate_AShortLineStillHeldAtTheEnd_IsReleased()
    {
        var (gate, released) = Gate();
        await gate.ChunkAsync("...");
        await gate.EndAsync();

        Assert.Equal(new[] { "..." }, released);
    }

    [Theory]
    [InlineData(false, false, false, false, false, false, true)]
    [InlineData(false, false, true, false, false, false, false)]
    [InlineData(false, false, true, false, true, false, true)]
    [InlineData(false, false, true, false, false, true, true)]
    [InlineData(false, true, false, false, false, false, false)]
    [InlineData(false, true, false, false, true, false, true)]
    [InlineData(false, false, false, true, false, false, true)]
    [InlineData(false, false, true, true, true, true, false)]
    [InlineData(false, true, false, true, true, true, false)]
    [InlineData(true, false, false, false, true, true, false)]
    public void Allowed_WhereTheyMayLetItGo(bool owed, bool direct, bool named, bool human, bool torn, bool windingDown, bool allowed)
        => Assert.Equal(allowed, Passing.Allowed(owed, direct, named, human, torn, windingDown));
}
