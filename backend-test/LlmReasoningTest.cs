using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Grains.Generation;
using PartyTown.Observability;

namespace BackendTest;

/// <summary>What actually goes over the wire for each reasoning level.</summary>
public class LlmReasoningTest
{
    private sealed class Capture : HttpMessageHandler
    {
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"x","object":"chat.completion","created":1,"model":"m","choices":[{"index":0,"finish_reason":"stop","logprobs":null,"message":{"role":"assistant","content":"hi"}}]}""",
                    Encoding.UTF8, "application/json"),
            };
        }
    }

    private static async Task<JsonNode> SentBodyAsync(ReasoningEffort? effort)
    {
        var capture = new Capture();
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("http://llama/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(capture)),
        };
        if (effort is { } level) options.AddPolicy(new ReasoningPolicy(level), PipelinePosition.PerCall);
        var client = new ChatClient("m", new ApiKeyCredential("k"), options);
        var job = new LlmGenerationJob { Messages = new List<LlmChatMessage> { new() { Role = "user", Content = "hello" } } };

        var text = await LlmEndpointGrainUtils.CompleteOneShotAsync(
            NullLogger.Instance, job, client, () => { }, Mock.Of<IAiObservabilityCapture>(), "p", "m", "http://llama");

        Assert.Equal("hi", text);
        return JsonNode.Parse(capture.Body)!;
    }

    [Theory]
    [InlineData(ReasoningEffort.Off, 0)]
    [InlineData(ReasoningEffort.Low, 512)]
    [InlineData(ReasoningEffort.Medium, 2048)]
    [InlineData(ReasoningEffort.High, 8192)]
    public async Task CappedLevels_SendTheirTokenBudget(ReasoningEffort effort, int budget)
    {
        var body = await SentBodyAsync(effort);

        Assert.Equal(budget, body["thinking_budget_tokens"]!.GetValue<int>());
        Assert.True(body["reasoning_control"]!.GetValue<bool>());
        Assert.Equal("hello", body["messages"]![0]!["content"]!.ToString());
    }

    [Fact]
    public async Task Off_AlsoDisablesThinkingInTheTemplate()
    {
        var body = await SentBodyAsync(ReasoningEffort.Off);

        Assert.False(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Max_KeepsThinkingOnWithNoCap()
    {
        var body = await SentBodyAsync(ReasoningEffort.Max);

        Assert.True(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Null(body["thinking_budget_tokens"]);
    }

    [Fact]
    public async Task WithoutThePolicy_NothingExtraIsSent()
    {
        var body = await SentBodyAsync(null);

        Assert.Null(body["chat_template_kwargs"]);
        Assert.Null(body["thinking_budget_tokens"]);
        Assert.Null(body["reasoning_control"]);
    }
}
