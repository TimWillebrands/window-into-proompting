namespace PartyTown.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; set; } = "openrouter";
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string? OpenRouterApiKey { get; set; }
}
