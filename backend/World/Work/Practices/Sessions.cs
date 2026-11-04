using System.Collections.Concurrent;
using System.Globalization;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.World.Economy;
using PartyTown.World.Places;
using PartyTown.World.Work.Markets;

namespace PartyTown.World.Work.Practices;

/// <summary>A Session in the World (ADR 0030): the booking, getting two people together, and the bill. Life and HTTP both book through here.</summary>
public sealed class Sessions(IGrainFactory grains, IWorldLlm llm, ILogger<Sessions> logger)
{
    private const string Shape = """{"homework": "<one concrete thing for the client to do, or none>", "days": "<days until it's due>", "kept": "<yes or no: did they do what they were going to do last time; none if there was nothing>"}""";
    private static readonly TimeSpan CloseBudget = TimeSpan.FromSeconds(90);
    private readonly ConcurrentDictionary<Guid, byte> _stepping = new();

    /// <summary>A booking, or why not. The same rules whoever is driving either side.</summary>
    public async Task<(PracticeSession? Session, string? Refusal)> BookAsync(Guid partyId, Guid clientId, Guid practitionerId, string reason, DateTimeOffset now)
    {
        if (clientId == practitionerId) return (null, "You can't book yourself.");
        var view = await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, practitionerId)).GetAsync();
        if (view.Job is not { Practice: { } practice } job) return (null, "They don't take clients.");
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        if (cast.FirstOrDefault(c => c.Id == clientId) is not { } client || cast.FirstOrDefault(c => c.Id == practitionerId) is not { } practitioner)
            return (null, "They aren't here.");
        if ((await grains.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, clientId)).GetAsync()).Balance < practice.Fee)
            return (null, $"{client.Name} can't afford {practice.Fee} credits.");

        var book = grains.GetGrain<ISessionsGrain>(partyId);
        var sessions = await book.GetSessionsAsync();
        if (sessions.Any(s => s.Status is SessionStatus.Booked or SessionStatus.Running && s.ClientId == clientId && s.PractitionerId == practitionerId))
            return (null, "They already have a session booked.");
        var mine = sessions.Where(s => s.PractitionerId == practitionerId || s.PractitionerId == clientId || s.ClientId == practitionerId || s.ClientId == clientId);
        if (PracticeRules.NextSlot(job, mine, now.ToLocalTime()) is not { } slot) return (null, $"{practitioner.Name} has nothing free this week.");

        return (await book.BookAsync(new PracticeSession
        {
            Id = Guid.NewGuid(), PractitionerId = practitionerId, PractitionerName = practitioner.Name, ClientId = clientId, ClientName = client.Name,
            Workplace = job.Workplace, Reason = MarketText.Clean(reason, 300), StartsAt = slot, Fee = practice.Fee,
        }), null);
    }

    /// <summary>The clock's step: starts what's due and closes what's over, whether or not anyone picks it.</summary>
    public async Task StepAsync(Guid partyId, DateTimeOffset now)
    {
        if (!_stepping.TryAdd(partyId, 0)) return;
        try
        {
            var ms = now.ToUnixTimeMilliseconds();
            foreach (var session in await grains.GetGrain<ISessionsGrain>(partyId).GetSessionsAsync())
            {
                if (session.Status == SessionStatus.Booked && session.StartsAt <= ms) await StartAsync(partyId, session, now);
                else if (session.Status == SessionStatus.Running)
                {
                    if (!session.Opened) await OpenIfTogetherAsync(partyId, session);
                    await CloseIfOverAsync(partyId, session, now);
                }
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Session step failed"); }
        finally { _stepping.TryRemove(partyId, out _); }
    }

    /// <summary>The Client joins the Practitioner, by the same rules as anyone joining anyone; a Locale opens as for a Gathering.</summary>
    private async Task StartAsync(Guid partyId, PracticeSession session, DateTimeOffset now)
    {
        var places = grains.GetGrain<IPlacesGrain>(partyId);
        var job = (await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, session.PractitionerId)).GetAsync()).Job;
        var asleep = await places.GetAsleepAsync();
        if (job?.Practice is null || (job.PlaceId ?? job.LocaleId) is not { } where || asleep.Contains(session.ClientId) || asleep.Contains(session.PractitionerId))
        {
            await MissAsync(partyId, session, now);
            return;
        }
        await places.OpenAsync(where, null, "gathering");
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        // Humans come themselves, as with Gatherings; a Persona's post is where a shift would put them.
        bool Llm(Guid id) => cast.FirstOrDefault(c => c.Id == id)?.DefaultDriver == DriverKind.LLM;
        if (Llm(session.PractitionerId)) await places.GoAwayAsync(session.PractitionerId, where, "working");
        if (Llm(session.ClientId)) await places.JoinAsync(session.ClientId, session.PractitionerId);
        if (await grains.GetGrain<ISessionsGrain>(partyId).StartAsync(session.Id, now.ToUnixTimeMilliseconds()))
            await RecordAsync(partyId, session, "start", $"has {session.ClientName} in for a session at {session.Workplace}");
    }

    /// <summary>Once they're in Earshot, whoever an LLM drives says the first thing; two humans just talk.</summary>
    private async Task OpenIfTogetherAsync(Guid partyId, PracticeSession session)
    {
        var snapshot = await grains.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync();
        if (!PracticeRules.Together(snapshot.Bodies, session.PractitionerId, session.ClientId)) return;
        // Wherever they actually met; a Place only gets its talk room once someone's there.
        var here = snapshot.Bodies.First(b => b.ParticipantId == session.ClientId).PlaceId;
        if (snapshot.Places.FirstOrDefault(p => p.Id == here)?.TalkRoomId is not { } talkRoom) return;
        var from = (await grains.GetGrain<IChatGroupGrain>(talkRoom).GetRecentMessagesAsync(1)).LastOrDefault()?.MessageId ?? -1;
        if (!await grains.GetGrain<ISessionsGrain>(partyId).OpenAsync(session.Id, talkRoom, from)) return;
        var cast = await grains.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        bool Llm(Guid id) => cast.FirstOrDefault(c => c.Id == id)?.DefaultDriver == DriverKind.LLM;
        var (who, intent) = Llm(session.PractitionerId)
            ? (session.PractitionerId, PracticeRules.Opening(session))
            : (session.ClientId, $"Tell {session.PractitionerName} why you came: {session.Reason}");
        if (!Llm(who)) return;
        try { await grains.GetGrain<IPersonaGrain>(who).InitiateAsync(talkRoom, intent, null); }
        catch (Exception ex) { logger.LogWarning(ex, "Opening session {Session} failed", session.Id); }
    }

    private async Task CloseIfOverAsync(Guid partyId, PracticeSession session, DateTimeOffset now)
    {
        var lines = await LinesAsync(session);
        if (!PracticeRules.Over(session, lines.Count, lines.LastOrDefault()?.SendAt, now.ToUnixTimeMilliseconds())) return;
        if (!PracticeRules.TookPlace(session, lines.Select(l => l.SenderId)))
        {
            await MissAsync(partyId, session, now);
            return;
        }
        // Flip first: a crash after this can lose Homework, never charge twice.
        if (!await grains.GetGrain<ISessionsGrain>(partyId).EndAsync(session.Id, SessionStatus.Done, now.ToUnixTimeMilliseconds())) return;
        var paid = await ChargeAsync(partyId, session);
        await RecordAsync(partyId, session, "finish", paid
            ? $"finished a session with {session.ClientName} (+{session.Fee} credits)"
            : $"finished a session with {session.ClientName}, who couldn't pay");
        using var cts = new CancellationTokenSource(CloseBudget);
        try { await LeaveHomeworkAsync(partyId, session, lines, cts.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Homework failed for session {Session}", session.Id); }
    }

    private async Task<bool> ChargeAsync(Guid partyId, PracticeSession session)
    {
        var client = grains.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, session.ClientId));
        if (!await client.SpendAsync(session.Fee, $"session with {session.PractitionerName}")) return false;
        await grains.GetGrain<IWalletGrain>(WorldKeys.Life(partyId, session.PractitionerId)).EarnAsync(session.Fee, $"session with {session.ClientName}");
        await grains.GetGrain<ISessionsGrain>(partyId).NotePaidAsync(session.Id);
        return true;
    }

    private async Task LeaveHomeworkAsync(Guid partyId, PracticeSession session, List<ChatMessage> lines, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string> { [session.PractitionerId] = session.PractitionerName, [session.ClientId] = session.ClientName };
        var transcript = string.Join("\n", lines.Select(l => $"{names.GetValueOrDefault(l.SenderId, "someone")}: {l.Content}"));
        var json = await llm.CompleteJsonAsync(JobComplexity.General,
            $"You read the transcript of a session between {session.PractitionerName} and their client {session.ClientName}. Write down what it left {session.ClientName} to do: one concrete, checkable thing they agreed to, in plain words, starting with a verb. If nothing was agreed, say none.\nOutput ONLY JSON: {Shape}",
            transcript, ct, 0.2, new[] { "homework", "days", "kept" });
        var text = json?["homework"]?.ToString().Trim();
        var days = int.TryParse(new string((json?["days"]?.ToString() ?? string.Empty).Where(char.IsDigit).ToArray()), NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)
            ? Math.Clamp(d, 1, 14) : PracticeRules.DefaultHomeworkDays;
        var next = string.IsNullOrWhiteSpace(text) || text.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : new Homework
        {
            Id = Guid.NewGuid(), SessionId = session.Id, ClientId = session.ClientId, PractitionerId = session.PractitionerId,
            Text = WorldStream.Clip(text, 200), DueAt = DateTimeOffset.UtcNow.AddDays(days).ToUnixTimeMilliseconds(),
        };
        var kept = json?["kept"]?.ToString().Trim().ToLowerInvariant() switch { "yes" => true, "no" => false, _ => (bool?)null };
        await grains.GetGrain<ISessionsGrain>(partyId).SetHomeworkAsync(next, session.ClientId, session.PractitionerId, kept);
    }

    private async Task MissAsync(Guid partyId, PracticeSession session, DateTimeOffset now)
    {
        if (!await grains.GetGrain<ISessionsGrain>(partyId).EndAsync(session.Id, SessionStatus.Missed, now.ToUnixTimeMilliseconds())) return;
        await RecordAsync(partyId, session, "missed", $"didn't get to see {session.ClientName}");
    }

    /// <summary>What the two of them said since it started; anyone else at the Spot isn't part of the Session.</summary>
    private async Task<List<ChatMessage>> LinesAsync(PracticeSession session)
    {
        if (session.TalkRoomId is not { } talkRoom) return new List<ChatMessage>();
        return (await grains.GetGrain<IChatGroupGrain>(talkRoom).GetRecentMessagesAsync(PracticeRules.MaxLines * 4))
            .Where(m => m.MessageId > session.FromMessageId && m.Kind is null && !string.IsNullOrWhiteSpace(m.Content))
            .Where(m => m.SenderId == session.PractitionerId || m.SenderId == session.ClientId)
            .ToList();
    }

    /// <summary>The log says a Session happened, never what was said in it.</summary>
    private Task RecordAsync(Guid partyId, PracticeSession session, string action, string summary)
        => grains.GetGrain<IWorldClockGrain>(partyId).RecordAsync(new WorldActivity
        {
            PersonaId = session.PractitionerId, PersonaName = session.PractitionerName, App = PracticeApp.AppId, Action = action,
            Summary = summary, WithPersonaId = session.ClientId,
        });
}
