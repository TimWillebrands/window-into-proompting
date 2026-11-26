using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

public class GravityTest
{
    private static readonly int[] LastLines = { 0, 6, 15, 16, 25, 40, 41, 80 };

    private static IEnumerable<double> Rolls(int n = 1000) => Enumerable.Range(0, n).Select(i => (i + 0.5) / n);

    private static double Share(int lastWords, double? gravity, UtteranceRhythm.Length length)
        => Rolls().Count(r => UtteranceRhythm.Pick(lastWords, r, gravity) == length) / 1000.0;

    [Fact]
    public void Pick_NoView_IsTodaysDrawExactly()
    {
        foreach (var words in LastLines)
        foreach (var roll in Rolls())
        {
            var (shortP, mediumP) = words <= 15 ? (0.70, 0.25) : words <= 40 ? (0.45, 0.40) : (0.30, 0.45);
            var today = roll < shortP ? UtteranceRhythm.Length.Short : roll < shortP + mediumP ? UtteranceRhythm.Length.Medium : UtteranceRhythm.Length.Long;
            Assert.Equal(today, UtteranceRhythm.Pick(words, roll, null));
            Assert.Equal(today, UtteranceRhythm.Pick(words, roll));
        }
    }

    [Fact]
    public void Pick_WhatALineThatAsksNothingGets_IsTodaysDrawExactly()
    {
        foreach (var words in LastLines)
        foreach (var roll in Rolls())
        foreach (var gravity in new[] { 0, 0.1, 0.2, UtteranceRhythm.Light })
            Assert.Equal(UtteranceRhythm.Pick(words, roll), UtteranceRhythm.Pick(words, roll, gravity));
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.25, 0.0)]
    [InlineData(0.625, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(1.4, 1.0)]
    public void Asked_IsMeasuredFromWhatTheJudgeGivesALineThatAsksNothing(double gravity, double asked)
        => Assert.Equal(asked, UtteranceRhythm.Asked(gravity), 9);

    [Fact]
    public void Pick_HighGravity_MakesARealAnswerLikely_AndOneLinersRare()
    {
        Assert.InRange(Share(20, 0.9, UtteranceRhythm.Length.Full), 0.78, 0.84);
        Assert.InRange(Share(20, 0.9, UtteranceRhythm.Length.Short), 0, 0.02);
        Assert.InRange(Share(6, 0.9, UtteranceRhythm.Length.Short), 0, 0.03);
    }

    [Fact]
    public void Pick_SmallTalkAtTheJudgesUsualLevel_StaysShort()
    {
        Assert.InRange(Share(6, 0.285, UtteranceRhythm.Length.Full), 0, 0.02);
        Assert.InRange(Share(6, 0.285, UtteranceRhythm.Length.Short), 0.64, 0.70);
    }

    [Fact]
    public void Pick_StaysADraw_EvenAtHighGravity()
    {
        var drawn = Rolls().Select(r => UtteranceRhythm.Pick(6, r, 0.8)).Distinct().ToList();
        Assert.Contains(UtteranceRhythm.Length.Full, drawn);
        Assert.Contains(UtteranceRhythm.Length.Short, drawn);
        Assert.Contains(UtteranceRhythm.Length.Medium, drawn);
    }

    [Fact]
    public void Pick_MoreGravity_NeverMeansMoreOneLiners()
    {
        var previous = 1.0;
        for (var g = 0.0; g <= 1.0; g += 0.1)
        {
            var share = Share(6, g, UtteranceRhythm.Length.Short);
            Assert.True(share <= previous + 1e-9, $"short share rose at gravity {g:F1}");
            previous = share;
        }
    }

    [Fact]
    public void Cue_EveryLengthHasItsOwn_AndTheRealAnswerAsksForWhatMatters()
    {
        var cues = Enum.GetValues<UtteranceRhythm.Length>().Select(UtteranceRhythm.Cue).ToList();
        Assert.Equal(cues.Count, cues.Distinct().Count());
        Assert.Equal("Length: one short line this time, under 12 words.", UtteranceRhythm.Cue(UtteranceRhythm.Length.Short));
        Assert.Equal("Length: a sentence or two this time, under 30 words.", UtteranceRhythm.Cue(UtteranceRhythm.Length.Medium));
        Assert.Equal("Length: you can take a bit more room this time if you have something to say, but under 60 words.", UtteranceRhythm.Cue(UtteranceRhythm.Length.Long));
        var full = UtteranceRhythm.Cue(UtteranceRhythm.Length.Full);
        Assert.Contains("under 70 words", full);
        Assert.Contains("think or feel", full);
        Assert.Contains("not a tour of everything", full);
        Assert.Contains("Nothing made up", full);
        Assert.Contains("hand the question back", full);
        Assert.Contains("not as an essay", full);
        Assert.DoesNotContain("\u2014", full);
    }

    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Sam = Guid.NewGuid();

    private static ChatMessage Line(int id, Guid who, string text) => new() { MessageId = id, SenderId = who, Content = text };

    [Fact]
    public void State_SaysWhoWhatAndWhy_AsFactsOnly()
    {
        var present = new[] { new ParticipantView(Me, "Denise", DriverKind.LLM), new ParticipantView(Sam, "Sam", DriverKind.User) };
        var asked = Line(4, Sam, "how do you feel about your life?");
        var history = new[]
        {
            Line(1, Sam, "hey"), Line(2, Me, "hey you"), Line(3, Sam, new string('x', 400)), asked,
        };

        var state = Gravity.State("Denise", "CTO of a small software company.", Me, present, history, asked, "Tell Sam how the year went.", owed: true, inPerson: false);

        Assert.Contains("# Denise\nCTO of a small software company.", state);
        Assert.Contains("a private chat, with Sam", state);
        Assert.Contains("Sam: hey\nDenise: hey you", state);
        Assert.DoesNotContain(new string('x', 400), state);
        Assert.Contains("# The line Denise is answering\nSam: how do you feel about your life?", state);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(state, "how do you feel"));
        Assert.Contains("# What Denise means to get across\nTell Sam how the year went.", state);
        Assert.Contains("Denise put this off earlier to answer it properly.", state);
    }

    [Fact]
    public void State_LeavesOutWhatTheTurnDoesNotHave()
    {
        var present = new[] { new ParticipantView(Me, "Mara", DriverKind.LLM), new ParticipantView(Sam, "Sanne", DriverKind.LLM), new ParticipantView(Guid.NewGuid(), "Dan", DriverKind.LLM) };

        var state = Gravity.State("Mara", null, Me, present, Array.Empty<ChatMessage>(), null, null, owed: false, inPerson: true);

        Assert.Contains("(no bio)", state);
        Assert.Contains("talking in person, with Sanne, Dan", state);
        Assert.Contains("(nothing yet)", state);
        Assert.DoesNotContain("answering", state);
        Assert.DoesNotContain("means to get across", state);
        Assert.DoesNotContain("put this off", state);
    }

    [Fact]
    public void Bio_IsTheShortBio_ElseTheFirstLinesOfTheSheet()
    {
        var withBio = new SelfView(Me, "Denise", DriverKind.LLM, "Pink hair, band shirts.", "You are Denise. A long sheet.", 0.5, 0.5);
        var sheetOnly = withBio with { Bio = null, SystemPrompt = "You are 34. You run a company. You swear. You keep a shrine." };

        Assert.Equal("Pink hair, band shirts.", Gravity.Bio(withBio, null));
        Assert.Equal("You are 34. You run a company. You swear.", Gravity.Bio(sheetOnly, null));
        Assert.Equal("Lived bio here.", Gravity.Bio(withBio, "# Denise\n## Who you are\nLived bio here.\n## How you talk\nfast"));
    }

    [Fact]
    public void Question_IsAboutThisPersona()
        => Assert.Equal("What Justin Ruysch is about to say needs more than a line or two.", Gravity.Question("Justin Ruysch").Instructions);

    private sealed class ScriptedJudge(Func<IReadOnlyDictionary<string, PartyTown.Services.SystemOne.SystemOneQuestion>, PartyTown.Services.SystemOne.SystemOneAnswers> answer)
        : PartyTown.Services.SystemOne.ISystemOneJudge
    {
        public List<IReadOnlyDictionary<string, PartyTown.Services.SystemOne.SystemOneQuestion>> Asked { get; } = new();

        public Task<PartyTown.Services.SystemOne.SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, PartyTown.Services.SystemOne.SystemOneQuestion> questions, CancellationToken ct)
        {
            Asked.Add(questions);
            return Task.FromResult(answer(questions));
        }
    }

    private static PartyTown.Services.SystemOne.SystemOneAnswers Answers(double gravity, Dictionary<string, double>? recall = null)
    {
        var answers = new PartyTown.Services.SystemOne.SystemOneAnswers();
        answers.Nouls[Gravity.Key] = gravity;
        if (recall is not null) answers.Choices[Gravity.RecallKey] = recall;
        return answers;
    }

    private static readonly string[] Memories = { "Mara's sister's landlord wants her out over the dog.", "You lent Denise your umbrella." };

    [Fact]
    public void Recall_OffersEveryMemoryAndNone()
    {
        var question = Gravity.Recall("Pieter", Memories);

        Assert.Equal("Which of these memories, if any, is on Pieter's mind in this moment?", question.Instructions);
        Assert.Equal(new[] { Gravity.Nothing, "1", "2" }, question.Options.Keys.ToArray());
        Assert.Equal(Memories[1], question.Options["2"]);
    }

    [Fact]
    public async Task WeighAsync_AsksTheMemoryInTheSameCall_AndSamplesIt()
    {
        var judge = new ScriptedJudge(_ => Answers(0.4, new Dictionary<string, double> { [Gravity.Nothing] = 0.5, ["1"] = 0.5, ["2"] = 0 }));
        var picks = new List<int?>();
        for (var i = 0; i < 200; i++)
            picks.Add((await Gravity.WeighAsync(judge, "Pieter", "state", Memories, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None, new Random(i))).Memory);

        Assert.All(judge.Asked, q => Assert.True(q.ContainsKey(Gravity.Key) && q.ContainsKey(Gravity.RecallKey)));
        Assert.Contains(1, picks);
        Assert.Contains(null, picks);
        Assert.DoesNotContain(2, picks);
    }

    [Fact]
    public async Task WeighAsync_NoneOnTheirMind_IsAView()
    {
        var judge = new ScriptedJudge(_ => Answers(0.3, new Dictionary<string, double> { [Gravity.Nothing] = 1, ["1"] = 0, ["2"] = 0 }));

        var weighed = await Gravity.WeighAsync(judge, "Pieter", "state", Memories, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new Gravity.Weighed(0.3, null, true), weighed);
    }

    [Fact]
    public async Task WeighAsync_NoDistribution_IsNoView_ButGravityStands()
    {
        var judge = new ScriptedJudge(_ => Answers(0.6));

        var weighed = await Gravity.WeighAsync(judge, "Pieter", "state", Memories, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new Gravity.Weighed(0.6, null, false), weighed);
    }

    [Fact]
    public async Task WeighAsync_NothingRecalled_AsksGravityAlone()
    {
        var judge = new ScriptedJudge(_ => Answers(0.2));

        var weighed = await Gravity.WeighAsync(judge, "Pieter", "state", Array.Empty<string>(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None);

        Assert.False(Assert.Single(judge.Asked).ContainsKey(Gravity.RecallKey));
        Assert.Equal(new Gravity.Weighed(0.2, null, false), weighed);
    }

    [Fact]
    public async Task WeighAsync_JudgeFails_IsNoViewOnEither()
    {
        var judge = new ScriptedJudge(_ => throw new HttpRequestException("down"));

        Assert.Equal(Gravity.Weighed.Unweighed, await Gravity.WeighAsync(judge, "Pieter", "state", Memories, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task WeighAsync_TheJudgesFaintTail_IsNeverDrawn()
    {
        var judge = new ScriptedJudge(_ => Answers(0.3, new Dictionary<string, double> { [Gravity.Nothing] = 0.9, ["1"] = Gravity.RecallFloor - 0.01, ["2"] = Gravity.RecallFloor + 0.01 }));
        var picks = new List<int?>();
        for (var i = 0; i < 2000; i++)
            picks.Add((await Gravity.WeighAsync(judge, "Pieter", "state", Memories, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, CancellationToken.None, new Random(i))).Memory);

        Assert.DoesNotContain(1, picks);
        Assert.Contains(2, picks);
    }
}
