using System.Text.Json.Serialization;

namespace PartyTown.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public List<ILlmProviderConfig> Providers { get; set; } = [];
}

/// <summary>
/// Flat union config for any LLM provider. Use <see cref="Type"/> to discriminate.
/// Supported types: "ollama", "openrouter".
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OpenRouterProviderConfig), typeDiscriminator: "openrouter")]
[JsonDerivedType(typeof(OllamaProviderConfig), typeDiscriminator: "ollama")]
public interface ILlmProviderConfig
{
    public string Type { get; init; }
}

public sealed class OpenRouterProviderConfig : ILlmProviderConfig
{
    public string Type { get; init; } = "openrouter";
    public string ApiKey { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://openrouter.ai/api/v1";

    public string TEMP_ModelName { get; init; } = "nvidia/nemotron-3-super-120b-a12b:free";
}

public sealed class OllamaProviderConfig : ILlmProviderConfig
{
    public string Type { get; init; } = "ollama";
    public string BaseUrl { get; init; } = "http://localhost:11434";

    public string TEMP_ModelName { get; init; } = "lfm2:24b";
}
