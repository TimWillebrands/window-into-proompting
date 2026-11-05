using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace PartyTown.Grains.Generation;

/// <summary>How long a local model may reason before answering; budgets match llama.cpp's web UI.</summary>
public enum ReasoningEffort
{
    /// <summary>Send nothing: the server and model decide.</summary>
    Default = 0,
    Off = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    /// <summary>Thinking on, no token cap.</summary>
    Max = 5,
}

/// <summary>Adds llama.cpp's reasoning fields; the OpenAI SDK has none and drops patched ones.</summary>
public sealed class ReasoningPolicy(ReasoningEffort effort) : PipelinePolicy
{
    /// <summary>Tokens of reasoning allowed; the budget is what works on models without native levels.</summary>
    private int? Budget => effort switch
    {
        ReasoningEffort.Off => 0,
        ReasoningEffort.Low => 512,
        ReasoningEffort.Medium => 2048,
        ReasoningEffort.High => 8192,
        _ => null,
    };

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        AddFields(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        AddFields(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private void AddFields(PipelineMessage message)
    {
        if (message.Request.Content is not { } content) return;
        using var buffer = new MemoryStream();
        content.WriteTo(buffer);
        if (JsonNode.Parse(buffer.ToArray()) is not JsonObject body) return;

        body["reasoning_control"] = true;
        body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = effort != ReasoningEffort.Off };
        if (Budget is { } tokens) body["thinking_budget_tokens"] = tokens;
        message.Request.Content = BinaryContent.Create(BinaryData.FromString(body.ToJsonString()));
    }
}
