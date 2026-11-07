using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Configuration;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Observability;
using PartyTown.Services.SystemOne;

namespace PartyTown.Grains.Generation;

public class OllamaEndpointGrain(
    IHttpClientFactory httpClientFactory,
    IAiObservabilityCapture aiObservability,
    Tailnets tailnets,
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

    public Task ApplyConfigAsync(LlmProviderEntry? entry)
    {
        _config = entry is { Type: "ollama" } && entry.Id == this.GetPrimaryKey() ? entry : null;
        return Task.CompletedTask;
    }

    public ValueTask<int> PressureAsync() => ValueTask.FromResult(_activeGenerations);

    public Task<ChatMessageMetadata> GetAttributionAsync()
        => Task.FromResult(new ChatMessageMetadata { Provider = "ollama", ModelName = ModelName });

    private string BaseUrl => _config?.BaseUrl ?? "http://localhost:11434";
    private string ModelName => _config?.ModelName ?? string.Empty;
    private string ProviderDescription => $"ollama[{BaseUrl}]";

    private OpenAIClientOptions OpenAiOptions
    {
        get
        {
            var options = new OpenAIClientOptions { Endpoint = new Uri($"{BaseUrl.TrimEnd('/')}/v1") };
            if (_config is { ReasoningEffort: not ReasoningEffort.Default } c) options.AddPolicy(new ReasoningPolicy(c.ReasoningEffort), PipelinePosition.PerCall);
            if (tailnets.ClientFor(_config?.Tailnet) is { } http) options.Transport = new HttpClientPipelineTransport(http);
            return options;
        }
    }

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

        return LlmEndpointGrainUtils.ReportingHardFailures(
            LlmEndpointGrainUtils.GenerateAsync(
                logger,
                parameters,
                chatClient,
                () => Interlocked.Decrement(ref _activeGenerations),
                aiObservability,
                "ollama",
                modelName,
                OpenAiOptions.Endpoint.ToString(),
                cancellationToken),
            ReportFailureAsync,
            cancellationToken);
    }

    public Task<string> CompleteOneShotAsync(
        LlmGenerationJob parameters,
        CancellationToken cancellationToken = default)
    {
        var modelName = ModelName;
        Interlocked.Increment(ref _activeGenerations);

        using var _ = logger.BeginGenerationScope(modelName, ProviderDescription);
        logger.LogDebug("LLM one-shot starting: {Model}", modelName);

        var chatClient = new ChatClient(
            modelName,
            new ApiKeyCredential("ollama"),
            OpenAiOptions);

        return CompleteReportingAsync(LlmEndpointGrainUtils.CompleteOneShotAsync(
            logger,
            parameters,
            chatClient,
            () => Interlocked.Decrement(ref _activeGenerations),
            aiObservability,
            "ollama",
            modelName,
            OpenAiOptions.Endpoint.ToString(),
            cancellationToken));
    }

    public async Task<string> JudgeAsync(string state, string questionsJson, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _activeGenerations);
        try
        {
            return await LlmEndpointGrainUtils.JudgeAsync(
                logger,
                tailnets.ClientFor(_config?.Tailnet) ?? httpClientFactory.CreateClient(HttpSystemOneJudge.ClientName),
                $"{BaseUrl.TrimEnd('/')}/v1/systemone",
                _config?.ApiKey,
                state,
                questionsJson,
                () => Interlocked.Decrement(ref _activeGenerations),
                aiObservability,
                "ollama",
                ModelName,
                BaseUrl,
                cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            await ReportFailureAsync(ex);
            throw;
        }
    }

    private async Task<string> CompleteReportingAsync(Task<string> call)
    {
        try
        {
            return await call;
        }
        catch (Exception ex) when (LlmEndpointGrainUtils.IsHardFailure(ex))
        {
            await ReportFailureAsync(ex);
            throw;
        }
    }

    // Pinged every 30s, so an unreachable box can be Offline instead of benched for 10 minutes.
    private Task ReportFailureAsync(Exception ex)
    {
        var router = GrainFactory.GetGrain<ILlmRouterGrain>(0);
        return LlmEndpointGrainUtils.IsUnreachable(ex)
            ? router.ReportOfflineAsync(this.GetPrimaryKey(), ex.Message)
            : router.ReportFailureAsync(this.GetPrimaryKey(), ex.Message);
    }

    /// <summary>Reachable when it lists its models; checks the server, not that the model is there.</summary>
    public async Task<string?> PingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var http = tailnets.ClientFor(_config?.Tailnet) ?? httpClientFactory.CreateClient();
            await ListModelNamesAsync(http, cancellationToken);
            return null;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return ex.Message;
        }
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching models from {BaseUrl}", BaseUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var http = tailnets.ClientFor(_config?.Tailnet) ?? httpClientFactory.CreateClient();
            var grainId = this.GetPrimaryKey();
            var complexity = _config?.SupportedComplexities ?? JobComplexity.General;

            var list = (await ListModelNamesAsync(http, cts.Token))
                .Select(name => new LlmModel
                {
                    Name = name,
                    EndpointProviderGrainId = grainId,
                    ProviderType = "ollama",
                    ProviderDescription = ProviderDescription,
                    Description = $"Local model {name}",
                    SupportedComplexities = complexity,
                })
                .ToList();

            logger.LogDebug("Fetched {Count} models from {BaseUrl}", list.Count, BaseUrl);
            return list;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaEndpointGrain failed to list models from {BaseUrl}", BaseUrl);
            return Array.Empty<LlmModel>();
        }
    }

    /// <summary>Ollama's own list, else the OpenAI one that llama.cpp and other local servers serve.</summary>
    private async Task<IReadOnlyList<string>> ListModelNamesAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var root = BaseUrl.TrimEnd('/');
        using var tags = await http.GetAsync($"{root}/api/tags", cancellationToken);
        if (tags.IsSuccessStatusCode)
        {
            var response = await tags.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken);
            return response?.Models.Select(m => m.Name).ToList() ?? new List<string>();
        }

        using var models = await http.GetAsync($"{root}/v1/models", cancellationToken);
        models.EnsureSuccessStatusCode();
        var openAi = await models.Content.ReadFromJsonAsync<OpenAiModelsResponse>(cancellationToken);
        return openAi?.Data.Select(m => m.Id).ToList() ?? new List<string>();
    }

    private sealed class OpenAiModelsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiModelEntry> Data { get; init; } = [];
    }

    private sealed class OpenAiModelEntry
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;
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
