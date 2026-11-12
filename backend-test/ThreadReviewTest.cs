using Moq;
using PartyTown.Services.SystemOne;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>What the World says closes a Life thread (ADR 0052): read at rest against what they remember, gone by the next Day.</summary>
public class ThreadReviewTest
{
    private static readonly DateTimeOffset Tomorrow = new(2026, 11, 12, 7, 0, 0, TimeSpan.Zero);
    private static readonly Guid Denise = Guid.NewGuid();
    private const string Staging = "Finish the release schedule review so the staging numbers are ready";
    private const string Bike = "Replace the rear light on her bike";
    private const string Plants = "Repot the plants on the balcony";
    private const string OnMind = "The staging numbers have to be ready before Justin's call on Monday.";
    private const string NoStaging = "You asked the CDU for the staging numbers; it said there is no staging environment and never was.";

    /// <summary>System One that says yes to a statement when <c>yes</c> does, no to the rest, and remembers what it was shown.</summary>
    private sealed class FakeJudge(Func<string, bool> yes) : ISystemOneJudge
    {
        public string? State { get; private set; }
        public int Asked { get; private set; }

        public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
        {
            (State, Asked) = (state, Asked + 1);
            var answers = new SystemOneAnswers();
            foreach (var (key, q) in questions) answers.Nouls[key] = yes(q.Instructions) ? 0.9 : 0.2;
            return Task.FromResult(answers);
        }
    }

    private static SelfState Yesterday() => new()
    {
        DayKey = "2026-11-11", ThreadsKey = "2026-11-11", Mood = "tense", Preoccupation = OnMind,
        Threads = new List<string> { Staging, Bike, Plants }, ThreadAges = new List<int> { 6, 2, 1 },
        Beats = new List<DayBeat> { new() { Minute = 22 * 60, Text = "goes to bed" } },
    };

    private static string NextDay(SelfState s) => SelfMath.DayState(s, Tomorrow, Denise, rewritten: false, "", "", "", "");

    [Fact]
    public async Task AThreadAndPreoccupationTheWorldContradicts_AreGoneByTheNextDay()
    {
        var day = Yesterday();
        Assert.Contains("staging numbers", NextDay(day));
        var judge = new FakeJudge(q => q.Contains("isn't so") && q.Contains("staging"));

        var (settled, contradicted) = await ThreadReview.JudgeAsync(judge, "Denise", new[] { NoStaging }, day.Threads, day.Preoccupation, CancellationToken.None);
        Assert.True(SelfMath.Close(day, settled, contradicted));

        Assert.Contains(NoStaging, judge.State);
        Assert.Equal(new[] { Staging, OnMind }, contradicted);
        var next = NextDay(day);
        Assert.DoesNotContain("staging", next);
        Assert.Contains(Bike, next);
        Assert.Contains(Plants, next);
        Assert.Equal(new[] { 2, 1 }, day.ThreadAges);
        Assert.Null(day.Preoccupation);
        Assert.Empty(day.Resolved);
    }

    [Fact]
    public async Task ASettledThread_Goes_AndReadsAsSettled()
    {
        var day = Yesterday();
        var judge = new FakeJudge(q => q.Contains("done or settled") && q.Contains("rear light"));

        var (settled, contradicted) = await ThreadReview.JudgeAsync(judge, "Denise", new[] { "You fixed the rear light on your bike in the hall." }, day.Threads, day.Preoccupation, CancellationToken.None);
        SelfMath.Close(day, settled, contradicted);

        Assert.Empty(contradicted);
        Assert.Equal(new[] { Bike }, day.Resolved);
        Assert.Equal(new[] { Staging, Plants }, day.Threads);
        Assert.Equal(new[] { 6, 1 }, day.ThreadAges);
        Assert.Equal(OnMind, day.Preoccupation);
        Assert.Contains("Finally settled: " + Bike, SelfRender.Presence(day, 1, new DateTimeOffset(2026, 11, 11, 23, 0, 0, TimeSpan.Zero), Array.Empty<string>()));
    }

    [Fact]
    public async Task NothingRemembered_AsksNothing_AndKeepsEverything()
    {
        var day = Yesterday();
        var judge = new FakeJudge(_ => true);

        var (settled, contradicted) = await ThreadReview.JudgeAsync(judge, "Denise", Array.Empty<string>(), day.Threads, day.Preoccupation, CancellationToken.None);

        Assert.Equal(0, judge.Asked);
        Assert.False(SelfMath.Close(day, settled, contradicted));
        Assert.Equal(3, day.Threads.Count);
    }

    [Fact]
    public async Task NoAnswer_IsNoView_AndKeepsEverything()
    {
        var day = Yesterday();
        var silent = new Mock<ISystemOneJudge>();
        silent.Setup(j => j.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, SystemOneQuestion>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemOneAnswers());

        var (settled, contradicted) = await ThreadReview.JudgeAsync(silent.Object, "Denise", new[] { NoStaging }, day.Threads, day.Preoccupation, CancellationToken.None);

        Assert.False(SelfMath.Close(day, settled, contradicted));
        Assert.Equal(OnMind, day.Preoccupation);
    }

    [Fact]
    public void Close_KeepsTodaysThreadsFromBeingWrittenAgain()
    {
        var day = Yesterday();
        day.ThreadsKey = "2026-11-10";

        SelfMath.Close(day, Array.Empty<string>(), new[] { Staging, Bike, Plants });

        Assert.Empty(day.Threads);
        Assert.Equal(day.DayKey, day.ThreadsKey);
    }
}
