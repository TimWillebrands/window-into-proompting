using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace PartyTown.Grains.Generation;

public enum ReasoningEffort
{
    Default = 0,
    Off = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Max = 5,
}

public sealed class ReasoningPolicy(ReasoningEffort effort) : PipelinePolicy
{
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
