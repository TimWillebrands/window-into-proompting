using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Logging;
using PartyTown.Model;

namespace PartyTown.Grains.Generation;

public class OpenRouterEndpointGrain(
    IHttpClientFactory httpClientFactory,
    ILogger<OpenRouterEndpointGrain> logger
) : Grain, IOpenRouterEndpointGrain
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
        // TODO: also filter by IsEnabled (see OllamaEndpointGrain.RefreshConfigAsync).
        _config = providers.FirstOrDefault(p => p.Id == this.GetPrimaryKey() && p.Type == "openrouter");
        if (_config is null)
            logger.LogWarning("OpenRouterEndpointGrain {Id}: no config found", this.GetPrimaryKey());
    }

    public ValueTask<int> PressureAsync() => ValueTask.FromResult(_activeGenerations);

    public Task<ChatMessageMetadata> GetAttributionAsync()
        => Task.FromResult(new ChatMessageMetadata { Provider = "openrouter", ModelName = ModelName });

    private string ApiKey => _config?.ApiKey ?? string.Empty;
    private string BaseUrl => _config?.BaseUrl ?? "https://openrouter.ai/api/v1";
    private string ModelName => _config?.ModelName ?? string.Empty;
    private const string ProviderDescription = "openrouter";

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
            new ApiKeyCredential(ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(BaseUrl) });

        return LlmEndpointGrainUtils.GenerateAsync(
            logger,
            parameters,
            chatClient,
            () => Interlocked.Decrement(ref _activeGenerations),
            cancellationToken);
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        const string url = "https://openrouter.ai/api/frontend/models/find?context=128000&fmt=cards&input_modalities=text&max_price=0&order=top-weekly";

        try
        {
            logger.LogDebug("Fetching models from OpenRouter");

            using var httpClient = httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var models = new List<LlmModel>();
            var grainId = this.GetPrimaryKey();
            var complexity = _config?.SupportedComplexities ?? JobComplexity.General;

            if (!document.RootElement.TryGetProperty("data", out var dataNode) ||
                !dataNode.TryGetProperty("models", out var modelsNode) ||
                modelsNode.ValueKind != JsonValueKind.Array)
            {
                return models;
            }

            foreach (var modelNode in modelsNode.EnumerateArray())
            {
                if (!modelNode.TryGetProperty("endpoint", out var endpointNode) ||
                    !endpointNode.TryGetProperty("model_variant_permaslug", out var idNode))
                {
                    continue;
                }

                var id = idNode.GetString();
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var name = modelNode.TryGetProperty("short_name", out var nameNode)
                    ? nameNode.GetString() ?? id
                    : id;

                var description = modelNode.TryGetProperty("description", out var descNode)
                    ? descNode.GetString()
                    : null;

                int? contextLength = null;
                if (modelNode.TryGetProperty("context_length", out var contextNode) && contextNode.TryGetInt32(out var ctx))
                    contextLength = ctx;

                models.Add(new LlmModel
                {
                    Name = id,
                    EndpointProviderGrainId = grainId,
                    ProviderType = "openrouter",
                    ProviderDescription = ProviderDescription,
                    Description = description ?? $"OpenRouter model {name}",
                    ContextLength = contextLength,
                    SupportedComplexities = complexity,
                });
            }

            logger.LogDebug("Fetched {Count} models from OpenRouter", models.Count);
            return models;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenRouterEndpointGrain failed to list models");
            return Array.Empty<LlmModel>();
        }
    }
}
