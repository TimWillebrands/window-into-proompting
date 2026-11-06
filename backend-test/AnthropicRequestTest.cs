using System.Text.Json.Nodes;
using PartyTown.Grains.Generation;
using PartyTown.World;

namespace BackendTest;

/// <summary>Jobs map onto Messages shapes the API accepts.</summary>
public class AnthropicRequestTest
{
    private static LlmChatMessage Msg(string role, string content) => new() { Role = role, Content = content };

    [Fact]
    public void SpeakingShape_GuidanceBecomesTheFinalUserTurn()
    {
        var (system, turns) = AnthropicRequest.ToTurns(new[]
        {
            Msg("system", "You are Ada."),
            Msg("user", "Bob: hi"),
            Msg("assistant", "hello"),
            Msg("system", "Keep it short."),
        });

        Assert.Equal("You are Ada.", system);
        Assert.Equal(new[] { "user", "assistant", "user" }, turns.Select(t => t.Role));
        Assert.Equal(AnthropicRequest.DirectionPrefix + "Keep it short.", turns[^1].Content);
    }

    [Fact]
    public void HistoryEndingOnAssistant_IsNotSentAsPrefill()
    {
        var (_, turns) = AnthropicRequest.ToTurns(new[] { Msg("assistant", "first"), Msg("assistant", "second") });

        Assert.Equal(new[] { "user", "assistant", "user" }, turns.Select(t => t.Role));
        Assert.Equal("first\n\nsecond", turns[1].Content);
    }

    [Fact]
    public void BlankMessages_AreDropped()
    {
        var (system, turns) = AnthropicRequest.ToTurns(new[] { Msg("system", " "), Msg("user", "") });

        Assert.Null(system);
        Assert.Single(turns);
    }

    [Fact]
    public void Schema_IsReducedToTheSupportedSubset()
    {
        var format = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "x",
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["pick"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                        ["score"] = new JsonObject { ["type"] = "number", ["minimum"] = 0 },
                        ["inner"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
                    },
                },
            },
        }.ToJsonString();

        var schema = AnthropicRequest.ToSchema(format)!;

        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.False(schema["properties"]!["inner"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Null(schema["properties"]!["score"]!["minimum"]);
        Assert.Null(schema["properties"]!["pick"]!["type"]);
        Assert.Equal(2, schema["properties"]!["pick"]!["anyOf"]!.AsArray().Count);
    }

    [Fact]
    public void WorldSchema_PassesThrough()
        => Assert.NotNull(AnthropicRequest.ToSchema(WorldLlm.SchemaFor(new[] { "onYourMind" })));

    [Theory]
    [InlineData("claude-opus-5-5", true)]
    [InlineData("claude-sonnet-5-5", true)]
    [InlineData("claude-haiku-5-5", false)]
    public void RefusalFallback_SkipsHaiku(string model, bool expected)
        => Assert.Equal(expected, AnthropicRequest.HasRefusalFallback(model));
}
