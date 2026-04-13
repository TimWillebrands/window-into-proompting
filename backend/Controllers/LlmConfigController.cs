using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using PartyTown.Grains.Generation;

namespace PartyTown.Controllers;

[ApiController]
[Route("[controller]")]
public class LlmConfigController(IGrainFactory grains) : ControllerBase
{
    private ILlmProviderConfigGrain ConfigGrain => grains.GetGrain<ILlmProviderConfigGrain>(0);

    [HttpGet("providers")]
    public async Task<ActionResult<List<LlmProviderEntry>>> GetProviders()
    {
        var providers = await ConfigGrain.GetProvidersAsync();
        return Ok(providers);
    }

    [HttpGet("providers/{id:guid}/models")]
    [OutputCache(Duration = 120)]
    public async Task<ActionResult<List<string>>> GetProviderModels(Guid id)
    {
        var providers = await ConfigGrain.GetProvidersAsync();
        var provider = providers.FirstOrDefault(p => p.Id == id);
        if (provider == null)
            return NotFound();

        var endpoint = provider.Type switch
        {
            "ollama" => (ILlmEndpointGrain)grains.GetGrain<IOllamaEndpointGrain>(id),
            "openrouter" => (ILlmEndpointGrain)grains.GetGrain<IOpenRouterEndpointGrain>(id),
            _ => throw new InvalidOperationException($"Unknown provider type: {provider.Type}")
        };

        var models = await endpoint.GetModelsAsync();
        return Ok(models.Select(m => m.Name).ToList());
    }

    [HttpPost("providers")]
    public async Task<ActionResult<LlmProviderEntry>> AddProvider([FromBody] LlmProviderEntry entry)
    {
        var created = await ConfigGrain.AddProviderAsync(entry);
        return CreatedAtAction(nameof(GetProviders), created);
    }

    [HttpPut("providers/{id:guid}")]
    public async Task<ActionResult<LlmProviderEntry>> UpdateProvider(Guid id, [FromBody] LlmProviderEntry entry)
    {
        if (entry.Id != id)
            return BadRequest("Id in body must match route id");

        var updated = await ConfigGrain.UpdateProviderAsync(entry);
        return Ok(updated);
    }

    [HttpDelete("providers/{id:guid}")]
    public async Task<ActionResult> DeleteProvider(Guid id)
    {
        await ConfigGrain.RemoveProviderAsync(id);
        return NoContent();
    }
}
