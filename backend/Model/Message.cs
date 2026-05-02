namespace PartyTown.Model;

/// <summary>
/// A single message in a party's conversation history.
/// Persisted by <see cref="PartyGrain"/> and sent to clients via realtime hub.
/// </summary>
[GenerateSerializer, Alias(nameof(ChatMessage))]
public record class ChatMessage
{
    /// <summary>Unique identifier for this message within the party.</summary>
    [Id(0)]
    public int MessageId { get; set; }

    /// <summary>The actual message text content.</summary>
    [Id(1)]
    public string? Content { get; set; }

    /// <summary>Reasoning output from models that support it (e.g., o1).</summary>
    [Id(2)]
    public string? Reasoning { get; set; }

    /// <summary>Appraisal decision JSON — the persona's evaluation of whether and how to respond.</summary>
    [Id(3)]
    public string? Appraisal { get; set; }

    /// <summary>Error message if generation failed.</summary>
    [Id(4)]
    public string? Error { get; set; }

    /// <summary>Sender type: "user" or "assistant".</summary>
    [Id(5)]
    public string SenderType { get; set; } = "assistant";

    /// <summary>ID of the persona or user who sent this message.</summary>
    [Id(6)]
    public Guid SenderId { get; set; }

    /// <summary>Unix timestamp in milliseconds when the message was sent/completed.</summary>
    [Id(7)]
    public long? SendAt { get; set; }

    /// <summary>Identifier of the chat group this message belongs to.</summary>
    [Id(9)]
    public Guid ChatGroupId { get; set; }

    /// <summary>Generation papertrail (model, provider, etc.). Null for user messages and pre-papertrail history.</summary>
    [Id(8)]
    public ChatMessageMetadata? Metadata { get; set; }

    /// <summary>The message id this one is reacting to. Null for user-authored messages
    /// and for assistant messages persisted before this field existed.</summary>
    [Id(10)]
    public int? TriggeredByMessageId { get; set; }
}

/// <summary>Provenance metadata for an assistant message — which backend produced it.
/// Persisted on <see cref="ChatMessage"/> and surfaced in the UI's Details panel.</summary>
[GenerateSerializer, Alias(nameof(ChatMessageMetadata))]
public sealed record class ChatMessageMetadata
{
    /// <summary>Provider backend that served the generation (e.g. "ollama", "openrouter").</summary>
    [Id(0)] public string? Provider { get; set; }

    /// <summary>Model that generated this message (e.g. "qwen2.5:14b").</summary>
    [Id(1)] public string? ModelName { get; set; }
}
