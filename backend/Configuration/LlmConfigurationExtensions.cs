using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PartyTown.Grains.Generation;
using PartyTown.Services.SystemOne;

namespace PartyTown.Configuration;

public static class LlmConfigurationExtensions
{
    public static IServiceCollection AddLlmProviderOptions(this IServiceCollection services)
    {
        services.AddSingleton<Tailnets>();
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

                var openRouterKey = config["OPENROUTER_API_KEY"];
                if (!string.IsNullOrWhiteSpace(openRouterKey) && options.Providers.All(p => p.Type != "openrouter"))
                {
                    options.Providers.Insert(0, new OpenRouterProviderConfig
                    {
                        ApiKey = openRouterKey,
                        TEMP_ModelName = config["OPENROUTER_MODEL"] ?? OpenRouterProviderConfig.DefaultModel,
                        SupportedComplexities = JobComplexity.General | JobComplexity.CharacterThoughts,
                    });
                    options.Providers.Insert(1, new OpenRouterProviderConfig
                    {
                        ApiKey = openRouterKey,
                        TEMP_ModelName = config["OPENROUTER_VOICE_MODEL"] ?? OpenRouterProviderConfig.DefaultVoiceModel,
                        SupportedComplexities = JobComplexity.CharacterVoice,
                    });
                }

                AddDecisionProviders(options, config);
            });
        });
        return services;
    }

    private static void AddDecisionProviders(LlmOptions options, IConfiguration config)
    {
        void Add(string type, string baseUrl, string? apiKey, string model) => options.DecisionProviders.Add(new LlmProviderEntry
        {
            Type = type,
            BaseUrl = baseUrl,
            ApiKey = apiKey,
            ModelName = model,
            SupportedComplexities = JobComplexity.Decisions,
            Priority = options.DecisionProviders.Count,
        });

        if (config["TYPESAFE_API_KEY"] is { Length: > 0 } typesafe)
            Add("openrouter", HttpSystemOneJudge.TypeSafeBaseUrl, typesafe, config["TYPESAFE_MODEL"] ?? HttpSystemOneJudge.TypeSafeModel);
        if (config["OPENROUTER_API_KEY"] is { Length: > 0 } openRouter && config.GetValue("SYSTEM_ONE_OPENROUTER", true))
            Add("openrouter", new OpenRouterProviderConfig().BaseUrl, openRouter, config["SYSTEM_ONE_OPENROUTER_MODEL"] ?? HttpSystemOneJudge.OpenRouterModel);
    }
}
