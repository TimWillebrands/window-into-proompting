using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Memory;

namespace PartyTown.Controllers;

[ApiController]
[Route("parties/{partyId:guid}/memory")]
public sealed class MemoryController(IMemoryRepository memoryRepository) : ControllerBase
{
    [HttpGet("graph")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemoryGraphDto>> GetGraph(Guid partyId, CancellationToken ct)
    {
        var graph = await memoryRepository.GetPartyMemoryGraphAsync(partyId, ct);
        return Ok(graph);
    }
}
