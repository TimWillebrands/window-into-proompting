using System.ClientModel;
using System.Net;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>How an LLM call ended decides what PostHog shows: done, stopped by its caller, timed out, or failed and why.</summary>
public class LlmGenerationTest
{
    private static readonly LlmGeneration Call = new(Array.Empty<LlmChatMessage>(), "", "ollama", "d1:3b", "http://localhost:11434", Streamed: false);

    [Fact]
    public void Done_IsNoErrorAndNotCancelled()
        => Assert.Equal((null, false), (Call.Ended(null, completed: true, default).Error, Call.Ended(null, true, default).Cancelled));

    [Fact]
    public void CallerCancelled_IsNotAnError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var ended = Call.Ended(new OperationCanceledException(cts.Token), completed: false, cts.Token);

        Assert.Equal((null, true), (ended.Error, ended.Cancelled));
    }

    [Fact]
    public void ReaderStoppedEarly_IsCancelled()
        => Assert.True(Call.Ended(null, completed: false, default).Cancelled);

    [Fact]
    public void ACancelNobodyAskedFor_IsATimeout()
        => Assert.Equal("timed out", Call.Ended(new TaskCanceledException(), completed: false, default).Error);

    public static TheoryData<Exception, int?> Failures => new()
    {
        { new HttpRequestException("402", null, HttpStatusCode.PaymentRequired), 402 },
        { new HttpRequestException("Connection refused"), null },
        { new AggregateException(new HttpRequestException("busy", null, HttpStatusCode.TooManyRequests)), 429 },
        { new InvalidOperationException("Claude: credit balance too low"), null },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Failure_KeepsItsMessageAndStatus(Exception failure, int? status)
    {
        var ended = Call.Ended(failure, completed: false, default);

        Assert.Equal((failure.Message, status, false), (ended.Error, ended.HttpStatus, ended.Cancelled));
    }

    [Theory]
    [InlineData("""{"answers":{},"usage":{"input_tokens":1802,"output_tokens":0}}""", 1802L, 0L)]
    [InlineData("""{"answers":{}}""", null, null)]
    [InlineData("not json", null, null)]
    public void JudgeUsage_ReadsTheSystemOneAnswer(string response, long? input, long? output)
    {
        var usage = LlmEndpointGrainUtils.JudgeUsage(response);

        Assert.Equal((input, output), (usage?.Input, usage?.Output));
    }
}
