namespace PartyTown.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public List<LlmProviderConfig> Providers { get; set; } = [];
}

/// <summary>
/// Flat union config for any LLM provider. Use <see cref="Type"/> to discriminate.
/// Supported types: "ollama", "openrouter".
/// </summary>
public sealed class LlmProviderConfig
{
    public string Type { get; init; } = string.Empty;

    // Ollama
    public string BaseUrl { get; init; } = "http://localhost:11434";

    // OpenRouter
    public string ApiKey { get; init; } = string.Empty;
}
