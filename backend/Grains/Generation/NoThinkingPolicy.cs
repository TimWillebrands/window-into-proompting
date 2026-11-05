using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace PartyTown.Grains.Generation;

/// <summary>Tells llama.cpp to skip reasoning; the OpenAI SDK has no field for it and drops patched ones.</summary>
public sealed class NoThinkingPolicy : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        AddFlag(message);
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        AddFlag(message);
        return ProcessNextAsync(message, pipeline, currentIndex);
    }

    private static void AddFlag(PipelineMessage message)
    {
        if (message.Request.Content is not { } content) return;
        using var buffer = new MemoryStream();
        content.WriteTo(buffer);
        if (JsonNode.Parse(buffer.ToArray()) is not JsonObject body) return;
        body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        message.Request.Content = BinaryContent.Create(BinaryData.FromString(body.ToJsonString()));
    }
}
