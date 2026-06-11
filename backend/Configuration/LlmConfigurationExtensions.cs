using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PartyTown.Configuration;

public static class LlmConfigurationExtensions
{
    /// <summary>
    /// Binds the polymorphic <c>Llm:Providers</c> section into <see cref="LlmOptions"/>,
    /// discriminating on each entry's <c>Type</c>. Shared by the backend host and the
    /// bench host (ADR 0011) — the two differ only in which configuration root they
    /// hand the section from.
    /// </summary>
    public static IServiceCollection AddLlmProviderOptions(this IServiceCollection services)
    {
        services.AddSingleton<IConfigureOptions<LlmOptions>>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            return new ConfigureOptions<LlmOptions>(options =>
            {
                var section = config.GetSection($"{LlmOptions.SectionName}:Providers");
                foreach (var child in section.GetChildren())
                {
                    var type = child["Type"];
                    ILlmProviderConfig provider = type switch
                    {
                        "ollama" => child.Get<OllamaProviderConfig>()!,
                        "openrouter" => child.Get<OpenRouterProviderConfig>()!,
                        _ => throw new InvalidOperationException($"Unknown LLM provider type: '{type}'")
                    };
                    options.Providers.Add(provider);
                }
            });
        });
        return services;
    }
}
