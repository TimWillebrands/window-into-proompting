using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PartyTown.Grains.Generation;
using PartyTown.Services.Memory;

namespace PartyTown.Services.SystemOne;

/// <summary>
/// System One port (ADR 0019): fast, calibrated judgements over a state — typed questions in,
/// probabilities out, no prose. The relevance-realization calls that only need to *weigh*
/// (which affordance, is this memorable) go here; generation stays with the LLM (System Two).
/// </summary>
public interface ISystemOneJudge
{
    Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct);
}

public static class SystemOneJudgeExtensions
{
    /// <summary>The judge for a call about one Persona: their Decisions Route first, then the pool (ADR 0034).</summary>
    public static ISystemOneJudge For(this ISystemOneJudge judge, Guid persona)
        => judge is ProviderJudge routed ? routed.ForPersona(persona) : judge;
}

public abstract record SystemOneQuestion(string Instructions);

/// <summary>Yes/no statement → probability it is true.</summary>
public sealed record NoulQuestion(string Instructions) : SystemOneQuestion(Instructions);

/// <summary>Pick one option → probability per option. Keys are option names, values optional descriptions.</summary>
public sealed record ChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string?> Options) : SystemOneQuestion(Instructions);

public sealed class SystemOneAnswers
{
    /// <summary>The judge that answered, when it says so: its numbers are only comparable with its own.</summary>
    public string? Model { get; set; }
    public Dictionary<string, double> Nouls { get; } = new();
    public Dictionary<string, Dictionary<string, double>> Choices { get; } = new();

    public double Noul(string key, double fallback) => Nouls.TryGetValue(key, out var p) ? p : fallback;

    /// <summary>Scales each option's probability by <paramref name="left"/> (0..1) before the draw: what habit or a second thought takes off it.</summary>
    public void Damp(string key, Func<string, double> left)
    {
        if (!Choices.TryGetValue(key, out var probs)) return;
        foreach (var option in probs.Keys.ToList()) probs[option] *= Math.Clamp(left(option), 0, 1);
    }

    /// <summary>Sample a choice from its distribution sharpened by <paramref name="temperature"/> (&lt;1 = greedier).</summary>
    public string? SampleChoice(string key, double temperature = 1.0, Random? random = null)
    {
        if (!Choices.TryGetValue(key, out var probs) || probs.Count == 0) return null;
        var t = Math.Max(0.05, temperature);
        var weights = probs.ToDictionary(kv => kv.Key, kv => Math.Pow(Math.Max(kv.Value, 1e-6), 1 / t));
        var roll = (random ?? Random.Shared).NextDouble() * weights.Values.Sum();
        foreach (var (option, w) in weights)
        {
            roll -= w;
            if (roll <= 0) return option;
        }
        return weights.Keys.Last();
    }
}

/// <summary>
/// The <c>/v1/systemone</c> wire format, served by TypeSafe Jev, Jev via OpenRouter, or a local decision
/// model (Cloudflare Clef via llama.cpp). Providers call it through <see cref="ProviderJudge"/>.
/// </summary>
public sealed class HttpSystemOneJudge(HttpClient http, string name, string url, string? apiKey, string model, ILogger<HttpSystemOneJudge> logger)
    : ISystemOneJudge
{
    public const string ClientName = "systemone";
    public const string TypeSafeBaseUrl = "https://api.typesafe.ai/v1";
    public const string TypeSafeModel = "jev-latest";
    public const string OpenRouterUrl = "https://openrouter.ai/api/v1/systemone";
    public const string OpenRouterModel = "~typesafe/jev-latest";


    public async Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
        => ParseResponse(await PostAsync(http, url, apiKey, BuildRequest(model, state, questions), name, logger, ct));

    public static async Task<string> PostAsync(HttpClient http, string url, string? apiKey, JsonObject request, string name, ILogger logger, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var res = await http.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            logger.LogWarning("System One {Judge} {Status}: {Body}", name, (int)res.StatusCode, LlmJson.LogPreview(body));
            res.EnsureSuccessStatusCode();
        }
        logger.LogInformation("System One {Judge} answered in {Ms:F0}ms",
            name, System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return body;
    }

    public static JsonObject BuildRequest(string model, string state, IReadOnlyDictionary<string, SystemOneQuestion> questions)
        => BuildRequest(model, state, BuildQuestions(questions));

    public static JsonObject BuildRequest(string model, string state, JsonNode questions)
        => new() { ["model"] = model, ["state"] = state, ["questions"] = questions };

    public static JsonObject BuildQuestions(IReadOnlyDictionary<string, SystemOneQuestion> questions)
    {
        var qs = new JsonObject();
        foreach (var (key, q) in questions)
        {
            qs[key] = q switch
            {
                NoulQuestion n => new JsonObject { ["type"] = "noul", ["instructions"] = n.Instructions },
                ChoiceQuestion c => new JsonObject
                {
                    ["type"] = "choice",
                    ["instructions"] = c.Instructions,
                    ["criteria"] = new JsonObject(c.Options.Select(o =>
                        new KeyValuePair<string, JsonNode?>(o.Key, o.Value is null ? null : JsonValue.Create(o.Value)))),
                },
                _ => throw new NotSupportedException(q.GetType().Name),
            };
        }
        return qs;
    }

    public static SystemOneAnswers ParseResponse(string body)
    {
        var answers = new SystemOneAnswers();
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String) answers.Model = model.GetString();
        if (!doc.RootElement.TryGetProperty("answers", out var all)) return answers;
        foreach (var a in all.EnumerateObject())
        {
            var type = a.Value.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type == "noul" && a.Value.TryGetProperty("noul", out var p))
                answers.Nouls[a.Name] = p.GetDouble();
            else if (type == "choice" && a.Value.TryGetProperty("probabilities", out var probs))
                answers.Choices[a.Name] = probs.EnumerateObject().ToDictionary(o => o.Name, o => o.Value.GetDouble());
        }
        return answers;
    }
}

