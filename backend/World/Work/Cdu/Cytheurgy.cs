using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.World.Work.Companies;

namespace PartyTown.World.Work.Cdu;

/// <summary>Where the World meets the CDU (ADR 0039): the I/O around <see cref="ICduGrain"/>, the tide, and word reaching staff.</summary>
public sealed class Cytheurgy(IGrainFactory grains, ICdu cdu, Desks desks, WorldMemory memory, ILogger<Cytheurgy> logger)
{
    private static readonly TimeSpan StatusFresh = TimeSpan.FromSeconds(15);
    private CduStatus? _status;
    private DateTimeOffset _statusAt;

    public bool Enabled => cdu.Enabled;

    /// <summary>Founds a CDU Company with an office of its own; null when the name is taken.</summary>
    public async Task<Company?> FoundAsync(Guid partyId, string name, string mandate)
    {
        var workplace = await desks.OfficeAsync(partyId, name);
        return await grains.GetGrain<ICompaniesGrain>(partyId).FoundAsync(new Company
        {
            Id = Guid.NewGuid(), Name = name, Mandate = mandate, Kind = CompanyKinds.Cdu, Market = string.Empty,
            Workplace = workplace, FoundedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });
    }

    /// <summary>Null when the CDU can't be reached: a dark desk, not a broken one.</summary>
    public async Task<CduStatus?> StatusAsync(CancellationToken ct, bool fresh = false)
    {
        if (!cdu.Enabled) return null;
        if (!fresh && _status is not null && DateTimeOffset.UtcNow - _statusAt < StatusFresh) return _status;
        try
        {
            _status = await cdu.StatusAsync(ct);
            _statusAt = DateTimeOffset.UtcNow;
            return _status;
        }
        catch (CduException ex)
        {
            logger.LogDebug(ex, "The CDU isn't answering");
            _status = null;
            return null;
        }
    }

    /// <summary>Says something to the CDU for a Cytheurgist; null when it couldn't be reached.</summary>
    public async Task<CduExchange?> SayAsync(Guid partyId, Company company, ParticipantView speaker, string text, string kind, string? model, CancellationToken ct)
    {
        try
        {
            var turn = await cdu.TalkAsync(new CduSpeaker(speaker.Id.ToString(), speaker.Name), text, kind, model, ct);
            var exchange = new CduExchange
            {
                TurnId = turn.Id, CompanyId = company.Id, SpeakerId = speaker.Id, SpeakerName = speaker.Name, Kind = kind, Text = turn.Text, Model = model,
                At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };
            await Grain(partyId).SpokeAsync(exchange);
            _status = null;
            return exchange;
        }
        catch (CduException ex)
        {
            logger.LogInformation("Couldn't reach the CDU for {Who}: {Why}", speaker.Name, ex.Message);
            return null;
        }
    }

    /// <summary>A look becomes the looker's own work memory: what git and GitHub say, not what the CDU claimed.</summary>
    public async Task<string> LookAsync(Guid partyId, Company company, ParticipantView looker, CancellationToken ct)
    {
        var seen = CduScreen.Look(await StatusAsync(ct, fresh: true));
        Remember(partyId, company.Id, new List<ParticipantView> { looker }, seen);
        return seen;
    }

    /// <summary>One pass for this Party: lines for spectators, news for staff, and the tide on shift edges.</summary>
    public async Task WatchAsync(Guid partyId, CancellationToken ct)
    {
        var companies = (await grains.GetGrain<ICompaniesGrain>(partyId).GetAllAsync()).Where(c => CompanyKinds.IsCdu(c.Kind)).ToList();
        if (companies.Count == 0) return;
        var grain = Grain(partyId);
        var state = await grain.GetAsync();
        try
        {
            // A Party new to the CDU starts from now: older news happened to other people.
            if (state is { News: 0, Lines: 0, Exchanges.Count: 0 })
            {
                if (await StatusAsync(ct, fresh: true) is not { } now) return;
                await grain.CatchUpAsync(now.Lines, now.News);
                state = await grain.GetAsync();
            }
            var lines = await cdu.LinesAsync(state.Lines, ct);
            await grain.SawAsync(lines.Select(l => new CduLine(l.N, l.At, l.Kind, l.Text)).ToList());
            var news = await cdu.NewsAsync(state.News, ct);
            if (news.Count > 0)
            {
                var told = await grain.HeardAsync(news.Select(Flatten).ToList());
                if (told.Count > 0) _status = null;
                foreach (var word in told) await TellAsync(partyId, companies, word);
            }
            await TideAsync(partyId, companies, state.OnShift, ct);
        }
        catch (CduException ex)
        {
            logger.LogDebug(ex, "Couldn't read the CDU for {Party}", partyId);
        }
    }

