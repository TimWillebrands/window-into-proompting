using Microsoft.AspNetCore.Mvc;
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

    // Reasoning is curator free-text and an untrusted boundary input (multi-user app) — cap
    // it so a single Stance can't bloat the ambient block or a prompt.
    private const int MaxReasoningLength = 600;

    /// <summary>
    /// Append a Stance (ADR 0016) authored by a Participant toward another Participant, a
    /// Concept, or themselves. Append-only — the new edge becomes the latest-wins current
    /// stance; prior edges are never mutated. The curator is the floor's first writer.
    /// </summary>
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
                // Normalise like the Capture extractor: lowercased name dedups, display keeps
                // the label as typed.
                target = new StanceTargetSpec(
                    StanceTargetKind.Concept, null, display.ToLowerInvariant(), display);
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

    /// <summary>
    /// One-click retract (ADR 0016): append a neutralizing edge over an earlier Stance.
    /// Latest-wins makes the retract current immediately — the target stops rendering in
    /// <c># Where you stand</c> — and the retracted edge stays in the log. Never deletes.
    /// </summary>
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

    /// <summary>
    /// Curator button: run Consolidation v1 (ADR 0016) for one Participant — walk the
    /// Recollections past their watermark, one LLM call, auto-append the proposed Stances,
    /// advance the watermark. A run over nothing new is a cheap no-op (no LLM call).
    /// </summary>
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

    /// <summary>
    /// Curator button, Party-wide: run Consolidation for every LLM-driven Participant.
    /// Participants with nothing past their watermark come back with zero walked — only
    /// those with unprocessed experience cost an LLM call.
    /// </summary>
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

    // Roster = everyone else in the Party (User-driven included — a persona can absolutely
    // form a stance toward the human), excluding the subject (self is its own target kind).
    private static List<ConsolidationRosterEntry> ToRoster(
        IReadOnlyList<CastMember> cast, Guid subjectPersonaId)
        => cast
            .Where(c => c.Id != subjectPersonaId)
            .Select(c => new ConsolidationRosterEntry(c.Id, c.Name))
            .ToList();

    private static ConsolidationRunResponse ToResponse(ConsolidationRunResult result)
        => new(result.RunId, result.PersonaId, result.RecollectionsWalked,
            result.StancesAppended, result.Watermark, result.Skipped);

    /// <summary>
    /// Every Stance authored by a Participant, newest first. The latest edge per target is
    /// flagged <see cref="StanceRecord.IsCurrent"/>; superseded edges remain for history.
    /// </summary>
    [HttpGet("participants/{personaId:guid}/stances")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StanceRecord>>> ListStances(
        Guid partyId, Guid personaId, CancellationToken ct)
    {
        var stances = await memoryRepository.ListStancesAsync(partyId, personaId, ct);
        return Ok(stances);
    }
}

/// <summary>
/// Request body for authoring a Stance. <see cref="TargetPersonaId"/> is required when
/// <see cref="TargetKind"/> is <see cref="StanceTargetKind.Participant"/>;
/// <see cref="TargetConceptName"/> when it is <see cref="StanceTargetKind.Concept"/>; neither
/// for <see cref="StanceTargetKind.Self"/>.
/// </summary>
public sealed record AppendStanceRequest(
    double Valence,
    string? Reasoning,
    StanceTargetKind TargetKind,
    Guid? TargetPersonaId,
    string? TargetConceptName);

/// <summary>The id of the freshly appended Stance edge.</summary>
public sealed record AppendStanceResponse(Guid Id);

/// <summary>
/// Outcome of one Consolidation run for one Participant. <see cref="Skipped"/> means another
/// run for the same Participant was already in flight and this one bowed out.
/// </summary>
public sealed record ConsolidationRunResponse(
    Guid RunId,
    Guid PersonaId,
    int RecollectionsWalked,
    int StancesAppended,
    DateTimeOffset? Watermark,
    bool Skipped);
