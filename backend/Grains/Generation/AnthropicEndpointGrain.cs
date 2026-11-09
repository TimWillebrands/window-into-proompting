using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using PartyTown.Logging;
using PartyTown.Model;
using PartyTown.Observability;

namespace PartyTown.Grains.Generation;

/// <summary>The model declined; retrying the same prompt on it won't help.</summary>
public sealed class LlmRefusalException(string? category) : Exception($"Claude declined this request ({category ?? "no category"})");

/// <summary>Claude over the official SDK. One grain per provider entry.</summary>
public class AnthropicEndpointGrain(ILogger<AnthropicEndpointGrain> logger, IAiObservabilityCapture aiObservability)
    : Grain, IAnthropicEndpointGrain
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

    /// <summary>Cloud providers aren't pinged; a failed call benches them instead.</summary>
    public Task<string?> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);

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
        BaseUrl = BaseUrl,
    };

    public IAsyncEnumerable<LlmGenerationEvent> GenerateAsync(LlmGenerationJob parameters, CancellationToken cancellationToken = default)
    {
        var model = Model;
        var request = AnthropicRequest.Build(parameters, model, Effort, AnthropicRequest.StreamMaxTokens);
        Interlocked.Increment(ref _activeGenerations);

        using var _ = logger.BeginGenerationScope(model, ProviderType);
        return LlmEndpointGrainUtils.ReportingHardFailures(StreamAsync(parameters, request, cancellationToken), ReportFailureAsync, cancellationToken);
    }

    private async IAsyncEnumerable<LlmGenerationEvent> StreamAsync(
        LlmGenerationJob job, MessageCreateParams request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var content = new StringBuilder();
        var model = Model;
        TimeSpan? firstToken = null;
        LlmUsage? usage = null;
        Exception? failure = null;
        string? stopProblem = null;
        var completed = false;
        try
        {
            await using var events = Client.Beta.Messages.CreateStreaming(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                // A catch can't wrap a yield, so failures are caught around the read alone.
                try
                {
                    if (!await events.MoveNextAsync()) break;
                }
                catch (Exception ex)
                {
                    failure = Readable(ex);
                    throw;
                }
                var streamEvent = events.Current;
                if (streamEvent.TryPickStart(out var start))
                {
                    // A server-side fallback can answer with another model than the one asked for.
                    model = start.Message.Model.Raw() ?? model;
                    usage = UsageOf(start.Message.Usage);
                }
                else if (streamEvent.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var text))
                {
                    if (!string.IsNullOrEmpty(text.Text))
                    {
                        firstToken ??= sw.Elapsed;
                        content.Append(text.Text);
                        yield return new LlmGenerationEvent(LlmGenerationEvent.ContentChunk, text.Text);
                    }
                }
                else if (streamEvent.TryPickDelta(out var delta))
                {
                    usage = (usage ?? new LlmUsage(null, null)) with { Output = delta.Usage.OutputTokens };
                    if (StopProblem(delta.Delta.StopReason?.Raw()) is { } problem)
                    {
                        logger.LogWarning("Claude stopped early: {Reason}", problem);
                        stopProblem = problem;
                        yield return new LlmGenerationEvent(LlmGenerationEvent.GenerationError, problem);
                        yield break;
                    }
                }
            }
            completed = true;
        }
        finally
        {
            Interlocked.Decrement(ref _activeGenerations);
            logger.LogInformation("LLM API call completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            var generation = new LlmGeneration(job.Messages, content.ToString(), ProviderType, model, BaseUrl, Streamed: true)
            {
                Tier = job.JobComplexity.ToString(),
                Latency = sw.Elapsed,
                TimeToFirstToken = firstToken,
                Usage = usage,
            };
            generation = stopProblem is not null
                ? generation with { Error = $"stop reason: {stopProblem}" }
                : generation.Ended(failure, completed, cancellationToken);
            await aiObservability.CaptureGenerationAsync(generation, CancellationToken.None);
        }
    }

    private string BaseUrl => _config?.BaseUrl ?? AnthropicRequest.DefaultBaseUrl;

    private static LlmUsage UsageOf(BetaUsage usage) => new(
        usage.InputTokens, usage.OutputTokens, usage.CacheReadInputTokens, usage.CacheCreationInputTokens);

    public async Task<string> CompleteOneShotAsync(LlmGenerationJob parameters, CancellationToken cancellationToken = default)
    {
        var model = Model;
        var request = AnthropicRequest.Build(parameters, model, Effort, AnthropicRequest.OneShotMaxTokens);
        Interlocked.Increment(ref _activeGenerations);

        using var _ = logger.BeginGenerationScope(model, ProviderType);
        var sw = Stopwatch.StartNew();
        var text = new StringBuilder();
        LlmUsage? usage = null;
        Exception? failure = null;
        var completed = false;
        try
        {
            var response = await Client.Beta.Messages.Create(request, cancellationToken);
            model = response.Model.Raw() ?? model;
            usage = UsageOf(response.Usage);
            if (response.StopReason?.Raw() == "refusal")
                throw new LlmRefusalException(response.StopDetails?.Category?.Raw());
            if (StopProblem(response.StopReason?.Raw()) is { } problem)
                logger.LogWarning("Claude one-shot stopped early: {Reason}", problem);

            foreach (var block in response.Content)
                if (block.TryPickText(out var part)) text.Append(part.Text);
            completed = true;
            return text.ToString();
        }
        catch (Exception ex) when (LlmEndpointGrainUtils.Benches(ex))
        {
            failure = Readable(ex);
            await ReportFailureAsync(ex);
            throw failure;
        }
        catch (Exception ex)
        {
            failure = Readable(ex);
            if (ex is AnthropicServiceException) throw failure;
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _activeGenerations);
            logger.LogInformation("LLM one-shot completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            var generation = new LlmGeneration(parameters.Messages, text.ToString(), ProviderType, model, BaseUrl, Streamed: false)
            {
                Tier = parameters.JobComplexity.ToString(),
                Latency = sw.Elapsed,
                Usage = usage,
            };
            await aiObservability.CaptureGenerationAsync(generation.Ended(failure, completed, cancellationToken), CancellationToken.None);
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
