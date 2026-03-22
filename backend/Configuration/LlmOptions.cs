namespace PartyTown.Configuration;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    public IReadOnlyList<ILlmProviderOptions> Providers { get; set; } = [];
}

public sealed record OllamaOptions : ILlmProviderOptions
{
    public required string BaseUrl { get; init; } = "http://localhost:11434";
    public int Priority { get; init; }
}

public sealed record OpenRouterOptions : ILlmProviderOptions
{
    public required string ApiKey { get; init; }
    public int Priority { get; init; }
}

public interface ILlmProviderOptions
{
    public int Priority { get; }
}
