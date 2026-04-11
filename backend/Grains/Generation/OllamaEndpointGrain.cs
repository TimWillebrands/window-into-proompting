using System.ClientModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using PartyTown.Configuration;
using PartyTown.Logging;

namespace PartyTown.Grains.Generation;

public class OllamaEndpointGrain(
    IOptions<LlmOptions> llmOptions,
    IHttpClientFactory httpClientFactory,
    ILogger<OllamaEndpointGrain> logger
) : Grain, IOllamaEndpointGrain
{
    private int _activeGenerations;

    public ValueTask<int> PressureAsync() => ValueTask.FromResult(_activeGenerations);

    private int GrainIndex => (int)this.GetPrimaryKeyLong();
    private OllamaProviderConfig Config => (OllamaProviderConfig)llmOptions.Value.Providers.ElementAt(GrainIndex);
    private string ProviderDescription => $"ollama[{Config.BaseUrl}]";

    private OpenAIClientOptions OpenAiOptions => new()
    {
        Endpoint = new Uri($"{Config.BaseUrl.TrimEnd('/')}/v1")
    };

    public IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(
        LlmGenerationJob parameters,
        CancellationToken cancellationToken = default)
    {
        var modelName = Config.TEMP_ModelName;
        Interlocked.Increment(ref _activeGenerations);

        try
        {
            using var _ = logger.BeginGenerationScope(modelName, ProviderDescription);
            logger.LogDebug("LLM API call starting: {Model}", modelName);
            var sw = Stopwatch.StartNew();

            var chatClient = new ChatClient(
                modelName,
                new ApiKeyCredential("ollama"),
                OpenAiOptions);

            return LlmEndpointGrainUtils.GenerateAsync(logger, parameters, chatClient, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _activeGenerations);
        }
    }

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Fetching models from Ollama at {BaseUrl}", Config.BaseUrl);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var http = httpClientFactory.CreateClient();
            var url = $"{Config.BaseUrl.TrimEnd('/')}/api/tags";
            var httpResponse = await http.GetAsync(url, cts.Token);
            httpResponse.EnsureSuccessStatusCode();

            var content = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("Ollama at {BaseUrl} returned empty response for /api/tags", Config.BaseUrl);
                return Array.Empty<LlmModel>();
            }

            var response = JsonSerializer.Deserialize<OllamaTagsResponse>(content);

            var list = (response?.Models ?? [])
                .Select(item => new LlmModel
                {
                    Name = item.Name,
                    EndpointProviderGrainId = GrainIndex,
                    ProviderType = "ollama",
                    ProviderDescription = ProviderDescription,
                    Description = $"Ollama model {item.Name}",
                    SupportedComplexities = JobComplexity.General,
                })
                .ToList();

            logger.LogDebug("Fetched {Count} models from Ollama", list.Count);
            return list;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OllamaEndpointGrain failed to list models from {BaseUrl}", Config.BaseUrl);
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
