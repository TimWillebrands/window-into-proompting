using PartyTown.Services.SystemOne;

namespace BackendTest;

/// <summary>System One port (ADR 0019): Jev wire mapping and calibrated sampling.</summary>
public class SystemOneTest
{
    [Fact]
    public void BuildRequest_MapsNoulAndChoiceToJevSchema()
    {
        var req = JevJudge.BuildRequest("jev-latest", "state text", new Dictionary<string, SystemOneQuestion>
        {
            ["memorable"] = new NoulQuestion("Worth remembering"),
            ["next"] = new ChoiceQuestion("Pick", new Dictionary<string, string?> { ["0"] = "rest", ["1"] = null }),
        });

        Assert.Equal("jev-latest", req["model"]!.GetValue<string>());
        Assert.Equal("state text", req["state"]!.GetValue<string>());
        Assert.Equal("noul", req["questions"]!["memorable"]!["type"]!.GetValue<string>());
        Assert.Equal("choice", req["questions"]!["next"]!["type"]!.GetValue<string>());
        Assert.Equal("rest", req["questions"]!["next"]!["criteria"]!["0"]!.GetValue<string>());
        Assert.Null(req["questions"]!["next"]!["criteria"]!["1"]);
    }

    [Fact]
    public void ParseResponse_ReadsNoulsAndChoiceProbabilities()
    {
        const string body = """
        {"model":"jev-1.13.0","answers":{
          "memorable":{"type":"noul","noul":0.82},
          "next":{"type":"choice","choice":"1","confidence":0.7,"probabilities":{"0":0.2,"1":0.8}}
        },"usage":{"input_tokens":10,"output_tokens":3}}
        """;

        var answers = JevJudge.ParseResponse(body);

        Assert.Equal(0.82, answers.Noul("memorable", 0));
        Assert.Equal(0.8, answers.Choices["next"]["1"]);
    }

    [Fact]
    public void SampleChoice_FollowsDistributionAndSharpensWithLowTemperature()
    {
        var answers = new SystemOneAnswers();
        answers.Choices["next"] = new Dictionary<string, double> { ["a"] = 0.75, ["b"] = 0.25 };
        var random = new Random(7);

        var warm = Enumerable.Range(0, 4000).Count(_ => answers.SampleChoice("next", 1.0, random) == "a") / 4000.0;
        var cold = Enumerable.Range(0, 4000).Count(_ => answers.SampleChoice("next", 0.2, random) == "a") / 4000.0;

        Assert.InRange(warm, 0.70, 0.80);
        Assert.True(cold > 0.98);
        Assert.Null(answers.SampleChoice("missing"));
    }
}
