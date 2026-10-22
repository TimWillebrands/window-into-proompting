using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Desires;

namespace PartyTown.Controllers;

/// <summary>Desires (ADR 0026): what each Persona wants, who they are (Intrinsic) and what play gave them (Acquired).</summary>
[ApiController]
public sealed class DesiresController(IGrainFactory grains) : ControllerBase
{
    [HttpGet("worlds/{partyId:guid}/desires")]
    public async Task<ActionResult<List<DesireSummary>>> GetDesires(Guid partyId)
    {
        var cast = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Where(c => c.Id != Narrator.PersonaId).ToList();
        var views = await Task.WhenAll(cast.Select(c => Desires(partyId, c.Id).GetAsync()));
        return Ok(cast.Select((c, i) => new DesireSummary(c.Id, c.Name, views[i].Intrinsic, views[i].Acquired)).ToList());
    }

    /// <summary>Author a Persona's Intrinsic Desires; from then on the sheet no longer rewrites them.</summary>
    [HttpPut("personas/{personaId:guid}/desires")]
    public async Task<ActionResult<List<Desire>>> SetIntrinsic(Guid personaId, [FromBody] List<DesireRequest> request)
        => Ok(await grains.GetGrain<IIntrinsicDesiresGrain>(personaId).SetAsync(request.Select(r => New(r, DesireSource.Authored)).ToList()));

    /// <summary>Give a Participant an Acquired Desire in this Party.</summary>
    [HttpPost("worlds/{partyId:guid}/desires/{participantId:guid}")]
    public async Task<ActionResult<Desire>> AddAcquired(Guid partyId, Guid participantId, [FromBody] DesireRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest("What do they want?");
        return Ok(await Desires(partyId, participantId).AddAsync(New(request, DesireSource.Authored)));
    }

    [HttpPost("worlds/{partyId:guid}/desires/{participantId:guid}/{desireId:guid}/status")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> SetStatus(Guid partyId, Guid participantId, Guid desireId, [FromBody] DesireStatusRequest request)
        => await Desires(partyId, participantId).MarkAsync(desireId, request.Status) ? NoContent() : NotFound();

    /// <summary>Promotion: an Acquired Desire becomes part of who the Persona is, in every Party.</summary>
    [HttpPost("worlds/{partyId:guid}/desires/{participantId:guid}/{desireId:guid}/promote")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Desire>>> Promote(Guid partyId, Guid participantId, Guid desireId)
    {
        var grain = Desires(partyId, participantId);
        if ((await grain.GetAsync()).Acquired.FirstOrDefault(d => d.Id == desireId) is not { } desire) return NotFound();
        var intrinsic = await grains.GetGrain<IIntrinsicDesiresGrain>(participantId).AddAsync(desire);
        await grain.RemoveAsync(desireId);
        return Ok(intrinsic);
    }

    private IDesiresGrain Desires(Guid partyId, Guid who) => grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, who));

    private static Desire New(DesireRequest r, DesireSource source)
    {
        var d = DesireRules.Parse(r.Text, r.Kind, source, DateTimeOffset.UtcNow) ?? new Desire { Kind = r.Kind, Text = r.Text.Trim(), Source = source };
        d.TargetId = r.TargetId;
        return d;
    }
}

public sealed record DesireRequest(DesireKind Kind, string Text, Guid? TargetId = null);

public sealed record DesireStatusRequest(DesireStatus Status);

public sealed record DesireSummary(Guid ParticipantId, string Name, List<Desire> Intrinsic, List<Desire> Acquired);
