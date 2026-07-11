using PartyTown.Services.Memory;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

/// <summary>
/// Tests for <see cref="RecallDeployment"/> — the salience-floor deployment default that
/// gives small decision models a fighting chance at closing the memory loop — and for
/// <see cref="MemoryExtractor.HasUnattributedFirstPerson"/>, the perspective-fidelity
/// guard on recollection drafts.
///
/// The floor's contract (each clause has a test):
///   • an explicit model pick always wins, floor never overrides
///   • floor fires only when responding — silence deploys nothing and burns no one-shot
///   • floor requires Relevant arm + salience ≥ 0.7 + RecallCount == 0
///   • RecallCount == 0 makes deployment one-shot: a strengthened memory never re-fires
/// </summary>
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

    // ── Model pick precedence ────────────────────────────────────────────────

    [Fact]
    public void ModelPick_Wins_EvenWhenFloorWouldChooseDifferently()
    {
        var low = Memory(salience: 0.4, snippet: "low");
        var high = Memory(salience: 0.9, snippet: "high");
        var recalled = new[] { high, low };

        var (memory, auto) = RecallDeployment.ResolvePick(modelPick: 2, responding: true, recalled);

        Assert.Same(low, memory);
        Assert.False(auto);
    }

    // ── Floor deployment ─────────────────────────────────────────────────────

    [Fact]
    public void NullPick_Responding_DeploysQualifyingMemory()
    {
        // The exact bench failure: memory surfaced at 0.8 via the relevant arm, decision
        // returned null. The floor deploys it.
        var recalled = new[] { Memory(salience: 0.8) };

        var (memory, auto) = RecallDeployment.ResolvePick(modelPick: null, responding: true, recalled);

        Assert.Same(recalled[0], memory);
        Assert.True(auto);
    }

    [Fact]
    public void NullPick_NotResponding_DeploysNothing()
    {
        // A persona that stays silent deploys nothing — and keeps its one-shot for the
        // beat where it actually speaks.
        var recalled = new[] { Memory(salience: 0.9) };

        var (memory, auto) = RecallDeployment.ResolvePick(modelPick: null, responding: false, recalled);

        Assert.Null(memory);
        Assert.False(auto);
    }

    [Fact]
    public void NullPick_BelowFloor_DeploysNothing()
    {
        // 0.4-0.6 is "would resurface when the topic comes up" — the model's judgment
        // call, not the floor's. Only "they'd bring it up on their own" auto-deploys.
        var recalled = new[] { Memory(salience: 0.69) };

        var (memory, _) = RecallDeployment.ResolvePick(modelPick: null, responding: true, recalled);

        Assert.Null(memory);
    }

    [Fact]
    public void NullPick_RecentArm_DeploysNothing()
    {
        // Recency-arm memories aren't anchored to who's present / what's named — a
        // high-weight but off-topic memory must not hijack the beat.
        var recalled = new[] { Memory(salience: 0.9, arm: RecallArm.Recent) };

        var (memory, _) = RecallDeployment.ResolvePick(modelPick: null, responding: true, recalled);

        Assert.Null(memory);
    }

    [Fact]
    public void NullPick_AlreadyDeployedOnce_NeverRefires()
    {
        // One-shot: the strengthening write increments recall_count on every pick, so a
        // floor-deployed memory can't loop into every subsequent beat.
        var recalled = new[] { Memory(salience: 0.9, recallCount: 1) };

        var (memory, _) = RecallDeployment.ResolvePick(modelPick: null, responding: true, recalled);

        Assert.Null(memory);
    }

    [Fact]
    public void NullPick_PicksFirstQualifier_ListIsSalienceRanked()
    {
        // recalled arrives salience-ranked (RecallAsync contract); the first qualifier is
        // the strongest. A disqualified stronger entry (already deployed) is skipped.
        var spent = Memory(salience: 0.95, recallCount: 2, snippet: "spent");
        var fresh = Memory(salience: 0.8, snippet: "fresh");
        var recalled = new[] { spent, fresh };

        var (memory, auto) = RecallDeployment.ResolvePick(modelPick: null, responding: true, recalled);

        Assert.Same(fresh, memory);
        Assert.True(auto);
    }

    [Fact]
    public void NullPick_EmptyRecall_DeploysNothing()
    {
        var (memory, auto) = RecallDeployment.ResolvePick(
            modelPick: null, responding: true, Array.Empty<RecalledMemory>());

        Assert.Null(memory);
        Assert.False(auto);
    }

    // ── Perspective-fidelity guard (RecollectionFidelity) ────────────────────

    [Theory]
    // The exact corruption stored in the bench run — speaker framed as hearer:
    [InlineData("You heard me announce I'm quitting", true)]
    // Pre-fix agency inversion — orphaned "me":
    [InlineData("You offered me coffee on Thursday at the Blue Mug", true)]
    [InlineData("You never imagined how free I will feel with Rise & Grind", true)]
    // Curly-apostrophe contraction still caught (bare "I" before the apostrophe):
    [InlineData("You watched me smile as I’m signing the lease", true)]
    // Clean observer/speaker framings pass:
    [InlineData("Denise invited you to coffee at the Blue Mug on Thursday.", false)]
    [InlineData("You witnessed Denise announce her bakery launch", false)]
    [InlineData("You announced quitting the agency to open Rise & Grind.", false)]
    // First person inside quoted speech is attributable — allowed:
    [InlineData("You heard Denise say \"I quit the agency\" on the phone.", false)]
    [InlineData("You heard Denise say “I’m opening a bakery” with glee.", false)]
    // Group first person can include the rememberer — allowed:
    [InlineData("Denise invited us all to the opening.", false)]
    public void HasUnattributedFirstPerson_FlagsCorruptedPerspective(string snippet, bool expected)
        => Assert.Equal(expected, RecollectionFidelity.HasUnattributedFirstPerson(snippet));

    // ── Sanitize: corrupted draft → event-description fallback ──────────────

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
        // The model's weight judgment wasn't the corrupted part — the moment stays as
        // salient as the model said, only the snippet is replaced by the (name-correct)
        // neutral description.
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

    // ── Near-duplicate detection (capture-time dedup of substituted snippets) ──

    // The exact pair Denise banked in the post-fix bench run (20260710-221340): both her
    // drafts corrupted across two captures of the same development, so she got two
    // substituted Event descriptions that say the same thing in different words. They sit
    // on DIFFERENT Events, so identity can't dedup them — text similarity must.
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
        // Later news about the same bakery is a NEW memory — must never be swallowed.
        => Assert.False(RecollectionFidelity.IsNearDuplicate(
            "You remember: Denise's bakery Rise & Grind failed its health inspection.",
            BenchDupB));

    [Fact]
    public void IsNearDuplicate_SharedCastAndTopic_ShortSnippets_NotDuplicate()
        // Short snippets share most of their few content words just by naming the same
        // people — the min-shared-words floor keeps them apart.
        => Assert.False(RecollectionFidelity.IsNearDuplicate(
            "Denise told Vlad about her cat.",
            "Denise asked Vlad to watch her cat on Friday."));

    [Fact]
    public void IsNearDuplicate_DistinctMomentInSameArc_NotDuplicate()
        // The coffee invite is its own moment even though it belongs to the same story.
        => Assert.False(RecollectionFidelity.IsNearDuplicate(
            "You were invited for coffee Thursday at the Blue Mug",
            BenchDupB));

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "You remember: something happened.")]
    public void IsNearDuplicate_BlankInput_NotDuplicate(string a, string b)
        => Assert.False(RecollectionFidelity.IsNearDuplicate(a, b));
}
