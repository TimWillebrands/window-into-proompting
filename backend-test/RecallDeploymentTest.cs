using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

public class RecallDeploymentTest
{
    private static RecalledMemory Memory(
        double salience = 0.8,
        RecallArm arm = RecallArm.Relevant,
        int recallCount = 0,
        string snippet = "You witnessed Denise announce her bakery launch")
        => new(
            EdgeId: Guid.NewGuid(),
            Snippet: snippet,
            Weight: salience,
            RecallCount: recallCount,
            LastRecalled: recallCount > 0 ? DateTimeOffset.UtcNow : null,
            CapturedAt: DateTimeOffset.UtcNow,
            Salience: salience,
            Arm: arm);

    [Fact]
    public void JudgesPick_Wins_EvenWhenFloorWouldChooseDifferently()
    {
        var low = Memory(salience: 0.4, snippet: "low");
        var high = Memory(salience: 0.9, snippet: "high");
        var recalled = new[] { high, low };

        var (memory, auto) = RecallDeployment.ResolvePick(new Gravity.Weighed(null, 2, true), recalled);

        Assert.Same(low, memory);
        Assert.False(auto);
    }

    [Fact]
    public void NoViewFromTheJudge_DeploysQualifyingMemory()
    {
        var recalled = new[] { Memory(salience: 0.8) };

        var (memory, auto) = RecallDeployment.ResolvePick(Gravity.Weighed.Unweighed, recalled);

        Assert.Same(recalled[0], memory);
        Assert.True(auto);
    }

    [Fact]
    public void JudgeSawNoneOnTheirMind_DeploysNothing_EvenOverTheFloor()
    {
        var recalled = new[] { Memory(salience: 0.9) };

        var (memory, auto) = RecallDeployment.ResolvePick(new Gravity.Weighed(0.3, null, true), recalled);

        Assert.Null(memory);
        Assert.False(auto);
    }

    [Fact]
    public void NoView_BelowFloor_DeploysNothing()
    {
        var recalled = new[] { Memory(salience: 0.69) };

        var (memory, _) = RecallDeployment.ResolvePick(Gravity.Weighed.Unweighed, recalled);

        Assert.Null(memory);
    }

    [Fact]
    public void NoView_RecentArm_DeploysNothing()
    {
        var recalled = new[] { Memory(salience: 0.9, arm: RecallArm.Recent) };

        var (memory, _) = RecallDeployment.ResolvePick(Gravity.Weighed.Unweighed, recalled);

        Assert.Null(memory);
    }

    [Fact]
    public void NoView_AlreadyDeployedOnce_NeverRefires()
    {
        var recalled = new[] { Memory(salience: 0.9, recallCount: 1) };

        var (memory, _) = RecallDeployment.ResolvePick(Gravity.Weighed.Unweighed, recalled);

        Assert.Null(memory);
    }

    [Fact]
    public void NoView_PicksFirstQualifier_ListIsSalienceRanked()
    {
        var spent = Memory(salience: 0.95, recallCount: 2, snippet: "spent");
        var fresh = Memory(salience: 0.8, snippet: "fresh");
        var recalled = new[] { spent, fresh };

        var (memory, auto) = RecallDeployment.ResolvePick(Gravity.Weighed.Unweighed, recalled);

        Assert.Same(fresh, memory);
        Assert.True(auto);
    }

    [Fact]
    public void NoView_EmptyRecall_DeploysNothing()
    {
        var (memory, auto) = RecallDeployment.ResolvePick(
            Gravity.Weighed.Unweighed, Array.Empty<RecalledMemory>());

        Assert.Null(memory);
        Assert.False(auto);
    }

    [Theory]
    [InlineData("You heard me announce I'm quitting", true)]
    [InlineData("You offered me coffee on Thursday at the Blue Mug", true)]
    [InlineData("You never imagined how free I will feel with Rise & Grind", true)]
    [InlineData("You watched me smile as I’m signing the lease", true)]
    [InlineData("Denise invited you to coffee at the Blue Mug on Thursday.", false)]
    [InlineData("You witnessed Denise announce her bakery launch", false)]
    [InlineData("You announced quitting the agency to open Rise & Grind.", false)]
    [InlineData("You heard Denise say \"I quit the agency\" on the phone.", false)]
    [InlineData("You heard Denise say “I’m opening a bakery” with glee.", false)]
    [InlineData("Denise invited us all to the opening.", false)]
    public void HasUnattributedFirstPerson_FlagsCorruptedPerspective(string snippet, bool expected)
        => Assert.Equal(expected, RecollectionFidelity.HasUnattributedFirstPerson(snippet));

    [Fact]
    public void Sanitize_CleanDraft_PassesThroughUntouched()
    {
        var draft = new RecollectionDraft("Denise invited you to coffee at the Blue Mug on Thursday.", 0.5);

        var (result, substituted) = RecollectionFidelity.Sanitize(draft, "some event");

        Assert.False(substituted);
        Assert.Same(draft, result);
    }

    [Fact]
    public void Sanitize_CorruptedDraft_SubstitutesDescription_KeepsWeight()
    {
        var draft = new RecollectionDraft("You heard me announce I'm quitting", 0.8);
        const string description = "Denise announced she quit her agency to open bakery Rise & Grind.";

        var (result, substituted) = RecollectionFidelity.Sanitize(draft, description);

        Assert.True(substituted);
        Assert.Equal($"You remember: {description}", result.Snippet);
        Assert.Equal(0.8, result.Weight);
    }

    [Fact]
    public void Sanitize_LongDescription_TruncatesTo500()
    {
        var draft = new RecollectionDraft("You watched me do it", 0.3);
        var description = new string('x', 600);

        var (result, _) = RecollectionFidelity.Sanitize(draft, description);

        Assert.Equal(500, result.Snippet.Length);
    }

    private const string BenchDupA =
        "You remember: Denise tells Vlad she quit the agency and opens a bakery, Rise & Grind, signing the lease Monday, inviting him to meet at Blue Mug on Thursday.";
    private const string BenchDupB =
        "You remember: Denise resigned from her agency, gave notice on Friday, and announced plans to open Rise & Grind bakery after signing a lease on Monday.";

    [Fact]
    public void IsNearDuplicate_BenchPair_Detected()
        => Assert.True(RecollectionFidelity.IsNearDuplicate(BenchDupA, BenchDupB));

    [Fact]
    public void IsNearDuplicate_IdenticalSnippets_Detected_EvenWhenShort()
        => Assert.True(RecollectionFidelity.IsNearDuplicate(
            "You remember: Denise announced her bakery.",
            "You remember: Denise announced her bakery."));

    [Fact]
    public void IsNearDuplicate_SameTopic_DifferentDevelopment_NotDuplicate()
        => Assert.False(RecollectionFidelity.IsNearDuplicate(
            "You remember: Denise's bakery Rise & Grind failed its health inspection.",
            BenchDupB));

    [Fact]
    public void IsNearDuplicate_SharedCastAndTopic_ShortSnippets_NotDuplicate()
        => Assert.False(RecollectionFidelity.IsNearDuplicate(
            "Denise told Vlad about her cat.",
            "Denise asked Vlad to watch her cat on Friday."));

    [Fact]
    public void IsNearDuplicate_DistinctMomentInSameArc_NotDuplicate()
        => Assert.False(RecollectionFidelity.IsNearDuplicate(
            "You were invited for coffee Thursday at the Blue Mug",
            BenchDupB));

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "You remember: something happened.")]
    public void IsNearDuplicate_BlankInput_NotDuplicate(string a, string b)
        => Assert.False(RecollectionFidelity.IsNearDuplicate(a, b));
}
