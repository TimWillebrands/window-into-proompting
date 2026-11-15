namespace PartyTown.Model;

[GenerateSerializer, Alias(nameof(ChatMessage))]
public record class ChatMessage
{
    [Id(0)]
    public int MessageId { get; set; }

    [Id(1)]
    public string? Content { get; set; }

    [Id(2)]
    public string? Reasoning { get; set; }

    [Id(3)]
    public string? Appraisal { get; set; }

    [Id(4)]
    public string? Error { get; set; }

    [Id(5)]
    public string SenderType { get; set; } = "assistant";

    [Id(6)]
    public Guid SenderId { get; set; }

    [Id(7)]
    public long? SendAt { get; set; }

    [Id(9)]
    public Guid ChatGroupId { get; set; }

    [Id(8)]
    public ChatMessageMetadata? Metadata { get; set; }

    [Id(10)]
    public int? TriggeredByMessageId { get; set; }

    [Id(11)]
    public string? Kind { get; set; }

    [Id(12)]
    public List<Guid>? HeardBy { get; set; }

    [Id(13)]
    public string? Volume { get; set; }
}

[GenerateSerializer, Alias(nameof(ChatMessageMetadata))]
public sealed record class ChatMessageMetadata
{
    [Id(0)] public string? Provider { get; set; }

    [Id(1)] public string? ModelName { get; set; }
}
