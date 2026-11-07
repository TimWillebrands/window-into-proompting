using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PartyTown.Configuration;

namespace PartyTown.World.Work.Works;

public sealed record RunnerModel(string Id, string Label, double In, double Out);

public sealed record RunnerStatus(bool Busy, double? ReserveUsd, List<RunnerModel> Models);

public sealed record RunnerDiff(int Files, int Added, int Removed, List<string> Paths);

public sealed record RunnerCheck(string Name, bool Passed, int Seconds, string Detail);

public sealed record RunnerRun(
    string Id, string Status, double CapUsd, double Usd, long Tokens, string? Report, RunnerDiff? Diff,
    string? Stop, string? Error, int? Proposition, List<RunnerCheck> Checks);

public sealed record RunnerLine(long N, long At, string Kind, string Text);

public sealed record RunnerRead(List<RunnerLine> Events, RunnerRun Run);

public sealed record RunnerProposition(int N, string Commission, string Title, string Branch, string Status, string? Note);

/// <summary>Refused by the Runner, or the Runner couldn't be reached; only a refusal carries a status.</summary>
public sealed class RunnerException(string message, int? status = null) : Exception(message)
{
    /// <summary>The Runner answered no; anything else may have landed and is worth asking again.</summary>
    public bool Refused => status is >= 400 and < 500;
}

/// <summary>The World's one door to the sandbox host; the agent itself is never reached directly (ADR 0039).</summary>
public interface IRunner
{
    bool Enabled { get; }
    Task<RunnerStatus> StatusAsync(CancellationToken ct);
    Task<RunnerRun> StartAsync(Guid commission, string kind, string prompt, string model, double capUsd, CancellationToken ct);
    Task<RunnerRead> ReadAsync(string runId, long after, CancellationToken ct);
    Task<RunnerRun> PromptAsync(string runId, string text, double addUsd, CancellationToken ct);
    Task<RunnerRun> ProposeAsync(string runId, string title, string why, string author, CancellationToken ct);
    Task<RunnerRun> DropAsync(string runId, CancellationToken ct);
    Task<List<RunnerProposition>> PropositionsAsync(CancellationToken ct);
    Task RejectAsync(int proposition, string? note, CancellationToken ct);
    /// <summary>Ready for <c>git am</c>.</summary>
    Task<string> PatchAsync(int proposition, CancellationToken ct);
}

/// <summary>Works:RunnerUrl, Works:Token and optionally Works:Tailnet, as a provider reaches its machine (ADR 0033).</summary>
public sealed class HttpRunner(IConfiguration config, Tailnets tailnets) : IRunner
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private static readonly HttpClient Direct = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string? _url = config["Works:RunnerUrl"]?.TrimEnd('/');
    private readonly string? _token = config["Works:Token"];
    private readonly string? _tailnet = config["Works:Tailnet"];

    public bool Enabled => !string.IsNullOrWhiteSpace(_url);

    public Task<RunnerStatus> StatusAsync(CancellationToken ct) => SendAsync<RunnerStatus>(HttpMethod.Get, "/status", null, ct);

    public Task<RunnerRun> StartAsync(Guid commission, string kind, string prompt, string model, double capUsd, CancellationToken ct)
        => SendAsync<RunnerRun>(HttpMethod.Post, "/runs", new { commission, kind, prompt, model, capUsd }, ct);

    public Task<RunnerRead> ReadAsync(string runId, long after, CancellationToken ct)
        => SendAsync<RunnerRead>(HttpMethod.Get, $"/runs/{runId}/events?after={after}", null, ct);

    public Task<RunnerRun> PromptAsync(string runId, string text, double addUsd, CancellationToken ct)
        => SendAsync<RunnerRun>(HttpMethod.Post, $"/runs/{runId}/prompt", new { text, addUsd }, ct);

    public Task<RunnerRun> ProposeAsync(string runId, string title, string why, string author, CancellationToken ct)
        => SendAsync<RunnerRun>(HttpMethod.Post, $"/runs/{runId}/propose", new { title, why, author }, ct);

    public Task<RunnerRun> DropAsync(string runId, CancellationToken ct) => SendAsync<RunnerRun>(HttpMethod.Post, $"/runs/{runId}/drop", new { }, ct);

    public Task<List<RunnerProposition>> PropositionsAsync(CancellationToken ct) => SendAsync<List<RunnerProposition>>(HttpMethod.Get, "/propositions", null, ct);

    public Task RejectAsync(int proposition, string? note, CancellationToken ct)
        => SendAsync<JsonElement>(HttpMethod.Post, $"/propositions/{proposition}/reject", new { note }, ct);

    public async Task<string> PatchAsync(int proposition, CancellationToken ct)
    {
        using var response = await CallAsync(HttpMethod.Get, $"/propositions/{proposition}/patch", null, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await CallAsync(method, path, body, ct);
        return await response.Content.ReadFromJsonAsync<T>(Web, ct) ?? throw new RunnerException("The Runner sent nothing.");
    }

    private async Task<HttpResponseMessage> CallAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Enabled) throw new RunnerException("No Runner is configured.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        using var request = new HttpRequestMessage(method, _url + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Web);
        try
        {
            var response = await (tailnets.ClientFor(_tailnet) ?? Direct).SendAsync(request, budget.Token);
            if (response.IsSuccessStatusCode) return response;
            var said = await response.Content.ReadAsStringAsync(budget.Token);
            response.Dispose();
            throw new RunnerException($"{(int)response.StatusCode}: {said}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new RunnerException(ex.Message);
        }
    }
}
