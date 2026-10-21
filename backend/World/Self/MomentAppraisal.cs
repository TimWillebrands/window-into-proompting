using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.SystemOne;

namespace PartyTown.World.Self;

/// <summary>
/// Conversations move moods (ADR 0021): when a Moment closes, System One asks per present persona whether it
/// left them better or worse off, and nudges their Self's valence. Detached and non-fatal.
/// </summary>
public sealed class MomentAppraisal(ISystemOneJudge judge, IGrainFactory grains, ILogger<MomentAppraisal> logger)
{
    private const int MaxLines = 14;

    private async Task<string> WhereAsync(Moment moment)
    {
        if (moment.Source == MomentSources.Board) return "the Board";
        var room = (await grains.GetGrain<IPartyGrain>(moment.PartyId).GetChatGroups()).FirstOrDefault(r => r.Id == moment.ThreadId);
        return room is null ? "a conversation" : room.Kind == "dm" ? $"your chat with {room.Name}" : $"#{room.Name}";
    }

    /// <param name="who">Personas who took a recollection away from it.</param>
    public async Task AppraiseAsync(Moment moment, IReadOnlySet<Guid> who)
    {
        if (!judge.Enabled) return;
        var personas = moment.Present.Where(p => p.Driver == DriverKind.LLM && who.Contains(p.Id)).ToList();
        if (personas.Count == 0) return;
        try
        {
            var where = await WhereAsync(moment);
            var names = moment.Present.ToDictionary(p => p.Id, p => p.Name);
            var transcript = string.Join("\n", moment.Lines.TakeLast(MaxLines)
                .Select(l => $"{names.GetValueOrDefault(l.SenderId, "Someone")}: {WorldStream.Clip(l.Text, 240)}"));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var answers = await judge.AskAsync(transcript, personas.ToDictionary(
                p => p.Id.ToString("N"),
                p => (SystemOneQuestion)new NoulQuestion($"This conversation left {p.Name} in a better mood than before it.")), cts.Token);
            foreach (var p in personas)
            {
                var better = answers.Noul(p.Id.ToString("N"), 0.5);
                await grains.GetGrain<IPersonaSelfGrain>(WorldKeys.Life(moment.PartyId, p.Id)).ShiftValenceAsync(better, where);
            }
            logger.LogInformation("Appraisal in {Where}: {Values}", where,
                string.Join(", ", personas.Select(p => $"{p.Name}={answers.Noul(p.Id.ToString("N"), 0.5):F2}")));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Moment appraisal failed in {ThreadId}", moment.ThreadId);
        }
    }
}
