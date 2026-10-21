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
    /// <summary>Whether beat-path weighing should go through this judge (Jev: always; LLM emulation: opt-in).</summary>
    bool Enabled { get; }

    Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct);
}

public abstract record SystemOneQuestion(string Instructions);

/// <summary>Yes/no statement → probability it is true.</summary>
public sealed record NoulQuestion(string Instructions) : SystemOneQuestion(Instructions);

/// <summary>Pick one option → probability per option. Keys are option names, values optional descriptions.</summary>
public sealed record ChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string?> Options) : SystemOneQuestion(Instructions);

public sealed class SystemOneAnswers
{
    public Dictionary<string, double> Nouls { get; } = new();
    public Dictionary<string, Dictionary<string, double>> Choices { get; } = new();

    public double Noul(string key, double fallback) => Nouls.TryGetValue(key, out var p) ? p : fallback;

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
/// Any <c>/v1/systemone</c> server: TypeSafe Jev, Jev via OpenRouter, or a local decision model
/// (Cloudflare Clef via llama.cpp). Same wire format everywhere.
/// </summary>
public sealed class HttpSystemOneJudge(HttpClient http, string name, string url, string? apiKey, string model, ILogger<HttpSystemOneJudge> logger)
    : ISystemOneJudge
{
    public const string TypeSafeUrl = "https://api.typesafe.ai/v1/systemone";
    public const string TypeSafeModel = "jev-latest";
    public const string OpenRouterUrl = "https://openrouter.ai/api/v1/systemone";
    public const string OpenRouterModel = "~typesafe/jev-latest";

    public string Name => name;

    public bool Enabled => true;

    public async Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(BuildRequest(model, state, questions).ToJsonString(), Encoding.UTF8, "application/json"),
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
        return ParseResponse(body);
    }

    public static JsonObject BuildRequest(string model, string state, IReadOnlyDictionary<string, SystemOneQuestion> questions)
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
        return new JsonObject { ["model"] = model, ["state"] = state, ["questions"] = qs };
    }

    public static SystemOneAnswers ParseResponse(string body)
    {
        var answers = new SystemOneAnswers();
        using var doc = JsonDocument.Parse(body);
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

/// <summary>Tries judges in order (local first); a failing judge sits out for a while instead of costing latency every beat.</summary>
public sealed class FailoverJudge(IReadOnlyList<ISystemOneJudge> judges, ILogger<FailoverJudge> logger) : ISystemOneJudge
{
    private static readonly TimeSpan Bench = TimeSpan.FromMinutes(5);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTimeOffset> _benchedUntil = new();

    public bool Enabled => judges.Any(j => j.Enabled);

    public async Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
    {
        Exception? last = null;
        for (var i = 0; i < judges.Count; i++)
        {
            if (!judges[i].Enabled) continue;
            if (_benchedUntil.TryGetValue(i, out var until) && until > DateTimeOffset.UtcNow && i < judges.Count - 1) continue;
            try
            {
                var answers = await judges[i].AskAsync(state, questions, ct);
                _benchedUntil.TryRemove(i, out _);
                return answers;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                last = ex;
                _benchedUntil[i] = DateTimeOffset.UtcNow + Bench;
                logger.LogWarning(ex, "System One judge #{Index} failed; benched for {Minutes} min", i, Bench.TotalMinutes);
            }
        }
        throw last ?? new InvalidOperationException("No System One judge configured.");
    }
}

/// <summary>System One emulated by one General-tier LLM call asking for probabilities. Less calibrated, same contract.</summary>
public sealed class LlmJudge(IGrainFactory grains, IConfiguration config, ILogger<LlmJudge> logger) : ISystemOneJudge
{
    public bool Enabled => config.GetValue<bool>("SYSTEM_ONE_EMULATE");

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
        var endpoint = await router.RouteAsync(JobComplexity.General, ct);
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