    /// <summary>The edge is kept only once the CDU took the call, so a dark CDU gets it again next beat.</summary>
    private async Task TideAsync(Guid partyId, List<Company> companies, bool was, CancellationToken ct)
    {
        var onShift = false;
        foreach (var company in companies)
        {
            foreach (var person in await desks.StaffAsync(partyId, company.Id))
            {
                if ((await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, person.Id)).GetAsync()).OnShift) onShift = true;
            }
        }
        var edge = CduRules.Edge(was, onShift);
        if (edge == CduShift.None) return;
        if (edge == CduShift.Began) await cdu.TideInAsync(ct);
        else await cdu.TideOutAsync(ct);
        await Grain(partyId).ShiftAsync(onShift);
    }

    private static CduNews Flatten(CduEntry e) => new(
        e.N, e.Kind, e.Text, e.Str("turn"), e.Int("number"), e.Flag("asked"), e.Int("commits") ?? 0, e.Flag("opened"));

    /// <summary>News reaches staff as work memory, never the wider world: the CDU is a secret (ADR 0039).</summary>
    private async Task TellAsync(Guid partyId, List<Company> companies, CduTold word)
    {
        var staff = new List<ParticipantView>();
        foreach (var company in companies) staff.AddRange(await desks.StaffAsync(partyId, company.Id));
        var told = word.Only is { } only ? staff.Where(s => s.Id == only).DistinctBy(s => s.Id).ToList() : staff.DistinctBy(s => s.Id).ToList();
        var clock = grains.GetGrain<IWorldClockGrain>(partyId);
        await clock.RecordAsync(new WorldActivity
        {
            PersonaName = told.Count == 1 ? told[0].Name : "the CDU", App = CduScreen.AppId, Action = word.Kind, Summary = word.Text,
            TargetId = word.CompanyId ?? companies[0].Id,
        });
        if (told.Count == 0) return;
        if (word.Lifts is { } lifts) await MoveMoodsAsync(partyId, told, lifts, word.Text);
        await clock.NotifyAsync(told.Select(s => s.Id).ToList());
        Remember(partyId, word.CompanyId ?? companies[0].Id, told, word.Text);
    }

    private void Remember(Guid partyId, Guid companyId, List<ParticipantView> who, string text)
    {
        var moment = new List<MomentLine> { new(Narrator.PersonaId, $"[At work] {text}", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) };
        _ = memory.CaptureAsync(new Moment(partyId, MomentSources.Work, companyId, moment, moment.Count - 1, who), CancellationToken.None);
    }

    private async Task MoveMoodsAsync(Guid partyId, List<ParticipantView> staff, bool lifts, string why)
    {
        foreach (var person in staff)
        {
            try
            {
                await grains.GetGrain<Self.IPersonaSelfGrain>(WorldKeys.Life(partyId, person.Id)).ShiftValenceAsync(lifts ? 0.7 : 0.3, WorldStream.Clip(why, 80));
            }
            catch (Exception ex) { logger.LogDebug(ex, "Mood from a verdict failed for {Who}", person.Id); }
        }
    }

    private ICduGrain Grain(Guid partyId) => grains.GetGrain<ICduGrain>(partyId);
}

/// <summary>Carries the CDU's news in, outside the World clock so a paused World still shows it working.</summary>
public sealed class CduWatch(IGrainFactory grains, Cytheurgy site, ILogger<CduWatch> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!site.Enabled) return;
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                foreach (var party in await grains.GetGrain<IPartyRootGrain>(Guid.Empty).GetAll()) await site.WatchAsync(party.Id, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "CDU watch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
