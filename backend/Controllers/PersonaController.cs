using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.Controllers;

[ApiController]
[Route("[controller]")]
/// <summary>
/// HTTP API for creating, reading, updating, and deleting personas.
/// </summary>
public class PersonaController(IGrainFactory grains, ILogger<PersonaController> logger) : ControllerBase
{
    /// <summary>
    /// Returns all personas currently registered.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<Persona[]>> GetAll()
    {
        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var personas = await root.GetAll();
        return Ok(personas);
    }

    /// <summary>
    /// Returns a single persona by id.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Persona>> GetById(Guid id)
    {
        if (id == Guid.Empty)
        {
            return BadRequest("Persona id cannot be empty.");
        }

        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var all = await root.GetAll();

        var persona = all.FirstOrDefault(p => p.Id == id);
        if (persona is null)
        {
            return NotFound();
        }

        return Ok(persona);
    }

    /// <summary>
    /// Creates or updates a persona based on the payload and optional route id.
    /// </summary>
    [HttpPut("{id:guid?}")]
    public async Task<ActionResult<Persona>> Upsert(Guid? id, [FromBody] Persona persona)
    {
        if (persona is null)
        {
            return BadRequest("Persona payload is required.");
        }

        if (id.HasValue && id.Value == Guid.Empty)
        {
            return BadRequest("Persona id cannot be empty.");
        }

        if (id.HasValue && persona.Id != Guid.Empty && id.Value != persona.Id)
        {
            return BadRequest("Route id does not match payload id.");
        }

        if (string.IsNullOrWhiteSpace(persona.Name))
        {
            return BadRequest("Persona name is required.");
        }

        if (string.IsNullOrWhiteSpace(persona.SystemPrompt))
        {
            return BadRequest("Persona system prompt is required.");
        }

        var personaId = id ?? (persona.Id == Guid.Empty ? Guid.NewGuid() : persona.Id);

        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var existing = await root.GetAllMetadata();
        var isNew = existing.All(item => item.Id != personaId);

        if (isNew)
        {
            logger.LogInformation("Creating persona: {PersonaName}", persona.Name);
        }
        else
        {
            logger.LogInformation("Updating persona: {PersonaId}", personaId);
        }

        await root.AddPersona(personaId, persona.Name, persona.SystemPrompt, persona.Bio);

        var personaGrain = grains.GetGrain<IPersonaGrain>(personaId);
        var updated = await personaGrain.GetPersona();

        if (isNew)
        {
            logger.LogInformation("Persona created: {PersonaId}", personaId);
        }

        return isNew
            ? CreatedAtAction(nameof(GetById), new { id = updated.Id }, updated)
            : AcceptedAtAction(nameof(GetById), new { id = updated.Id }, updated);
    }

    /// <summary>
    /// Removes a persona by id.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (id == Guid.Empty)
        {
            return BadRequest("Persona id cannot be empty.");
        }

        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var exists = await root.HasPersonaId(id);
        if (!exists)
        {
            return NotFound();
        }

        logger.LogInformation("Deleting persona: {PersonaId}", id);
        await root.RemovePersona(id);
        return NoContent();
    }
}
