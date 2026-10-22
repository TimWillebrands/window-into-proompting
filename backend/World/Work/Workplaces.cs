using PartyTown.Model;
using PartyTown.World.Economy;

namespace PartyTown.World.Work;

/// <summary>What a shift does in the World (ADR 0026): the Body goes to work and comes back, the Wallet gets paid.</summary>
public sealed class Workplaces(IGrainFactory grains, ILogger<Workplaces> logger)
{
    /// <summary>Carries out one clock step; returns the Activity to log, or null when nothing happened.</summary>
    public async Task<WorldActivity?> ApplyAsync(Guid partyId, CastMember who, ShiftChange change)
    {
        if (change.Job is not { } job || change.Record is not { } record) return null;
        switch (change.Step)
        {
            case ShiftStep.Start:
                await GoAsync(partyId, who.Id, job);
                await AnchorAsync(partyId, who.Id, job);
                return Activity(who, "start", record.LateMinutes > 0
                    ? $"got to work at {job.Workplace}, {record.LateMinutes} min late"
                    : $"started work at {job.Workplace}");
            case ShiftStep.End:
            {
                var pay = await PayAsync(partyId, who.Id, job, record);
                await LeaveAsync(partyId, who.Id, job);
                return Activity(who, "finish", $"finished work at {job.Workplace} (+{pay} credits)");
            }
            case ShiftStep.Miss:
                return Activity(who, "missed", $"missed work at {job.Workplace} today");
        }
        return null;
    }

    /// <summary>Walking out mid-shift: paid for the time worked.</summary>
    public async Task<int> LeaveEarlyAsync(Guid partyId, Guid who, Occupation job, ShiftRecord record)
    {
        var pay = await PayAsync(partyId, who, job, record);
        await LeaveAsync(partyId, who, job);
        return pay;
    }

    private async Task<int> PayAsync(Guid partyId, Guid who, Occupation job, ShiftRecord record)
    {
        if (record.StartedAt is not { } from || record.EndedAt is not { } to) return 0;
        var key = WorldKeys.Life(partyId, who);
        var wallet = grains.GetGrain<IWalletGrain>(key);
        var pay = Shifts.Pay((await wallet.GetAsync()).Means, from, to);
        if (pay <= 0) return 0;
        await wallet.EarnAsync(pay, $"pay from {job.Workplace}");
        await grains.GetGrain<IOccupationGrain>(key).NotePaidAsync(record.Day, pay);
        return pay;
    }

    /// <summary>Onscreen: walk to a Staff spot and work. Offscreen: away at the workplace's Locale (ADR 0027).</summary>
    public async Task GoAsync(Guid partyId, Guid who, Occupation job)
    {
        try
        {
            if ((job.PlaceId ?? job.LocaleId) is { } where)
                await grains.GetGrain<Places.IPlacesGrain>(partyId).GoAwayAsync(who, where, job.Onscreen ? "working" : "at work");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Going to work failed for {Who}", who); }
    }

    /// <summary>A day worked at an offscreen Locale is footfall toward Opening it (ADR 0027); once a day counts.</summary>
    private async Task AnchorAsync(Guid partyId, Guid who, Occupation job)
    {
        if (job.PlaceId is not null || job.LocaleId is not { } localeId) return;
        try { await grains.GetGrain<Growth.IGrowthGrain>(partyId).NoteAsync(localeId, who, Growth.FootfallSources.Anchor); }
        catch (Exception ex) { logger.LogDebug(ex, "Work footfall failed for {Who}", who); }
    }

    /// <summary>Onscreen workers stay where they are, just off duty; offscreen ones come back.</summary>
    private async Task LeaveAsync(Guid partyId, Guid who, Occupation job)
    {
        try
        {
            var places = grains.GetGrain<Places.IPlacesGrain>(partyId);
            if (job.Onscreen) await places.SetActivityAsync(who, null, null);
            else await places.ReturnAsync(who);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Leaving work failed for {Who}", who); }
    }

    private static WorldActivity Activity(CastMember who, string action, string summary)
        => new() { PersonaId = who.Id, PersonaName = who.Name, App = WorkApp.AppId, Action = action, Summary = summary };
}
