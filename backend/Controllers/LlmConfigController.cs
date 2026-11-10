using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using Microsoft.AspNetCore.OutputCaching;
using PartyTown.Configuration;
using PartyTown.Grains;
using PartyTown.Grains.Generation;

namespace PartyTown.Controllers;

[ApiController]
[Route("[controller]")]
public class LlmConfigController(IGrainFactory grains, IHttpClientFactory httpClientFactory, Tailnets tailnets) : ControllerBase
{
    private const string MaskPrefix = "••••";

    private ILlmProviderConfigGrain ConfigGrain => grains.GetGrain<ILlmProviderConfigGrain>(0);

    /// <summary>Provider list with API keys masked — keys never leave the server.</summary>
    [HttpGet("providers")]
    public async Task<ActionResult<List<LlmProviderEntry>>> GetProviders()
    {
        var providers = await ConfigGrain.GetProvidersAsync();
        return Ok(providers.Select(p => p with { ApiKey = Mask(p.ApiKey) }).ToList());
    }

    /// <summary>Whether the router can use each enabled provider now: Ready, Benched or Offline (ADR 0040).</summary>
    [HttpGet("providers/availability")]
    public async Task<ActionResult<List<ProviderAvailability>>> GetAvailability()
        => Ok(await grains.GetGrain<ILlmRouterGrain>(0).GetAvailabilityAsync());

    /// <summary>Tailnets this deployment has a sidecar for; a provider can go through one.</summary>
    [HttpGet("tailnets")]
    public ActionResult<List<string>> GetTailnets() => Ok(tailnets.Names.ToList());

    /// <summary>Spend per OpenRouter key (OpenRouter /key endpoint); other providers are free/local.</summary>
    [HttpGet("usage")]
    public async Task<ActionResult<List<ProviderUsage>>> GetUsage(CancellationToken ct)
    {
        var providers = (await ConfigGrain.GetProvidersAsync())
            .Where(p => p.Type == "openrouter" && p.IsEnabled && !string.IsNullOrWhiteSpace(p.ApiKey))
            // Usage is per key; providers sharing a key would double count.
            .GroupBy(p => p.ApiKey)
            .Select(g => g.First() with { ModelName = string.Join(" + ", g.Select(p => p.ModelName)) })
            .ToList();
        var http = httpClientFactory.CreateClient();
        var result = new List<ProviderUsage>();
        foreach (var p in providers)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, $"{p.BaseUrl.TrimEnd('/')}/key");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", p.ApiKey);
                using var res = await http.SendAsync(req, ct);
                res.EnsureSuccessStatusCode();
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
                var data = doc.RootElement.GetProperty("data");
                double? Num(string name) => data.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : null;
                result.Add(new ProviderUsage(p.Id, p.ModelName, Num("usage") ?? 0, Num("limit"), Num("limit_remaining")));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.Add(new ProviderUsage(p.Id, p.ModelName, 0, null, null));
            }
        }
        return Ok(result);
    }

    /// <summary>Which persona uses which provider for which tier; everything else uses the shared pool.</summary>
    [HttpGet("persona-routes")]
    public async Task<ActionResult<List<PersonaRoute>>> GetPersonaRoutes()
        => Ok(await ConfigGrain.GetPersonaRoutesAsync());

    /// <summary>Which provider each persona's work goes to now, per tier; persona Guid.Empty is work nobody owns (ADR 0048).</summary>
    [HttpGet("assignments")]
    public async Task<ActionResult<List<ModelAssignment>>> GetAssignments()
    {
        var personas = await grains.GetGrain<IPersonaRootGrain>(Guid.Empty).GetAll();
        return Ok(await grains.GetGrain<ILlmRouterGrain>(0).GetAssignmentsAsync(personas.Select(p => p.Id).ToList()));
    }

    /// <summary>Replaces one persona's routes; send an empty list to put them back on the shared pool.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPut("persona-routes/{personaId:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<PersonaRoute>>> SetPersonaRoutes(Guid personaId, [FromBody] List<PersonaRoute> routes)
    {
        try
        {
            return Ok(await ConfigGrain.SetPersonaRoutesAsync(personaId, routes));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private static string? Mask(string? key)
        => string.IsNullOrEmpty(key) ? key : key.Length <= 8 ? MaskPrefix : $"{MaskPrefix}{key[^4..]}";

    [HttpGet("providers/{id:guid}/models")]
    // TODO: 120s OutputCache can serve stale models after provider mutations.
    // Prefer tag-based invalidation ("llm-providers" tag + evict in AddProviderAsync/UpdateProviderAsync/RemoveProviderAsync).
    [OutputCache(Duration = 120)]
    public async Task<ActionResult<List<string>>> GetProviderModels(Guid id)
    {
        var providers = await ConfigGrain.GetProvidersAsync();
        var provider = providers.FirstOrDefault(p => p.Id == id);
        if (provider == null)
            return NotFound();

        var endpoint = LlmEndpoints.Resolve(grains, provider.Type, id);

        var models = await endpoint.GetModelsAsync();
        return Ok(models.Select(m => m.Name).ToList());
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]

    [HttpPost("providers")]
    public async Task<ActionResult<LlmProviderEntry>> AddProvider([FromBody] LlmProviderEntry entry)
    {
        var created = await ConfigGrain.AddProviderAsync(entry);
        // TODO: Location header points at the collection endpoint (GetProviders has no {id} route).
        // Add a GetProvider(Guid id) action and use CreatedAtAction(nameof(GetProvider), new { id = created.Id }, created).
        return CreatedAtAction(nameof(GetProviders), created with { ApiKey = Mask(created.ApiKey) });
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]

    [HttpPut("providers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LlmProviderEntry>> UpdateProvider(Guid id, [FromBody] LlmProviderEntry entry)
    {
        if (entry.Id != id)
            return BadRequest("Id in body must match route id");

        // A masked key coming back from the UI means "unchanged".
        if (entry.ApiKey?.StartsWith(MaskPrefix) == true)
        {
            var current = (await ConfigGrain.GetProvidersAsync()).FirstOrDefault(p => p.Id == id);
            entry = entry with { ApiKey = current?.ApiKey };
        }

        try
        {
            var updated = await ConfigGrain.UpdateProviderAsync(entry);
            return Ok(updated with { ApiKey = Mask(updated.ApiKey) });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]

    [HttpDelete("providers/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteProvider(Guid id)
    {
        try
        {
            await ConfigGrain.RemoveProviderAsync(id);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }
}

public sealed record ProviderUsage(Guid ProviderId, string? ModelName, double UsageUsd, double? LimitUsd, double? RemainingUsd);
