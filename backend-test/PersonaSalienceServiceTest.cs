using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Model;
using PartyTown.Services.ResponsePipeline;
using PartyTown.Services.SystemOne;

namespace BackendTest;

/// <summary>Stop-signal salience through System One: the score, the clamp, and "let it ride" on any failure.</summary>
public class PersonaSalienceServiceTest
{
    private static SelfView MakeSelf() =>
        new(Guid.NewGuid(), "Vlad", DriverKind.LLM, "Reluctant philosopher", null, 0.5, 0.3);

    private static ChatMessage NewMessage(string content) => new()
    {
        MessageId = 99,
        SenderId = Guid.NewGuid(),
        SenderType = "user",
        Content = content,
    };

    private static PersonaSalienceService Service(Func<SystemOneAnswers> answer)
    {
        var judge = new Mock<ISystemOneJudge>();
        judge.Setup(j => j.AskAsync(It.IsAny<string>(), It.IsAny<IReadOnlyDictionary<string, SystemOneQuestion>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(answer);
        return new PersonaSalienceService(judge.Object, NullLogger.Instance);
    }

    private static SystemOneAnswers Answers(double? interrupt, string? kind = null)
    {
        var answers = new SystemOneAnswers();
        if (interrupt is { } p) answers.Nouls["interrupt"] = p;
        if (kind is not null) answers.Choices["kind"] = new Dictionary<string, double> { [kind] = 0.7, ["tangent"] = 0.3 };
        return answers;
    }

    private static Task<SalienceScore> ScoreAsync(PersonaSalienceService service) => service.ScoreAsync(
        MakeSelf(), "I was about to defend it.", "Defend it.", "I think it's actua",
        NewMessage("forget that, it's been deprecated"), "Mira", CancellationToken.None);

    [Fact]
    public async Task Answer_GivesValueAndMostLikelyKind()
    {
        var result = await ScoreAsync(Service(() => Answers(0.82, "redirect")));

        Assert.Equal(0.82, result.Value, precision: 3);
        Assert.Equal("redirect", result.Kind);
    }

    [Theory]
    [InlineData(2.4, 1.0)]
    [InlineData(-0.5, 0.0)]
    public async Task Value_IsClampedToUnitInterval(double raw, double expected)
        => Assert.Equal(expected, (await ScoreAsync(Service(() => Answers(raw, "contradict")))).Value);

    [Fact]
    public async Task NoKind_DefaultsToTangent()
        => Assert.Equal("tangent", (await ScoreAsync(Service(() => Answers(0.4)))).Kind);

    [Fact]
    public async Task NoAnswer_LetsItRide()
        => Assert.Equal(SalienceScore.LetItRide, await ScoreAsync(Service(() => Answers(null))));

    [Fact]
    public async Task JudgeFailure_LetsItRide()
        => Assert.Equal(SalienceScore.LetItRide, await ScoreAsync(Service(() => throw new InvalidOperationException("judge down"))));

    [Fact]
    public async Task Cancellation_Propagates()
        => await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScoreAsync(Service(() => throw new OperationCanceledException())));
}
