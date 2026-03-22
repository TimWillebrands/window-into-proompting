using System.Text.Json.Nodes;

namespace PartyTown.Services.Llm;

public sealed record class LlmModel
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string? Description { get; init; }
    public int? ContextLength { get; init; }
}

public sealed record class LlmChatMessage
{
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string? Name { get; init; }
}

public sealed record class LlmGenerationParams
{
    public string Model { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public IReadOnlyList<LlmChatMessage> Messages { get; init; } = [];
    public string? UserId { get; init; }
    public string? RoomId { get; init; }
    public double? Temperature { get; init; }
    public JsonObject? ResponseFormat { get; init; }
}

public sealed record class LlmGenerationEvent
{
    public string Type { get; init; } = "message";
    public string Data { get; init; } = string.Empty;
}