/// <summary>System One on the provider layer (ADR 0034): the Decisions tier routes, benches and falls through like any other.</summary>
public sealed class ProviderJudge(IGrainFactory grains, LlmJudge emulation, ILogger<ProviderJudge> logger)
    : BackgroundService, ISystemOneJudge
{
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromSeconds(30);
    private volatile bool _hasProviders;

    public ISystemOneJudge ForPersona(Guid persona) => new PersonaJudge(this, persona);

    private sealed class PersonaJudge(ProviderJudge judge, Guid persona) : ISystemOneJudge
    {
        public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
            => judge.AskAsync(persona, state, questions, ct);
    }

    /// <summary>Knows its providers before the world starts, so the first beat routes right.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await RefreshAsync();
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshEvery);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await RefreshAsync();
        }
        catch (OperationCanceledException) { }
    }

    public async Task RefreshAsync()
    {
        try
        {
            var config = grains.GetGrain<ILlmProviderConfigGrain>(0);
            var providers = await config.GetProvidersAsync();
            _hasProviders = providers.Any(p => p.IsEnabled && !p.Reserved && p.SupportedComplexities.HasFlag(JobComplexity.Decisions));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't read providers for System One; keeping the last answer");
        }
    }

    public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
        => AskAsync(null, state, questions, ct);

    private async Task<SystemOneAnswers> AskAsync(Guid? persona, string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var endpoint = persona is { } id ? await router.AssignedEndpointAsync(id, JobComplexity.Decisions, ct) : null;
        if (endpoint is null && !_hasProviders) return await emulation.AskAsync(state, questions, ct);

        var questionsJson = HttpSystemOneJudge.BuildQuestions(questions).ToJsonString();
        var tried = new HashSet<ILlmEndpointGrain>();
        Exception? last = null;
        while (true)
        {
            // A failing endpoint benches itself, so the next route finds another; the same one again means none is left.
            endpoint ??= _hasProviders ? await TryRouteAsync(router, persona, ct) : null;
            if (endpoint is null || !tried.Add(endpoint)) break;
            try
            {
                return HttpSystemOneJudge.ParseResponse(await endpoint.JudgeAsync(state, questionsJson, ct));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                last = ex;
                endpoint = null;
            }
        }

        // Every decision model failed: a General-tier LLM still beats a failed beat (ADR 0040).
        logger.LogWarning(last, "No decision model answered; emulating System One with an LLM");
        return await emulation.AskAsync(state, questions, ct);
    }

    private static async Task<ILlmEndpointGrain?> TryRouteAsync(ILlmRouterGrain router, Guid? persona, CancellationToken ct)
    {
        try { return await router.RouteAsync(JobComplexity.Decisions, persona, ct); }
        catch (InvalidOperationException) { return null; }
    }
}

/// <summary>System One emulated by one General-tier LLM call asking for probabilities. Less calibrated, same contract.</summary>
public sealed class LlmJudge(IGrainFactory grains, ILogger<LlmJudge> logger) : ISystemOneJudge
{
    public async Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var (key, q) in questions)
        {
            switch (q)
            {
                case NoulQuestion n:
                    sb.AppendLine($"- \"{key}\" (true/false): {n.Instructions}\n  answer: probability 0..1 that it is true");
                    break;
                case ChoiceQuestion c:
                    sb.AppendLine($"- \"{key}\" (choice): {c.Instructions}");
                    foreach (var (opt, desc) in c.Options)
                        sb.AppendLine($"    \"{opt}\"{(desc is null ? "" : $": {desc}")}");
                    sb.AppendLine("  answer: object mapping EVERY option name to a probability, summing to 1");
                    break;
            }
        }

        var system = """
You are a fast intuitive judge. Read the state, answer each question with calibrated probabilities. No explanations.
Output ONLY a JSON object: {"<question key>": <number or object of option probabilities>, ...}
""";
        var user = $"# State\n{state}\n\n# Questions\n{sb}";
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var endpoint = await router.RouteAsync(JobComplexity.General, cancellationToken: ct);
        var raw = await endpoint.CompleteOneShotAsync(new LlmGenerationJob
        {
            Messages = new List<LlmChatMessage>
            {
                new() { Role = "system", Content = system },
                new() { Role = "user", Content = user },
            },
            JobComplexity = JobComplexity.General,
            ModelParameters = new LlmModelParameters { Temperature = 0.2 },
        }, ct);

        var answers = new SystemOneAnswers();
        var json = LlmJson.TryRepairObject(raw ?? string.Empty);
        if (json is null || JsonNode.Parse(json) is not JsonObject root)
        {
            logger.LogWarning("LlmJudge unparseable: {Preview}", LlmJson.LogPreview(raw));
            return answers;
        }

        foreach (var (key, q) in questions)
        {
            var node = root[key];
            if (q is NoulQuestion && TryNumber(node, out var p))
                answers.Nouls[key] = Math.Clamp(p, 0, 1);
            else if (q is ChoiceQuestion c && node is JsonObject probs)
            {
                var dist = c.Options.Keys.ToDictionary(o => o, o => TryNumber(probs[o], out var v) ? Math.Max(0, v) : 0);
                var sum = dist.Values.Sum();
                if (sum > 0) answers.Choices[key] = dist.ToDictionary(kv => kv.Key, kv => kv.Value / sum);
            }
        }
        return answers;
    }

    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is not JsonValue v) return false;
        if (v.TryGetValue(out double d)) { value = d; return true; }
        return v.TryGetValue(out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
