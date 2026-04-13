using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Logging;

namespace PartyTown.Grains.Generation;

public class OllamaEndpointGrain(
    IHttpClientFactory httpClientFactory,
    ILogger<OllamaEndpointGrain> logger
) : Grain, IOllamaEndpointGrain
{
    private int _activeGenerations;
    private LlmProviderEntry? _config;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        await RefreshConfigAsync();
    }

    private async Task RefreshConfigAsync()
    {
        var configGrain = GrainFactory.GetGrain<ILlmProviderConfigGrain>(0);
        var providers = await configGrain.GetProvidersAsync();
        // TODO: also filter by IsEnabled so disabled providers keep _config null.
        // Router (LlmRouterGrain) already filters disabled, but LlmConfigController.GetProviderModels bypasses the router.
        _config = providers.FirstOrDefault(p => p.Id == this.GetPrimaryKey() && p.Type == "ollama");
        if (_config is null)
            logger.LogWarning("OllamaEndpointGrain {Id}: no config found", this.GetPrimaryKey());
    }

    public ValueTask<int> PressureAsync() => ValueTask.FromResult(_activeGenerations);

    private string BaseUrl => _config?.BaseUrl ?? "http://localhost:11434";
    private string ModelName => _config?.ModelName ?? string.Empty;
    private string ProviderDescription => $"ollama[{BaseUrl}]";

    private OpenAIClientOptions OpenAiOptions => new()
    {
        Endpoint = new Uri($"{BaseUrl.TrimEnd('/')}/v1")
    };

    public IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationJob parameters,
        CancellationToken cancellationToken = default)
    {
        var modelName = ModelName;
        Interlocked.Increment(ref _activeGenerations);

        using var _ = logger.BeginGenerationScope(modelName, ProviderDescription);
        logger.LogDebug("LLM API call starting: {Model}", modelName);

        var chatClient = new ChatClient(
            modelName,
            new ApiKeyCredential("ollama"),
            OpenAiOptions);

        return LlmEndpointGrainUtils.GenerateAsync(
            logger,
            parameters,
            chatClient,
            () => Interlocked.Decrement(ref _activeGenerations),
            cancellationToken);
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching models from Ollama at {BaseUrl}", BaseUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var http = httpClientFactory.CreateClient();
            var url = $"{BaseUrl.TrimEnd('/')}/api/tags";
            var httpResponse = await http.GetAsync(url, cts.Token);
            httpResponse.EnsureSuccessStatusCode();

            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("Ollama at {BaseUrl} returned empty response for /api/tags", BaseUrl);
                return Array.Empty<LlmModel>();
            }

            var response = JsonSerializer.Deserialize<OllamaTagsResponse>(content);
            var grainId = this.GetPrimaryKey();
            var complexity = _config?.SupportedComplexities ?? JobComplexity.General;

            var list = (response?.Models ?? [])
                .Select(item => new LlmModel
                {
                    Name = item.Name,
                    EndpointProviderGrainId = grainId,
                    ProviderType = "ollama",
                    ProviderDescription = ProviderDescription,
                    Description = $"Ollama model {item.Name}",
                    SupportedComplexities = complexity,
                })
                .ToList();

            logger.LogDebug("Fetched {Count} models from Ollama", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaEndpointGrain failed to list models from {BaseUrl}", BaseUrl);
            return Array.Empty<LlmModel>();
        }
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public List<OllamaModelEntry> Models { get; init; } = [];
    }

    private sealed class OllamaModelEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }
}
