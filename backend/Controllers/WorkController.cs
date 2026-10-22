using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Work;

namespace PartyTown.Controllers;

/// <summary>Jobs (ADR 0026): who works where and when, and how today's shift went. Authors set or clear them.</summary>
[ApiController]
[Route("worlds/{partyId:guid}")]
public sealed class WorkController(IGrainFactory grains) : ControllerBase
{
    [HttpGet("jobs")]
    public async Task<ActionResult<List<JobSummary>>> GetJobs(Guid partyId)
    {
        var cast = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Where(c => c.Id != Narrator.PersonaId).ToList();
        var views = await Task.WhenAll(cast.Select(c => Occupation(partyId, c.Id).GetAsync()));
        // Openings rename and move workplaces (ADR 0027); show them as they are now.
        var places = (await grains.GetGrain<World.Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places;
        foreach (var job in views.Select(v => v.Job).OfType<Occupation>())
            if (places.FirstOrDefault(p => p.Id == (job.PlaceId ?? job.LocaleId)) is { } place) WorkRules.Sync(job, place);
        return Ok(cast.Select((c, i) => Summary(c.Id, c.Name, views[i])).ToList());
    }

    /// <summary>Give someone a job, or change it. Days like "Mon-Fri"; hours like "09:00-17:00", not past midnight.</summary>
    [HttpPut("jobs/{participantId:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobSummary>> SetJob(Guid partyId, Guid participantId, [FromBody] JobRequest request)
    {
        var who = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Workplace)) return BadRequest("A job needs a title and a workplace.");
        if (Shifts.ParseHours(request.Hours) is not var (start, end)) return BadRequest("Hours like 09:00-17:00, within one day.");
        var view = await Occupation(partyId, participantId).SetAsync(new Occupation
        {
            Title = request.Title.Trim(), Workplace = request.Workplace.Trim(), Days = Shifts.ParseDays(request.Days), Start = start, End = end,
        });
        return Ok(Summary(who.Id, who.Name, view));
    }

    [HttpDelete("jobs/{participantId:guid}")]
    public async Task<ActionResult<JobSummary>> ClearJob(Guid partyId, Guid participantId)
    {
        var who = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is null) return NotFound();
        return Ok(Summary(who.Id, who.Name, await Occupation(partyId, participantId).SetAsync(null)));
    }

    private IOccupationGrain Occupation(Guid partyId, Guid who) => grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who));

    private static JobSummary Summary(Guid id, string name, OccupationView view) => new(
        id, name, view.Seeded,
        view.Job?.Title, view.Job?.Workplace, view.Job?.Onscreen ?? false,
        view.Job is { } j ? Shifts.DaysText(j.Days) : null,
        view.Job is { } h ? $"{Shifts.Clock(h.Start)}-{Shifts.Clock(h.End)}" : null,
        view.Today?.Day, view.Today?.Status.ToString(), view.Today?.LateMinutes ?? 0, view.Today?.Paid ?? 0);
}

public sealed record JobRequest(string Title, string Workplace, string? Days, string Hours);

/// <param name="Seeded">False while the job is still being read off the persona's sheet.</param>
/// <param name="ShiftStatus">Last shift: On, Done, LeftEarly or Missed.</param>
public sealed record JobSummary(
    Guid ParticipantId,
    string Name,
    bool Seeded,
    string? Title,
    string? Workplace,
    bool Onscreen,
    string? Days,
    string? Hours,
    string? ShiftDay,
    string? ShiftStatus,
    int LateMinutes,
    int Paid);
