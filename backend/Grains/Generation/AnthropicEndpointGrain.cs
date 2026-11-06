using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using PartyTown.Logging;
using PartyTown.Model;

namespace PartyTown.Grains.Generation;

/// <summary>The model declined; retrying the same prompt on it won't help.</summary>
public sealed class LlmRefusalException(string? category) : Exception($"Claude declined this request ({category ?? "no category"})");

/// <summary>Claude over the official SDK. One grain per provider entry.</summary>
public class AnthropicEndpointGrain(ILogger<AnthropicEndpointGrain> logger) : Grain, IAnthropicEndpointGrain
{
    private const string ProviderType = "anthropic";

    private int _activeGenerations;
    private LlmProviderEntry? _config;
    private AnthropicClient? _client;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        var providers = await GrainFactory.GetGrain<ILlmProviderConfigGrain>(0).GetProvidersAsync();
        _config = providers.FirstOrDefault(p => p.Id == this.GetPrimaryKey() && p.Type == ProviderType);
        if (_config is null)
            logger.LogWarning("AnthropicEndpointGrain {Id}: no config found", this.GetPrimaryKey());
    }

    public Task ApplyConfigAsync(LlmProviderEntry? entry)
    {
        _config = entry is { Type: ProviderType } && entry.Id == this.GetPrimaryKey() ? entry : null;
        _client = null;
        return Task.CompletedTask;
    }

    public ValueTask<int> PressureAsync() => ValueTask.FromResult(_activeGenerations);

    public Task<ChatMessageMetadata> GetAttributionAsync()
        => Task.FromResult(new ChatMessageMetadata { Provider = ProviderType, ModelName = Model });

    public Task<string> JudgeAsync(string state, string questionsJson, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Claude is not a System One judge.");

    private string Model => _config?.ModelName ?? string.Empty;

    private string? Effort => _config?.ReasoningEffort switch
    {
        ReasoningEffort.Low => "low",
        ReasoningEffort.Medium => "medium",
        ReasoningEffort.High => "high",
        _ => null,
    };

    private AnthropicClient Client => _client ??= new AnthropicClient
    {
        ApiKey = _config?.ApiKey ?? string.Empty,
        BaseUrl = _config?.BaseUrl ?? AnthropicRequest.DefaultBaseUrl,
    };

    public IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(LlmGenerationJob parameters, CancellationToken cancellationToken = default)
    {
        var model = Model;
        var request = AnthropicRequest.Build(parameters, model, Effort, AnthropicRequest.StreamMaxTokens);
        Interlocked.Increment(ref _activeGenerations);

        using var _ = logger.BeginGenerationScope(model, ProviderType);
        return LlmEndpointGrainUtils.ReportingHardFailures(StreamAsync(request, cancellationToken), ReportFailureAsync, cancellationToken);
    }

    private async IAsyncEnumerable<LlmGenerationEvent> StreamAsync(
        MessageCreateParams request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await foreach (var streamEvent in Client.Beta.Messages.CreateStreaming(request, cancellationToken))
            {
                if (streamEvent.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var text))
                {
                    if (!string.IsNullOrEmpty(text.Text))
                        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, text.Text);
                }
                else if (streamEvent.TryPickDelta(out var delta) && StopProblem(delta.Delta.StopReason?.Raw()) is { } problem)
                {
                    logger.LogWarning("Claude stopped early: {Reason}", problem);
                    yield return new LlmGenerationEvent(LlmGenerationEvent.GenerationError, problem);
                    yield break;
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeGenerations);
            logger.LogInformation("LLM API call completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }

    public async Task<string> CompleteOneShotAsync(LlmGenerationJob parameters, CancellationToken cancellationToken = default)
    {
        var model = Model;
        var request = AnthropicRequest.Build(parameters, model, Effort, AnthropicRequest.OneShotMaxTokens);
        Interlocked.Increment(ref _activeGenerations);

        using var _ = logger.BeginGenerationScope(model, ProviderType);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await Client.Beta.Messages.Create(request, cancellationToken);
            if (response.StopReason?.Raw() == "refusal")
                throw new LlmRefusalException(response.StopDetails?.Category?.Raw());
            if (StopProblem(response.StopReason?.Raw()) is { } problem)
                logger.LogWarning("Claude one-shot stopped early: {Reason}", problem);

            var text = new StringBuilder();
            foreach (var block in response.Content)
                if (block.TryPickText(out var part)) text.Append(part.Text);
            return text.ToString();
        }
        catch (Exception ex) when (LlmEndpointGrainUtils.IsHardFailure(ex))
        {
            await ReportFailureAsync(ex);
            throw Readable(ex);
        }
        catch (AnthropicServiceException ex)
        {
            throw Readable(ex);
        }
        finally
        {
            Interlocked.Decrement(ref _activeGenerations);
            logger.LogInformation("LLM one-shot completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }

    /// <summary>Stop reasons the caller must hear about; a null means the turn finished normally.</summary>
    private static string? StopProblem(string? reason) => reason is "refusal" or "max_tokens" ? reason : null;

    private Task ReportFailureAsync(Exception ex)
        => GrainFactory.GetGrain<ILlmRouterGrain>(0).ReportFailureAsync(this.GetPrimaryKey(), Readable(ex).Message);

    /// <summary>SDK messages are bare status codes and its types don't cross the grain boundary; keep the API's own words.</summary>
    private static Exception Readable(Exception ex)
        => ex is AnthropicServiceException { ApiMessage: { Length: > 0 } message } ? new InvalidOperationException($"Claude: {message}") : ex;

    public async Task<IReadOnlyList<LlmModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var page = await Client.Models.List(new Anthropic.Models.Models.ModelListParams { Limit = 1000 }, cancellationToken);
            return page.Items.Select(m => new LlmModel
            {
                Name = m.ID,
                EndpointProviderGrainId = this.GetPrimaryKey(),
                ProviderType = ProviderType,
                ProviderDescription = ProviderType,
                Description = m.DisplayName,
                SupportedComplexities = _config?.SupportedComplexities ?? JobComplexity.General,
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AnthropicEndpointGrain failed to list models");
            return Array.Empty<LlmModel>();
        }
    }
}
