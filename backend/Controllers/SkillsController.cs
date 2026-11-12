using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Skills;

namespace PartyTown.Controllers;

/// <summary>Skills (ADR 0054): what each Persona knows how to do, who they are (Intrinsic) and what doing taught them (Acquired).</summary>
[ApiController]
public sealed class SkillsController(IGrainFactory grains) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("worlds/{partyId:guid}/skills")]
    public async Task<ActionResult<List<SkillSummary>>> GetSkills(Guid partyId)
    {
        var cast = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Where(c => c.Id != Narrator.PersonaId).ToList();
        var views = await Task.WhenAll(cast.Select(c => Skills(partyId, c.Id).GetAsync()));
        return Ok(cast.Select((c, i) => new SkillSummary(c.Id, c.Name, views[i].Where(s => s.Intrinsic).ToList(), views[i].Where(s => !s.Intrinsic).ToList())).ToList());
    }

    /// <summary>Author a Persona's Intrinsic Skills; from then on the sheet no longer rewrites them.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPut("personas/{personaId:guid}/skills")]
    [ProducesResponseType<List<Skill>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<Skill>>> SetIntrinsic(Guid personaId, [FromBody] List<SkillRequest> request)
    {
        if (request.Any(r => string.IsNullOrWhiteSpace(r.Name))) return BadRequest("Every skill needs a name.");
        return Ok(await grains.GetGrain<IIntrinsicSkillsGrain>(personaId).SetAsync(request.Select(r => New(r, SkillSource.Authored, practice: 30)).ToList()));
    }

    /// <summary>Give a Participant an Acquired Skill in this Party, as if they'd picked it up already.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("worlds/{partyId:guid}/skills/{participantId:guid}")]
    [ProducesResponseType<SkillView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SkillView>> AddAcquired(Guid partyId, Guid participantId, [FromBody] SkillRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("What do they know how to do?");
        return Ok(await Skills(partyId, participantId).AddAsync(New(request, SkillSource.Authored, practice: SkillRules.Learns)));
    }

    /// <summary>Forget an Acquired Skill; what taught it can teach it again.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpDelete("worlds/{partyId:guid}/skills/{participantId:guid}/{skillId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Forget(Guid partyId, Guid participantId, Guid skillId)
        => await Skills(partyId, participantId).RemoveAsync(skillId) is null ? NotFound() : NoContent();

    /// <summary>Promotion: an Acquired Skill becomes part of who the Persona is, in every Party.</summary>
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
    [HttpPost("worlds/{partyId:guid}/skills/{participantId:guid}/{skillId:guid}/promote")]
    [ProducesResponseType<List<Skill>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Skill>>> Promote(Guid partyId, Guid participantId, Guid skillId)
    {
        if (await Skills(partyId, participantId).RemoveAsync(skillId) is not { } skill) return NotFound();
        return Ok(await grains.GetGrain<IIntrinsicSkillsGrain>(participantId).AddAsync(skill));
    }

    private ISkillsGrain Skills(Guid partyId, Guid who) => grains.GetGrain<ISkillsGrain>(WorldKeys.Life(partyId, who));

    private static Skill New(SkillRequest r, SkillSource source, int practice)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new Skill
        {
            Name = WorldStream.Clip(r.Name.Trim(), 60), How = WorldStream.Clip(r.How?.Trim() ?? string.Empty, 400),
            Cues = (r.Cues ?? new List<string>()).SelectMany(c => PartyTown.World.Self.VoiceWear.ContentWords(c)).Distinct().Take(SkillRules.MaxCues).ToList(),
            Source = source, Practice = practice, Since = now, PractisedAt = now, WrittenFor = SkillRules.Level(practice),
        };
    }
}

public sealed record SkillRequest(string Name, string? How = null, List<string>? Cues = null);

public sealed record SkillSummary(Guid ParticipantId, string Name, List<SkillView> Intrinsic, List<SkillView> Acquired);
