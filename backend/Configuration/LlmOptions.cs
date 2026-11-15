using System.Text.Json.Serialization;
using PartyTown.Grains.Generation;

namespace PartyTown.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public List<ILlmProviderConfig> Providers { get; set; } = [];

    public List<LlmProviderEntry> DecisionProviders { get; set; } = [];
}

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

    public const string DefaultModel = "deepseek/deepseek-v4.1-flash";

    public const string DefaultVoiceModel = "anthropic/claude-haiku-5.5";

    public string TEMP_ModelName { get; init; } = DefaultModel;

    public JobComplexity SupportedComplexities { get; init; } = JobComplexity.General;
}

public sealed class OllamaProviderConfig : ILlmProviderConfig
{
    public string Type { get; init; } = "ollama";
    public string BaseUrl { get; init; } = "http://localhost:11434";

    public string TEMP_ModelName { get; init; } = "lfm2:24b";

    public JobComplexity SupportedComplexities { get; init; } = JobComplexity.General;
}
