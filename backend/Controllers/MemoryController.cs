using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Memory;

namespace PartyTown.Controllers;

/// <summary>
/// Debug viz endpoint for the per-Party memory subgraph (issue #58). Thin pass-through
/// to <see cref="IMemoryRepository.GetPartyMemoryGraphAsync"/>; all Cypher and shaping
/// lives in the repository.
/// </summary>
[ApiController]
[Route("parties/{partyId:guid}/memory")]
public sealed class MemoryController(IMemoryRepository memoryRepository) : ControllerBase
{
    /// <summary>
    /// Returns the AGE-backed memory subgraph (nodes + edges) scoped to one Party for
    /// the Memory Graph desktop app. Snapshot; client refreshes manually.
    /// </summary>
    [HttpGet("graph")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemoryGraphDto>> GetGraph(Guid partyId, CancellationToken ct)
    {
        var graph = await memoryRepository.GetPartyMemoryGraphAsync(partyId, ct);
        return Ok(graph);
    }
}
