using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PartyTown.Configuration;

namespace PartyTown.World.Work.Cdu;

public sealed record CduSpeaker(string Id, string Name);

public sealed record CduCommit(string Sha, string Subject, string Author);

/// <summary>The Agora as the CDU last saw it. State is OPEN, MERGED or CLOSED; Checks none, pending, passing or failing.</summary>
public sealed record CduPr(int Number, string Url, string Title, string State, string Head, string Checks);

public sealed record CduAllowance(double Usd, double CapUsd);

/// <summary>A mind the CDU can be woken with. Tier is free, small or capable; prices are USD per million tokens.</summary>
public sealed record CduModel(string Id, string Label, string? Tier, double In, double Out);

public sealed record CduTrunk(string? Head, List<CduCommit> Ahead);

public sealed record CduTurn(
    string Id, CduSpeaker From, string Text, string Kind, string Status, long At, long? DoneAt,
    string? Reply, bool? Asked, List<CduCommit> Commits, double? Usd, string? Error);

public sealed record CduStatus(
    bool Awake, string? Hearing, int Queued, string Model, CduTrunk? Trunk, CduPr? Pr, CduAllowance? Allowance,
    long Lines, long News, CduTurn? Last, List<CduModel>? Models = null);

/// <summary>A transcript line or a piece of news, numbered for reading by cursor.</summary>
public sealed record CduEntry(long N, long At, string Kind, string Text, JsonElement? Data)
{
    public string? Str(string name) => Data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public int? Int(string name) => Data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    public double? Num(string name) => Data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    public bool Flag(string name) => Data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>Refused by the CDU, or the CDU couldn't be reached; only a refusal carries a status.</summary>
public sealed class CduException(string message, int? status = null) : Exception(message)
{
    public int? Status => status;
}

/// <summary>The World's one door to the CDU (ADR 0039). The tide calls return at once; what came of them is news.</summary>
public interface ICdu
{
    bool Enabled { get; }
    Task<CduStatus> StatusAsync(CancellationToken ct);
    /// <summary>Model is the mind the speaker chose; null wakes the cheapest.</summary>
    Task<CduTurn> TalkAsync(CduSpeaker from, string text, string kind, string? model, CancellationToken ct);
    Task<List<CduEntry>> LinesAsync(long after, CancellationToken ct);
    Task<List<CduEntry>> NewsAsync(long after, CancellationToken ct);
    Task TideInAsync(CancellationToken ct);
    Task TideOutAsync(CancellationToken ct);
}

/// <summary>Cdu:Url, Cdu:Token and optionally Cdu:Tailnet, as a provider reaches its machine (ADR 0033).</summary>
public sealed class HttpCdu(IConfiguration config, Tailnets tailnets) : ICdu
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly HttpClient Direct = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string? _url = config["Cdu:Url"]?.TrimEnd('/');
    private readonly string? _token = config["Cdu:Token"];
    private readonly string? _tailnet = config["Cdu:Tailnet"];

    public bool Enabled => !string.IsNullOrWhiteSpace(_url);

    public Task<CduStatus> StatusAsync(CancellationToken ct) => SendAsync<CduStatus>(HttpMethod.Get, "/status", null, ct);

    public Task<CduTurn> TalkAsync(CduSpeaker from, string text, string kind, string? model, CancellationToken ct)
        => SendAsync<CduTurn>(HttpMethod.Post, "/talk", new { from, text, kind, model }, ct);

    public Task<List<CduEntry>> LinesAsync(long after, CancellationToken ct) => SendAsync<List<CduEntry>>(HttpMethod.Get, $"/lines?after={after}", null, ct);

    public Task<List<CduEntry>> NewsAsync(long after, CancellationToken ct) => SendAsync<List<CduEntry>>(HttpMethod.Get, $"/news?after={after}", null, ct);

    public Task TideInAsync(CancellationToken ct) => SendAsync<JsonElement>(HttpMethod.Post, "/tide/in", new { }, ct);

    public Task TideOutAsync(CancellationToken ct) => SendAsync<JsonElement>(HttpMethod.Post, "/tide/out", new { }, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Enabled) throw new CduException("No CDU is configured.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(CallBudget);
        using var request = new HttpRequestMessage(method, _url + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null) request.Content = JsonContent.Create(body, options: Web);
        try
        {
            using var response = await (tailnets.ClientFor(_tailnet) ?? Direct).SendAsync(request, budget.Token);
            if (!response.IsSuccessStatusCode)
                throw new CduException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(budget.Token)}", (int)response.StatusCode);
            return await response.Content.ReadFromJsonAsync<T>(Web, budget.Token) ?? throw new CduException("The CDU sent nothing.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            throw new CduException(ex.Message);
        }
    }
}
