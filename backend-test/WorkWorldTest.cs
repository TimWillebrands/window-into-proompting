using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Economy;
using PartyTown.World.Places;
using PartyTown.World.Work;

namespace BackendTest;

/// <summary>A shift in a real cluster (ADR 0026): the Body goes to work, the Wallet is paid per shift.</summary>
public class WorkWorldTest : IClassFixture<WorldLifeTest.Fixture>
{
    private readonly WorldLifeTest.Fixture _fx;

    public WorkWorldTest(WorldLifeTest.Fixture fx) => _fx = fx;

    private static readonly DateTimeOffset Monday9 = new(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(1));

    [Fact]
    public async Task OnscreenShift_WalksToWorkAndPaysAtTheEnd()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var key = WorldKeys.Life(partyId, denise);
        var places = _fx.GrainFactory.GetGrain<IPlacesGrain>(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        var occupation = _fx.GrainFactory.GetGrain<IOccupationGrain>(key);
        var wallet = _fx.GrainFactory.GetGrain<IWalletGrain>(key);
        await occupation.SetAsync(new Occupation { Title = "barista", Workplace = cafe.Name, PlaceId = cafe.Id });
        Assert.True((await wallet.GetAsync()).Employed);
        var before = (await wallet.GetAsync()).Balance;

        var cast = await _fx.GrainFactory.GetGrain<PartyTown.Grains.IPartyGrain>(partyId).GetCastAsync();
        var self = cast.Single(c => c.Id == denise);
        var workplaces = new Workplaces(_fx.GrainFactory, NullLogger<Workplaces>.Instance);

        var start = await occupation.StepAsync(Monday9.AddMinutes(25), asleep: false);
        Assert.Equal(ShiftStep.Start, start.Step);
        var started = await workplaces.ApplyAsync(partyId, self, start);
        Assert.Contains("15 min late", started!.Summary);
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(cafe.Id, body.DestPlaceId ?? body.PlaceId);
        Assert.True((await occupation.GetAsync()).OnShift);

        var end = await occupation.StepAsync(Monday9.AddHours(8), asleep: false);
        Assert.Equal(ShiftStep.End, end.Step);
        var finished = await workplaces.ApplyAsync(partyId, self, end);
        Assert.Contains("finished work at the Café", finished!.Summary);
        Assert.Equal(before + Shifts.Pay("getting by", Monday9.AddMinutes(25).ToUnixTimeMilliseconds(), Monday9.AddHours(8).ToUnixTimeMilliseconds()),
            (await wallet.GetAsync()).Balance);
        Assert.Equal(ShiftStep.None, (await occupation.StepAsync(Monday9.AddHours(9), false)).Step);
    }

    [Fact]
    public async Task ShiftSleptThrough_IsMissedOnlyWhenTheWorldSawIt()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var occupation = _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise));
        await occupation.SetAsync(new Occupation { Title = "clerk", Workplace = "the office in Zuid" });

        // Clock not running during the shift: nothing to miss.
        Assert.Equal(ShiftStep.None, (await occupation.StepAsync(Monday9.AddHours(9), asleep: true)).Step);

        var tuesday = Monday9.AddDays(1);
        Assert.Equal(ShiftStep.None, (await occupation.StepAsync(tuesday.AddHours(1), asleep: true)).Step);
        Assert.Equal(ShiftStep.Miss, (await occupation.StepAsync(tuesday.AddHours(9), asleep: true)).Step);
    }

    [Fact]
    public async Task OffscreenShift_IsAwayAtALocaleAndComesBack()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var occupation = _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise));
        var job = (await occupation.SetAsync(new Occupation { Title = "clerk", Workplace = "the tax office" })).Job!;
        Assert.Null(job.PlaceId);
        Assert.NotNull(job.LocaleId);

        var places = _fx.GrainFactory.GetGrain<IPlacesGrain>(partyId);
        var self = (await _fx.GrainFactory.GetGrain<PartyTown.Grains.IPartyGrain>(partyId).GetCastAsync()).Single(c => c.Id == denise);
        var workplaces = new Workplaces(_fx.GrainFactory, NullLogger<Workplaces>.Instance);
        await workplaces.ApplyAsync(partyId, self, await occupation.StepAsync(Monday9, false));
        var away = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(job.LocaleId, away.DestPlaceId ?? away.PlaceId);
        Assert.Equal("at work", away.AwayAs);

        var footfall = await _fx.GrainFactory.GetGrain<PartyTown.World.Growth.IGrowthGrain>(partyId).GetReadingsAsync();
        Assert.Contains(footfall, f => f.LocaleId == job.LocaleId);

        await workplaces.ApplyAsync(partyId, self, await occupation.StepAsync(Monday9.AddHours(8), false));
        var back = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.NotEqual(job.LocaleId, back.DestPlaceId ?? back.PlaceId);
    }

    [Fact]
    public async Task OfficeOpenedMidShift_EndsTheShiftOnscreenAndClosingTurnsItBack()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var occupation = _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise));
        var localeId = (await occupation.SetAsync(new Occupation { Title = "clerk", Workplace = "work" })).Job!.LocaleId!.Value;
        var places = _fx.GrainFactory.GetGrain<IPlacesGrain>(partyId);
        var self = (await _fx.GrainFactory.GetGrain<PartyTown.Grains.IPartyGrain>(partyId).GetCastAsync()).Single(c => c.Id == denise);
        var workplaces = new Workplaces(_fx.GrainFactory, NullLogger<Workplaces>.Instance);
        await workplaces.ApplyAsync(partyId, self, await occupation.StepAsync(Monday9, false));

        var opened = await places.OpenAsync(localeId, "office", "test");
        Assert.NotNull(opened);
        var end = await occupation.StepAsync(Monday9.AddHours(8), false);
        Assert.Equal(localeId, end.Job!.PlaceId);
        Assert.Equal(opened!.Name, end.Job.Workplace);
        await workplaces.ApplyAsync(partyId, self, end);
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Null(body.Activity);

        Assert.True(await places.CloseAsync(localeId));
        var next = await occupation.StepAsync(Monday9.AddDays(1), false);
        Assert.Null(next.Job!.PlaceId);
        Assert.Equal(localeId, next.Job.LocaleId);
    }

    [Fact]
    public async Task WorkplaceNamedLikeAPlace_IsOnscreen()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var job = (await _fx.GrainFactory.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, denise))
            .SetAsync(new Occupation { Title = "barista", Workplace = "Café" })).Job!;
        Assert.NotNull(job.PlaceId);
        Assert.Equal("the Café", job.Workplace);
    }

    [Fact]
    public async Task NoJob_KeepsDailyPay()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var key = WorldKeys.Life(partyId, denise);
        await _fx.GrainFactory.GetGrain<IOccupationGrain>(key).SetAsync(null);
        Assert.False((await _fx.GrainFactory.GetGrain<IWalletGrain>(key).GetAsync()).Employed);
    }
}
