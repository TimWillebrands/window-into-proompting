using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Work;

namespace PartyTown.Controllers;

[ApiController]
[Route("worlds/{partyId:guid}")]
public sealed class WorkController(IGrainFactory grains, Actors actors) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("jobs")]
    public async Task<ActionResult<List<JobSummary>>> GetJobs(Guid partyId)
    {
        var cast = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Where(c => c.Id != Narrator.PersonaId).ToList();
        var views = await Task.WhenAll(cast.Select(c => Occupation(partyId, c.Id).GetAsync()));
        var places = (await grains.GetGrain<World.Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Places;
        foreach (var job in views.Select(v => v.Job).OfType<Occupation>())
            if (places.FirstOrDefault(p => p.Id == (job.PlaceId ?? job.LocaleId)) is { } place) WorkRules.Sync(job, place);
        return Ok(cast.Select((c, i) => Summary(c.Id, c.Name, views[i])).ToList());
    }

    [HttpPut("jobs/{participantId:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobSummary>> SetJob(Guid partyId, Guid participantId, [FromBody] JobRequest request)
    {
        await actors.ActAsync(User, partyId, participantId);
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
        await actors.ActAsync(User, partyId, participantId);
        var who = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is null) return NotFound();
        return Ok(Summary(who.Id, who.Name, await Occupation(partyId, participantId).SetAsync(null)));
    }

    [AllowAnonymous]
    [HttpGet("jobs/{participantId:guid}/clothes")]
    public async Task<ActionResult<WorkClothesView>> GetClothes(Guid partyId, Guid participantId)
        => Ok(await ClothesAsync(partyId, participantId, await Occupation(partyId, participantId).GetAsync()));

    [HttpPost("jobs/{participantId:guid}/clothes")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkClothesView>> TailorClothes(Guid partyId, Guid participantId, [FromBody] WorkClothesRequest request)
    {
        await actors.ActAsync(User, partyId, participantId);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 300) return BadRequest("Say what to wear for work, under 300 characters.");
        var occupation = Occupation(partyId, participantId);
        if ((await occupation.GetAsync()).Job is null) return BadRequest("Work clothes need a job.");
        return Ok(await ClothesAsync(partyId, participantId, await occupation.TailorAsync(request.Text.Trim())));
    }

    [HttpDelete("jobs/{participantId:guid}/clothes")]
    public async Task<ActionResult<WorkClothesView>> ClearClothes(Guid partyId, Guid participantId)
    {
        await actors.ActAsync(User, partyId, participantId);
        return Ok(await ClothesAsync(partyId, participantId, await Occupation(partyId, participantId).SetClothesAsync(null)));
    }

    private async Task<WorkClothesView> ClothesAsync(Guid partyId, Guid participantId, OccupationView view)
    {
        var body = (await grains.GetGrain<World.Places.IPlacesGrain>(partyId).GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == participantId);
        var clothes = view.Job?.Clothes;
        return new WorkClothesView(view.Job?.Title, clothes is not null, clothes?.OwnClothes ?? false, clothes?.Summary, body?.WorkClothes is not null, view.OnShift);
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

public sealed record WorkClothesRequest(string Text);

public sealed record WorkClothesView(string? Title, bool Designed, bool OwnClothes, string? Summary, bool Wearing, bool OnShift);

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
