using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;

namespace PartyTown.Controllers;

[ApiController]
[Route("parties/{partyId:guid}/memory")]
public sealed class MemoryController(
    IMemoryRepository memoryRepository,
    ConsolidationService consolidationService,
    IGrainFactory grains) : ControllerBase
{
    [HttpGet("graph")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemoryGraphDto>> GetGraph(Guid partyId, CancellationToken ct)
    {
        var graph = await memoryRepository.GetPartyMemoryGraphAsync(partyId, ct);
        return Ok(graph);
    }

    private const int MaxReasoningLength = 600;

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("participants/{personaId:guid}/stances")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AppendStanceResponse>> AppendStance(
        Guid partyId, Guid personaId, [FromBody] AppendStanceRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest("Missing request body.");
        }

        if (double.IsNaN(request.Valence) || request.Valence < -1.0 || request.Valence > 1.0)
        {
            return BadRequest("Valence must be a number in the range -1..1.");
        }

        var reasoning = request.Reasoning?.Trim();
        if (string.IsNullOrEmpty(reasoning))
        {
            return BadRequest("Reasoning is required.");
        }
        if (reasoning.Length > MaxReasoningLength)
        {
            return BadRequest($"Reasoning must be at most {MaxReasoningLength} characters.");
        }

        StanceTargetSpec target;
        switch (request.TargetKind)
        {
            case StanceTargetKind.Participant:
                if (request.TargetPersonaId is not Guid targetPersonaId || targetPersonaId == Guid.Empty)
                {
                    return BadRequest("A Participant Stance requires targetPersonaId.");
                }
                if (targetPersonaId == personaId)
                {
                    return BadRequest("Use TargetKind.Self to record a stance toward oneself.");
                }
                target = new StanceTargetSpec(StanceTargetKind.Participant, targetPersonaId, null, null);
                break;

            case StanceTargetKind.Concept:
                var display = request.TargetConceptName?.Trim();
                if (string.IsNullOrEmpty(display))
                {
                    return BadRequest("A Concept Stance requires targetConceptName.");
                }
                if (display.Length > MemoryExtractor.MaxConceptNameChars)
                {
                    return BadRequest(
                        $"Concept name must be at most {MemoryExtractor.MaxConceptNameChars} characters.");
                }
                target = new StanceTargetSpec(
                    StanceTargetKind.Concept, null,
                    MemoryExtractor.NormaliseConceptName(display), display);
                break;

            case StanceTargetKind.Self:
                target = new StanceTargetSpec(StanceTargetKind.Self, null, null, null);
                break;

            default:
                return BadRequest("Unknown stance target kind.");
        }

        var id = await memoryRepository.AppendStanceAsync(
            partyId, personaId, target, request.Valence, reasoning, attribution: null, ct);
        return CreatedAtAction(
            nameof(ListStances), new { partyId, personaId }, new AppendStanceResponse(id));
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("participants/{personaId:guid}/stances/{stanceId:guid}/retract")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppendStanceResponse>> RetractStance(
        Guid partyId, Guid personaId, Guid stanceId, CancellationToken ct)
    {
        var id = await memoryRepository.RetractStanceAsync(partyId, personaId, stanceId, ct);
        if (id is null)
        {
            return NotFound("No such stance authored by this participant.");
        }
        return CreatedAtAction(
            nameof(ListStances), new { partyId, personaId }, new AppendStanceResponse(id.Value));
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("participants/{personaId:guid}/consolidate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConsolidationRunResponse>> ConsolidateParticipant(
        Guid partyId, Guid personaId, CancellationToken ct)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var member = cast.FirstOrDefault(c => c.Id == personaId);
        if (member is null || member.DefaultDriver != DriverKind.LLM)
        {
            return NotFound("No LLM-driven participant with this id in the party.");
        }

        var result = await consolidationService.RunAsync(
            partyId, ToSubject(member), ToRoster(cast, personaId), ct);
        return Ok(ToResponse(result));
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("consolidate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ConsolidationRunResponse>>> ConsolidateParty(
        Guid partyId, CancellationToken ct)
    {
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var responses = new List<ConsolidationRunResponse>();
        foreach (var member in cast.Where(c => c.DefaultDriver == DriverKind.LLM))
        {
            var result = await consolidationService.RunAsync(
                partyId, ToSubject(member), ToRoster(cast, member.Id), ct);
            responses.Add(ToResponse(result));
        }
        return Ok(responses);
    }

    private static ConsolidationSubject ToSubject(CastMember member)
        => new(member.Id, member.Name, member.Bio);

    private static List<ConsolidationRosterEntry> ToRoster(
        IReadOnlyList<CastMember> cast, Guid subjectPersonaId)
        => cast
            .Where(c => c.Id != subjectPersonaId)
            .Select(c => new ConsolidationRosterEntry(c.Id, c.Name))
            .ToList();

    private static ConsolidationRunResponse ToResponse(ConsolidationRunResult result)
        => new(result.RunId, result.PersonaId, result.RecollectionsWalked,
            result.StancesAppended, result.Watermark, result.Skipped);

    [HttpGet("participants/{personaId:guid}/stances")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StanceRecord>>> ListStances(
        Guid partyId, Guid personaId, CancellationToken ct)
    {
        var stances = await memoryRepository.ListStancesAsync(partyId, personaId, ct);
        return Ok(stances);
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("participants/{personaId:guid}/stances/{stanceId:guid}/promote")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AppendStanceResponse>> PromoteStance(
        Guid partyId, Guid personaId, Guid stanceId, CancellationToken ct)
    {
        var id = await memoryRepository.PromoteStanceAsync(partyId, personaId, stanceId, ct);
        if (id is null)
        {
            return NotFound("No such stance authored by this participant.");
        }
        return CreatedAtAction(
            nameof(ListStances), new { partyId, personaId }, new AppendStanceResponse(id.Value));
    }
}

public sealed record AppendStanceRequest(
    double Valence,
    string? Reasoning,
    StanceTargetKind TargetKind,
    Guid? TargetPersonaId,
    string? TargetConceptName);

public sealed record AppendStanceResponse(Guid Id);

public sealed record ConsolidationRunResponse(
    Guid RunId,
    Guid PersonaId,
    int RecollectionsWalked,
    int StancesAppended,
    DateTimeOffset? Watermark,
    bool Skipped);
