using PartyTown.Model;
using PartyTown.World.Economy;

namespace PartyTown.World.Work;

public sealed class Workplaces(IGrainFactory grains, ILogger<Workplaces> logger)
{
    public async Task<WorldActivity?> ApplyAsync(Guid partyId, CastMember who, ShiftChange change)
    {
        if (change.Job is not { } job || change.Record is not { } record) return null;
        switch (change.Step)
        {
            case ShiftStep.Start:
                await ChangeForWorkAsync(partyId, who.Id, job);
                await GoAsync(partyId, who.Id, job);
                await AnchorAsync(partyId, who.Id, job);
                return Activity(who, "start", record.LateMinutes > 0
                    ? $"got to work at {job.Workplace}, {record.LateMinutes} min late"
                    : $"started work at {job.Workplace}");
            case ShiftStep.End:
            {
                var pay = await PayAsync(partyId, who.Id, job, record);
                await LeaveAsync(partyId, who.Id, job);
                if (job.Practice is null && !string.IsNullOrWhiteSpace(job.Title))
                    await Skills.Practising.NoteAsync(grains, partyId, who.Id, Skills.Practising.Work(job.Title, $"worked a shift as {job.Title} at {job.Workplace}"), logger);
                return Activity(who, "finish", $"finished work at {job.Workplace} (+{pay} credits)");
            }
            case ShiftStep.Miss:
                return Activity(who, "missed", $"missed work at {job.Workplace} today");
        }
        return null;
    }

    public async Task<int> LeaveEarlyAsync(Guid partyId, Guid who, Occupation job, ShiftRecord record)
    {
        var pay = await PayAsync(partyId, who, job, record);
        await LeaveAsync(partyId, who, job);
        return pay;
    }

    private async Task<int> PayAsync(Guid partyId, Guid who, Occupation job, ShiftRecord record)
    {
        if (job.Practice is not null || record.StartedAt is not { } from || record.EndedAt is not { } to) return 0;
        var key = WorldKeys.Life(partyId, who);
        var wallet = grains.GetGrain<IWalletGrain>(key);
        var pay = Shifts.Pay((await wallet.GetAsync()).Means, from, to);
        if (pay <= 0) return 0;
        await wallet.EarnAsync(pay, $"pay from {job.Workplace}");
        await grains.GetGrain<IOccupationGrain>(key).NotePaidAsync(record.Day, pay);
        return pay;
    }

    public async Task GoAsync(Guid partyId, Guid who, Occupation job)
    {
        try
        {
            if ((job.PlaceId ?? job.LocaleId) is { } where)
                await grains.GetGrain<Places.IPlacesGrain>(partyId).GoAwayAsync(who, where, job.Onscreen ? "working" : "at work");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Going to work failed for {Who}", who); }
    }

    private async Task ChangeForWorkAsync(Guid partyId, Guid who, Occupation job)
    {
        if (job.Clothes is not { OwnClothes: false } clothes) return;
        try { await grains.GetGrain<Places.IPlacesGrain>(partyId).ChangeForWorkAsync(who, clothes); }
        catch (Exception ex) { logger.LogWarning(ex, "Changing for work failed for {Who}", who); }
    }

    private async Task AnchorAsync(Guid partyId, Guid who, Occupation job)
    {
        if (job.PlaceId is not null || job.LocaleId is not { } localeId) return;
        try { await grains.GetGrain<Growth.IGrowthGrain>(partyId).NoteAsync(localeId, who, Growth.FootfallSources.Anchor); }
        catch (Exception ex) { logger.LogDebug(ex, "Work footfall failed for {Who}", who); }
    }

    private async Task LeaveAsync(Guid partyId, Guid who, Occupation job)
    {
        try
        {
            var places = grains.GetGrain<Places.IPlacesGrain>(partyId);
            await places.ChangeBackAsync(who);
            if (job.Onscreen) await places.SetActivityAsync(who, null, null);
            else await places.ReturnAsync(who);
        }
        catch (Exception ex) { logger.LogWarning(ex, "Leaving work failed for {Who}", who); }
    }

    private static WorldActivity Activity(CastMember who, string action, string summary)
        => new() { PersonaId = who.Id, PersonaName = who.Name, App = WorkApp.AppId, Action = action, Summary = summary };
}
