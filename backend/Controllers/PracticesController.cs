using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World;
using PartyTown.World.Work;
using PartyTown.World.Work.Practices;

namespace PartyTown.Controllers;

/// <summary>Practices (ADR 0030): set someone up to take Clients, see who's booked, and book as anyone.</summary>
[ApiController]
[Route("worlds/{partyId:guid}")]
public sealed class PracticesController(IGrainFactory grains, Sessions sessions, Actors actors) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("practices")]
    public async Task<ActionResult<List<PracticeSummary>>> GetPractices(Guid partyId)
    {
        var cast = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).Where(c => c.Id != Narrator.PersonaId).ToList();
        var views = await Task.WhenAll(cast.Select(c => Occupation(partyId, c.Id).GetAsync()));
        return Ok(cast.Select((c, i) => (Who: c, Job: views[i].Job))
            .Where(p => p.Job?.Practice is not null)
            .Select(p => Summary(p.Who.Id, p.Who.Name, p.Job!))
            .ToList());
    }

    /// <summary>"Persona xyz starts as a career coach for logistics". Days like "Mon-Fri"; hours like "09:00-17:00".</summary>
    [HttpPut("practices/{participantId:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PracticeSummary>> SetUp(Guid partyId, Guid participantId, [FromBody] PracticeRequest request)
    {
        await actors.ActAsync(User, partyId, participantId);
        var who = (await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync()).FirstOrDefault(c => c.Id == participantId);
        if (who is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Workplace) || string.IsNullOrWhiteSpace(request.Field))
            return BadRequest("A Practice needs a title, a workplace and a field.");
        if (request.Fee is < PracticeRules.MinFee or > PracticeRules.MaxFee) return BadRequest($"A Fee is {PracticeRules.MinFee} to {PracticeRules.MaxFee} credits.");
        if (Shifts.ParseHours(request.Hours) is not var (start, end)) return BadRequest("Hours like 09:00-17:00, within one day.");
        var view = await Occupation(partyId, participantId).SetAsync(new Occupation
        {
            Title = request.Title.Trim(), Workplace = request.Workplace.Trim(), Days = Shifts.ParseDays(request.Days), Start = start, End = end,
            Practice = new Practice { Field = request.Field.Trim(), Approach = request.Approach?.Trim() ?? string.Empty, Fee = request.Fee },
        });
        return Ok(Summary(who.Id, who.Name, view.Job!));
    }

    [AllowAnonymous]
    [HttpGet("sessions")]
    public async Task<ActionResult<List<SessionSummary>>> GetSessions(Guid partyId)
    {
        var book = grains.GetGrain<ISessionsGrain>(partyId);
        var homework = await book.GetHomeworkAsync();
        return Ok((await book.GetSessionsAsync()).OrderByDescending(s => s.StartsAt).Select(s => Of(s, homework)).ToList());
    }

    /// <summary>Book the next free slot with a Practitioner, as any Client.</summary>
    [HttpPost("practices/{participantId:guid}/sessions")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SessionSummary>> Book(Guid partyId, Guid participantId, [FromBody] BookSessionRequest request)
    {
        await actors.ActAsync(User, partyId, request.ClientId);
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest("Say why you're coming.");
        var (session, refusal) = await sessions.BookAsync(partyId, request.ClientId, participantId, request.Reason, DateTimeOffset.UtcNow);
        if (session is null) return BadRequest(refusal);
        await grains.GetGrain<IWorldClockGrain>(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = session.ClientId, PersonaName = session.ClientName, App = PracticeApp.AppId, Action = "book",
            Summary = $"booked a session with {session.PractitionerName}", WithPersonaId = session.PractitionerId,
        });
        return Ok(Of(session, new List<Homework>()));
    }

    private IOccupationGrain Occupation(Guid partyId, Guid who) => grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, who));

    private static PracticeSummary Summary(Guid id, string name, Occupation job) => new(
        id, name, job.Title, job.Workplace, Shifts.DaysText(job.Days), $"{Shifts.Clock(job.Start)}-{Shifts.Clock(job.End)}",
        job.Practice!.Field, job.Practice.Approach, job.Practice.Fee);

    private static SessionSummary Of(PracticeSession s, List<Homework> homework) => new(
        s.Id, s.PractitionerId, s.PractitionerName, s.ClientId, s.ClientName, s.Workplace, s.Reason, s.StartsAt, s.StartedAt, s.EndedAt,
        s.Status.ToString(), s.Fee, s.Paid, homework.FirstOrDefault(h => h.SessionId == s.Id)?.Text, homework.FirstOrDefault(h => h.SessionId == s.Id)?.Kept);
}

public sealed record PracticeRequest(string Title, string Workplace, string Field, string? Approach, int Fee, string? Days, string Hours);

public sealed record BookSessionRequest(Guid ClientId, string Reason);

public sealed record PracticeSummary(Guid ParticipantId, string Name, string Title, string Workplace, string Days, string Hours, string Field, string Approach, int Fee);

/// <param name="Status">Booked, Running, Done or Missed.</param>
public sealed record SessionSummary(
    Guid Id, Guid PractitionerId, string PractitionerName, Guid ClientId, string ClientName, string Workplace, string Reason,
    long StartsAt, long? StartedAt, long? EndedAt, string Status, int Fee, bool Paid, string? Homework, bool? HomeworkKept);
