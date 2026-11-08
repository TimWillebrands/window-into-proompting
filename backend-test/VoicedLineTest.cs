using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains.Generation;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World;

namespace BackendTest;

/// <summary>What may be published as a persona's line: never an error placeholder, never the note that steered it. Cases are verbatim from prod.</summary>
public class VoicedLineTest
{
    private const string CafeLeak = "Lena asks in Dutch whether I've eaten. Reply in English, short, under 12 words, avoid stale words and \"boat, foam, ferry, real\". I'm tired, cold toast earlier, so admit that lightly.\n\nnothing but cold toast, and that was a crime too";
    private const string DmLeak = "I need to send Anne a short private line, under 12 words, light, in English. The draft's question about Justin's book is fine but a bit probing for someone I've never really talked to. Keep it to the dunes joke.\n\nSend to Anne: dunes bit made me laugh, hope justin's still on that book";

    [Theory]
    [InlineData("[ERROR: Can't parse response]")]
    [InlineData("  [ERROR: Can't parse response]\n")]
    [InlineData("<<error: provider returned 503>>")]
    [InlineData("<<<SYSTEM ERROR: upstream provider timeout>>>")]
    [InlineData("(failed to generate)")]
    [InlineData("Error: request failed with status 500")]
    public void IsFailureMarker_CatchesPlaceholders(string text)
        => Assert.True(SpeechCleanup.IsFailureMarker(text));

    [Theory]
    [InlineData("*falls silent*")]
    [InlineData("(sigh)")]
    [InlineData("[laughs] fair enough")]
    [InlineData("Error: coffee not found")]
    [InlineData("Can't believe the machine died again, error after error.")]
    [InlineData("that was a total failure, the cake I mean")]
    [InlineData("")]
    public void IsFailureMarker_LeavesSpeechAlone(string text)
        => Assert.False(SpeechCleanup.IsFailureMarker(text));

    [Theory]
    [InlineData(CafeLeak, "nothing but cold toast, and that was a crime too")]
    [InlineData(DmLeak, "dunes bit made me laugh, hope justin's still on that book")]
    [InlineData("Keep it short, in Dutch, under 10 words.\n\n\"Ja, ik kom eraan.\"", "Ja, ik kom eraan.")]
    [InlineData("Reply in English, one line, under 12 words, avoid stale words.", "")]
    public void DropPlanningNote_KeepsOnlyTheLine(string raw, string expected)
        => Assert.Equal(expected, SpeechCleanup.DropPlanningNote(raw));

    [Theory]
    [InlineData("I'll reply to Mara later, promise.")]
    [InlineData("Lena asks if I'm coming. Tell her yes.")]
    [InlineData("ok so the short version: he never paid.\n\nlong version tomorrow")]
    [InlineData("Sunday, 10:42. Thomas's boots still not bought.\n\nOnions at half eleven, boots by the door.")]
    [InlineData("Keep it light, you said. Fine. I'm keeping it light.")]
    public void DropPlanningNote_LeavesOrdinaryTextAlone(string raw)
        => Assert.Equal(raw, SpeechCleanup.DropPlanningNote(raw));

    [Fact]
    public void Clean_NeverPassesAPlaceholderOrANote()
    {
        Assert.Equal(string.Empty, SpeechCleanup.Clean("[ERROR: Can't parse response]", "Henkie Spenkie"));
        Assert.Equal("nothing but cold toast, and that was a crime too", SpeechCleanup.Clean(CafeLeak, "Yusuf Halloran"));
    }

    [Theory]
    [InlineData("Al wijzer geworden? Ik ben nog steeds in de barrel, en dat is waar.", "Al wijzer geworden?", "Ik ben nog steeds in de barrel, en dat is waar.")]
    [InlineData("Al wijzer geworden?", "Al wijzer geworden?", "")]
    [InlineData("Ha! Fair.", "Ha!", "Ha! Fair.")]
    [InlineData("Nee, alleen mopperig.", "Al wijzer geworden?", "Nee, alleen mopperig.")]
    public void DropEcho_StripsTheAnsweredLineRepeatedBack(string text, string answering, string expected)
        => Assert.Equal(expected, SpeechCleanup.DropEcho(text, answering));

    private static WorldLlm LlmAnswering(string raw)
    {
        var endpoint = new Mock<ILlmEndpointGrain>();
        endpoint.Setup(e => e.CompleteOneShotAsync(It.IsAny<LlmGenerationJob>(), It.IsAny<CancellationToken>())).ReturnsAsync(raw);
        var router = new Mock<ILlmRouterGrain>();
        router.Setup(r => r.RouteAsync(It.IsAny<JobComplexity>(), It.IsAny<CancellationToken>())).ReturnsAsync(endpoint.Object);
        var grains = new Mock<IGrainFactory>();
        grains.Setup(g => g.GetGrain<ILlmRouterGrain>(0, null)).Returns(router.Object);
        return new WorldLlm(grains.Object, NullLogger<WorldLlm>.Instance);
    }

    [Fact]
    public async Task WorldLlm_JsonFieldHoldingAPlaceholder_IsAFailedParse()
    {
        // Henkie's Board reply, Sun 10:16: the call succeeded, the body was the placeholder, and it got posted.
        var llm = LlmAnswering("""{"body": "[ERROR: Can't parse response]", "respect": "no"}""");

        Assert.Null(await llm.CompleteJsonAsync(JobComplexity.CharacterVoice, "s", "u", default, fields: new[] { "body", "respect" }));
    }

    [Fact]
    public async Task WorldLlm_TextPlaceholder_IsNoAnswer()
        => Assert.Null(await LlmAnswering("[ERROR: Can't parse response]").CompleteAsync(JobComplexity.CharacterVoice, "s", "u", default));

    [Fact]
    public async Task WorldLlm_OrdinaryJson_StillParses()
    {
        var json = await LlmAnswering("""{"body": "marijke gets the soup, you get the bread.", "respect": "no"}""")
            .CompleteJsonAsync(JobComplexity.CharacterVoice, "s", "u", default, fields: new[] { "body", "respect" });

        Assert.Equal("marijke gets the soup, you get the bread.", json!["body"]!.GetValue<string>());
    }
}

