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

/// <summary>What actually goes over the wire when a provider skips thinking.</summary>
public class LlmThinkingTest
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

    private static async Task<JsonNode> SentBodyAsync(bool disableThinking)
    {
        var capture = new Capture();
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("http://llama/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(capture)),
        };
        if (disableThinking) options.AddPolicy(new NoThinkingPolicy(), PipelinePosition.PerCall);
        var client = new ChatClient("m", new ApiKeyCredential("k"), options);
        var job = new LlmGenerationJob { Messages = new List<LlmChatMessage> { new() { Role = "user", Content = "hello" } } };

        var text = await LlmEndpointGrainUtils.CompleteOneShotAsync(
            NullLogger.Instance, job, client, () => { }, Mock.Of<IAiObservabilityCapture>(), "p", "m", "http://llama");

        Assert.Equal("hi", text);
        return JsonNode.Parse(capture.Body)!;
    }

    [Fact]
    public async Task DisableThinking_SendsTheChatTemplateFlag()
    {
        var body = await SentBodyAsync(disableThinking: true);

        Assert.False(body["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.Equal("hello", body["messages"]![0]!["content"]!.ToString());
    }

    [Fact]
    public async Task ByDefault_NoChatTemplateFlagIsSent()
    {
        var body = await SentBodyAsync(disableThinking: false);

        Assert.Null(body["chat_template_kwargs"]);
    }
}
