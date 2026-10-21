using System.Text.Json.Serialization;
using PartyTown.Grains.Generation;

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

    /// <summary>Cheap, fast, structured-output capable; the default for env-seeded worlds.</summary>
    public const string DefaultModel = "deepseek/deepseek-v4.1-flash";

    /// <summary>Speaking-phase voice: noticeably better character adherence for ~$0.0002 a line.</summary>
    public const string DefaultVoiceModel = "anthropic/claude-haiku-5.5";

    public string TEMP_ModelName { get; init; } = DefaultModel;

    /// <summary>Which <see cref="JobComplexity"/> tiers this provider serves (bound from
    /// config; <c>[Flags]</c>, so a comma-separated value sets several). Lets the router send,
    /// e.g., the spoken voice to a smarter model and the high-frequency thinking to a fast one.</summary>
    public JobComplexity SupportedComplexities { get; init; } = JobComplexity.General;
}

public sealed class OllamaProviderConfig : ILlmProviderConfig
{
    public string Type { get; init; } = "ollama";
    public string BaseUrl { get; init; } = "http://localhost:11434";

    public string TEMP_ModelName { get; init; } = "lfm2:24b";

    /// <summary>Which <see cref="JobComplexity"/> tiers this provider serves (bound from
    /// config; <c>[Flags]</c>, so a comma-separated value sets several). Lets the router send,
    /// e.g., the spoken voice to a smarter model and the high-frequency thinking to a fast one.</summary>
    public JobComplexity SupportedComplexities { get; init; } = JobComplexity.General;
}
