using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Works;

/// <summary>Where a Works Company meets the Runner (ADR 0039): the I/O around <see cref="ICommissionsGrain"/>, and word reaching its staff.</summary>
public sealed class Worksite(IGrainFactory grains, IRunner runner, Desks desks, WorldMemory memory, ILogger<Worksite> logger)
{
    private static readonly TimeSpan StatusFresh = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartGrace = TimeSpan.FromMinutes(5);
    private const int ToldMax = 400;
    private RunnerStatus? _status;
    private DateTimeOffset _statusAt;

    public bool Enabled => runner.Enabled;

    /// <summary>Founds a Works Company with an office of its own; null when the name is taken.</summary>
    public async Task<Company?> FoundAsync(Guid partyId, string name, string mandate)
    {
        var workplace = await desks.OfficeAsync(partyId, name);
        return await grains.GetGrain<ICompaniesGrain>(partyId).FoundAsync(new Company
        {
            Id = Guid.NewGuid(), Name = name, Mandate = mandate, Kind = CompanyKinds.Works, Market = string.Empty,
            Workplace = workplace, FoundedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    /// <summary>Null when the builder can't be reached; a quiet desk, not a broken one.</summary>
    public async Task<RunnerStatus?> StatusAsync(CancellationToken ct, bool fresh = false)
    {
        if (!runner.Enabled) return null;
        if (!fresh && _status is not null && DateTimeOffset.UtcNow - _statusAt < StatusFresh) return _status;
        try
        {
            _status = await runner.StatusAsync(ct);
            _statusAt = DateTimeOffset.UtcNow;
            return _status;
        }
        catch (RunnerException ex)
        {
            logger.LogDebug(ex, "The Runner isn't answering");
            _status = null;
            return null;
        }
    }

    /// <summary>Files and starts an order. Null means the bench is taken; a refused start ends as a Failed Commission.</summary>
    public async Task<Commission?> OrderAsync(Guid partyId, Company company, ParticipantView architect, CommissionKind kind, CommissionDraft draft, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var commission = await Commissions(partyId).FileAsync(new Commission
        {
            Id = Guid.NewGuid(), CompanyId = company.Id, ArchitectId = architect.Id, ArchitectName = architect.Name, Kind = kind,
            Title = draft.Title, Why = draft.Why, Brief = draft.Brief, Model = draft.Model, BudgetUsd = draft.BudgetUsd,
            FiledAt = now, UpdatedAt = now,
        });
        return commission is null ? null : await StartAsync(partyId, company, commission, ct);
    }

    /// <summary>Starting is idempotent per Commission, so an unanswered start stays Filed and the watch asks again.</summary>
    private async Task<Commission?> StartAsync(Guid partyId, Company company, Commission commission, CancellationToken ct)
    {
        try
        {
            var kind = commission.Kind == CommissionKind.Survey ? "survey" : "change";
            var run = await runner.StartAsync(commission.Id, kind, WorksScreen.Order(company, commission), commission.Model, commission.BudgetUsd, ct);
            _status = null;
            return await Commissions(partyId).StartedAsync(commission.Id, run.Id);
        }
        catch (RunnerException ex) when (ex.Refused)
        {
            return await Commissions(partyId).MarkAsync(commission.Id, CommissionStatus.Failed, ex.Message);
        }
        catch (RunnerException ex)
        {
            logger.LogInformation("No answer starting {Commission}, asking again later: {Why}", commission.Id, ex.Message);
            return commission;
        }
    }

    /// <summary>Asks again for Commissions whose start went unanswered, until it's clearly not coming.</summary>
    private async Task RetryStartsAsync(Guid partyId, IEnumerable<Commission> filed, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var commission in filed)
        {
            if (now - commission.FiledAt > StartGrace.TotalMilliseconds)
            {
                await Commissions(partyId).MarkAsync(commission.Id, CommissionStatus.Failed, "The builder never started.");
                continue;
            }
            if (await grains.GetGrain<ICompaniesGrain>(partyId).GetAsync(commission.CompanyId) is { } company)
                await StartAsync(partyId, company, commission, ct);
        }
    }

    public async Task<bool> SteerAsync(Guid partyId, Commission commission, string message, double addUsd, CancellationToken ct)
        => await TryAsync(commission, CommissionStatus.Working, partyId, () => runner.PromptAsync(commission.RunId!, message, addUsd, ct));

    public async Task<bool> ProposeAsync(Guid partyId, Commission commission, CancellationToken ct)
        => await TryAsync(commission, CommissionStatus.Checking, partyId, () => runner.ProposeAsync(commission.RunId!, commission.Title, commission.Why, commission.ArchitectName, ct));

    public async Task<bool> DropAsync(Guid partyId, Commission commission, CancellationToken ct)
        => await TryAsync(commission, CommissionStatus.Dropped, partyId, () => runner.DropAsync(commission.RunId!, ct));

    private async Task<bool> TryAsync(Commission commission, CommissionStatus next, Guid partyId, Func<Task<RunnerRun>> act)
    {
        if (commission.RunId is null) return false;
        try
        {
            await act();
            _status = null;
            await Commissions(partyId).MarkAsync(commission.Id, next);
            return true;
        }
        catch (RunnerException ex)
        {
            logger.LogInformation("The Runner refused a move on {Commission}: {Why}", commission.Id, ex.Message);
            return false;
        }
    }

    /// <summary>The world-spirits' no; a yes needs no call, it's read from the repo once merged.</summary>
    public async Task<bool> RejectAsync(Guid partyId, Commission commission, string? note, CancellationToken ct)
    {
        if (commission.Proposition is not { } number) return false;
        await runner.RejectAsync(number, note, ct);
        await ReadVerdictsAsync(partyId, ct);
        return true;
    }

    /// <summary>Reads one pass of news from the Runner for this Party, and pushes it to spectators and staff.</summary>
    public async Task WatchAsync(Guid partyId, bool verdicts, CancellationToken ct)
    {
        var watched = (await Commissions(partyId).GetAllAsync()).Where(CommissionRules.IsWatched).ToList();
        foreach (var commission in watched.Where(c => c.RunId is not null && CommissionRules.IsOpen(c)))
        {
            try { await ReadRunAsync(partyId, commission, ct); }
            catch (RunnerException ex) { logger.LogDebug(ex, "Couldn't read run {Run}", commission.RunId); }
        }
        await RetryStartsAsync(partyId, watched.Where(c => c.RunId is null && c.Status == CommissionStatus.Filed), ct);
        if (verdicts && watched.Any(c => c.Status == CommissionStatus.Proposed))
        {
            try { await ReadVerdictsAsync(partyId, ct); }
            catch (RunnerException ex) { logger.LogDebug(ex, "Couldn't read propositions"); }
        }
    }

    private async Task ReadRunAsync(Guid partyId, Commission commission, CancellationToken ct)
    {
        var read = await runner.ReadAsync(commission.RunId!, commission.Cursor, ct);
        var lines = read.Events.Select(e => new WorksLine(e.N, e.At, e.Kind, e.Text)).ToList();
        var run = read.Run;
        var facts = new CommissionFacts(
            CommissionRules.StatusOf(run.Status), run.Usd, run.Tokens, run.Report,
            run.Diff?.Files ?? 0, run.Diff?.Added ?? 0, run.Diff?.Removed ?? 0, run.Diff?.Paths ?? new List<string>(),
            run.Stop, run.Error, run.Proposition,
            run.Checks.Select(c => new CommissionCheck(c.Name, c.Passed, c.Seconds, c.Detail)).ToList(),
            lines.LastOrDefault()?.N ?? commission.Cursor);
        var moved = facts.Status != commission.Status;
        if (!moved && lines.Count == 0 && facts.SpentUsd == commission.SpentUsd) return;
        if (await Commissions(partyId).SyncAsync(commission.Id, facts, lines) is { News: not CommissionNews.None } update) await TellAsync(partyId, update);
    }

    private async Task ReadVerdictsAsync(Guid partyId, CancellationToken ct)
    {
        var all = await Commissions(partyId).GetAllAsync();
        foreach (var proposition in await runner.PropositionsAsync(ct))
        {
            if (!Guid.TryParse(proposition.Commission, out var id) || all.FirstOrDefault(c => c.Id == id) is not { Status: CommissionStatus.Proposed }) continue;
            if (await Commissions(partyId).VerdictAsync(id, proposition.Status, proposition.Note) is { News: not CommissionNews.None } update) await TellAsync(partyId, update);
        }
    }

    /// <summary>The bench's news reaches staff as work memory, never the wider world: the Works is a secret (ADR 0039).</summary>
    private async Task TellAsync(Guid partyId, CommissionUpdate update)
    {
        var c = update.Commission;
        var text = Told(update);
        var clock = grains.GetGrain<IWorldClockGrain>(partyId);
        await clock.RecordAsync(new WorldActivity { PersonaName = c.ArchitectName, App = WorksScreen.AppId, Action = "result", Summary = text, TargetId = c.Id });
        var staff = await desks.StaffAsync(partyId, c.CompanyId);
        var told = update.News == CommissionNews.Answered ? staff.Where(s => s.Id == c.ArchitectId).ToList() : staff;
        if (told.Count == 0) return;
        if (update.News is CommissionNews.Accepted or CommissionNews.Rejected) await MoveMoodsAsync(partyId, told, update.News == CommissionNews.Accepted, c);
        await clock.NotifyAsync(told.Select(s => s.Id).ToList());
        var moment = new List<MomentLine> { new(Narrator.PersonaId, $"[At work] {text}", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) };
        _ = memory.CaptureAsync(new Moment(partyId, MomentSources.Work, c.CompanyId, moment, moment.Count - 1, told), CancellationToken.None);
    }

    private async Task MoveMoodsAsync(Guid partyId, List<ParticipantView> staff, bool taken, Commission c)
    {
        foreach (var person in staff)
        {
            try
            {
                await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(partyId, person.Id))
                    .ShiftValenceAsync(taken ? 0.7 : 0.3, $"the world-spirits and \"{c.Title}\"");
            }
            catch (Exception ex) { logger.LogDebug(ex, "Mood from a verdict failed for {Who}", person.Id); }
        }
    }

    private static string Told(CommissionUpdate update)
    {
        var c = update.Commission;
        return update.News switch
        {
            CommissionNews.TurnDone => c.Files == 0
                ? $"The builder stopped on \"{c.Title}\" with nothing changed, {WorksScreen.Money(c.SpentUsd)} spent."
                : $"The builder stopped on \"{c.Title}\": {c.Files} files changed, {WorksScreen.Money(c.SpentUsd)} spent.",
            CommissionNews.Answered => $"The builder answered \"{c.Title}\": {WorldStream.Clip(c.Report, ToldMax)}",
            CommissionNews.Proposed => $"\"{c.Title}\" went to the world-spirits, {WorksScreen.ChecksLine(c)}.",
            CommissionNews.Accepted => $"The world-spirits took \"{c.Title}\".",
            CommissionNews.Rejected => c.Status == CommissionStatus.Fallen
                ? $"\"{c.Title}\" fell: the world-spirits turned down what it stood on."
                : $"The world-spirits turned down \"{c.Title}\"{(string.IsNullOrWhiteSpace(c.Note) ? "." : $": {WorldStream.Clip(c.Note, ToldMax)}")}",
            _ => $"The builder fell over on \"{c.Title}\".",
        };
    }

    private ICommissionsGrain Commissions(Guid partyId) => grains.GetGrain<ICommissionsGrain>(partyId);
}

/// <summary>Carries the Runner's news in, outside the World clock so a paused World still shows its builder working.</summary>
public sealed class WorksWatch(IGrainFactory grains, Worksite site, ILogger<WorksWatch> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
    private const int VerdictEvery = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!site.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        var beat = 0;
        do
        {
            try
            {
                var parties = await grains.GetGrain<IPartyRootGrain>(Guid.Empty).GetAll();
                var verdicts = beat++ % VerdictEvery == 0;
                foreach (var party in parties) await site.WatchAsync(party.Id, verdicts, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Works watch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
