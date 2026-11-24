using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PartyTown.World.Places;

namespace PartyTown.Services.Render;

public sealed class RenderOptions
{
    public string? AccountId { get; set; }
    public string? ApiToken { get; set; }
    public string? BaseUrl { get; set; }
    public string? ScreenshotUrl { get; set; }
}

public sealed class PlaceRenderer(IHttpClientFactory http, IMemoryCache cache, IOptions<RenderOptions> options, ILogger<PlaceRenderer> logger)
{
    public const string ClientName = "place-render";
    private static readonly TimeSpan DraftLife = TimeSpan.FromMinutes(10);
    private RenderOptions Options => options.Value;

    private bool Local => !string.IsNullOrWhiteSpace(Options.ScreenshotUrl);

    public bool Available =>
        !string.IsNullOrWhiteSpace(Options.BaseUrl)
        && (Local || (!string.IsNullOrWhiteSpace(Options.AccountId) && !string.IsNullOrWhiteSpace(Options.ApiToken)));

    public string StashDraft(Place draft)
    {
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        cache.Set(DraftKey(key), draft, DraftLife);
        return key;
    }

    public Place? Draft(string key) => cache.TryGetValue(DraftKey(key), out Place? draft) ? draft : null;

    public async Task<byte[]> RenderAsync(Guid world, Guid placeId, string? draft, int? hour, CancellationToken ct = default)
    {
        if (!Available) throw new InvalidOperationException("Rendering isn't set up: Render:BaseUrl and either Render:ScreenshotUrl or Render:AccountId with Render:ApiToken are needed.");
        var page = $"{Options.BaseUrl!.TrimEnd('/')}/render/place/{placeId}?world={world}"
                   + (draft is null ? string.Empty : $"&draft={draft}")
                   + (hour is null ? string.Empty : $"&hour={Math.Clamp(hour.Value, 0, 23)}");
        var body = new JsonObject
        {
            ["url"] = page,
            ["viewport"] = new JsonObject { ["width"] = 1280, ["height"] = 860 },
            ["gotoOptions"] = new JsonObject { ["waitUntil"] = "load", ["timeout"] = 10000 },
            ["waitForSelector"] = new JsonObject { ["selector"] = "html[data-render-ready]", ["timeout"] = 15000 },
            ["screenshotOptions"] = new JsonObject { ["type"] = "png" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Local
            ? Options.ScreenshotUrl
            : $"https://api.cloudflare.com/client/v4/accounts/{Options.AccountId}/browser-rendering/screenshot")
        {
            Content = JsonContent.Create(body),
        };
        if (!Local) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiToken);
        using var response = await http.CreateClient(ClientName).SendAsync(request, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "image/png") return bytes;
        var why = Why(bytes);
        logger.LogWarning("Rendering {Page} failed: {Status} {Why}", page, (int)response.StatusCode, why);
        throw new InvalidOperationException($"The renderer said {(int)response.StatusCode}: {why}");
    }

    private static string Why(byte[] bytes)
    {
        try
        {
            var doc = JsonNode.Parse(bytes);
            var errors = doc?["errors"] as JsonArray;
            if (errors is { Count: > 0 }) return string.Join("; ", errors.Select(e => e?["message"]?.ToString()));
        }
        catch (JsonException)
        {
        }

        return System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 300));
    }

    private static string DraftKey(string key) => $"render-draft:{key}";
}
