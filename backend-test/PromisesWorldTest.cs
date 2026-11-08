using Microsoft.Extensions.Logging.Abstractions;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Promises in a real cluster (ADR 0043): one Desire per promise, lapses that cool, meet-ups on the Calendar.</summary>
public class PromisesWorldTest : IClassFixture<CduWorldTest.Fixture>
{
    private readonly CduWorldTest.Fixture _fx;

    public PromisesWorldTest(CduWorldTest.Fixture fx) => _fx = fx;

    private IDesiresGrain DesiresOf(Guid partyId, Guid who) => _fx.GrainFactory.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, who));

    private static Desire Promise(Guid holder, DateTimeOffset at, string when = PromiseRules.Soon)
        => PromiseRules.Make(holder, false, "I'll send you the trace.", "can you look at it?", when, at);

    [Fact]
    public async Task SayingItAgain_KeepsOneDesire_AndPromisesDontCrowdOutWants()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var desires = DesiresOf(partyId, denise);
        for (var i = 0; i < 3; i++)
            await desires.AddAsync(new Desire { Kind = DesireKind.Being, Text = $"want {i}", Since = DateTimeOffset.UtcNow.AddMinutes(i).ToUnixTimeMilliseconds() });

        await desires.AddAsync(Promise(pieter, DateTimeOffset.Now));
        var again = await desires.AddAsync(Promise(pieter, DateTimeOffset.Now));

        var acquired = (await desires.GetAsync()).Acquired;
        Assert.Equal(1, again.Again);
        Assert.Single(acquired, d => DesireRules.IsPromise(d));
        Assert.Equal(3, acquired.Count(d => !DesireRules.IsPromise(d) && d.Status == DesireStatus.Active));
    }

    [Fact]
    public async Task ADuePromiseLeftToLapse_Fades_AndCoolsWhoeverWasOwedIt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var ties = _fx.GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, pieter));
        var before = (await ties.GetTieAsync(denise)).Warmth;
        await DesiresOf(partyId, denise).AddAsync(Promise(pieter, DateTimeOffset.Now.AddHours(-25)));

        var faded = Assert.Single((await DesiresOf(partyId, denise).GetAsync()).Acquired);

        Assert.Equal(DesireStatus.Faded, faded.Status);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while ((await ties.GetTieAsync(denise)).Warmth >= before && DateTime.UtcNow < deadline) await Task.Delay(100);
        Assert.True((await ties.GetTieAsync(denise)).Warmth < before);
    }

    [Fact]
    public async Task TheHolder_HearsItFromThePromisersOwnDesires()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        await DesiresOf(partyId, denise).AddAsync(Promise(pieter, DateTimeOffset.Now));

        var pieters = await _fx.GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, pieter)).RenderAsync(new List<Guid> { denise });
        var others = await _fx.GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, Guid.NewGuid())).RenderAsync(new List<Guid> { denise });

        Assert.Contains("Denise told you \"I'll send you the trace.\"", pieters);
        Assert.DoesNotContain("told you", others);
    }

    [Fact]
    public async Task ASetDayMeetup_IsAGatheringAtTheHostsHome_BothGoing_AndOnlyOnce()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var follow = new FollowThrough(new EmptyJudge(), _fx.GrainFactory, NullLogger<FollowThrough>.Instance);
        string Name(Guid id) => id == denise ? "Denise" : "Pieter";
        var thursday = DateTimeOffset.Now.AddDays(4).ToUnixTimeMilliseconds();

        Assert.True(await follow.PlanMeetupAsync(partyId, denise, pieter, thursday, PromiseRules.OtherHome, Name));
        Assert.True(await follow.PlanMeetupAsync(partyId, pieter, denise, thursday + 3_600_000, PromiseRules.TheirHome, Name));

        var gathering = Assert.Single(await _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId).GetGatheringsAsync());
        var home = (await _fx.GrainFactory.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync()).Places.Single(p => p.Id == gathering.PlaceId);
        Assert.Equal(pieter, gathering.HostId);
        Assert.Equal(PlaceKinds.Home, home.Kind);
        Assert.True(home.LivesIn(pieter));
        Assert.Equal(new HashSet<Guid> { denise, pieter }, gathering.AttendeeIds.ToHashSet());
        Assert.Equal("Denise over at Pieter's", gathering.Title);
    }

    [Fact]
    public async Task AMeetupWithNoHomeToHoldIt_FallsBack()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var follow = new FollowThrough(new EmptyJudge(), _fx.GrainFactory, NullLogger<FollowThrough>.Instance);

        Assert.False(await follow.PlanMeetupAsync(partyId, denise, Guid.NewGuid(), DateTimeOffset.Now.AddDays(2).ToUnixTimeMilliseconds(), PromiseRules.OtherHome, _ => "Someone"));
        Assert.Empty(await _fx.GrainFactory.GetGrain<ICalendarGrain>(partyId).GetGatheringsAsync());
    }

    private sealed class EmptyJudge : ISystemOneJudge
    {
        public Task<SystemOneAnswers> AskAsync(string state, IReadOnlyDictionary<string, SystemOneQuestion> questions, CancellationToken ct)
            => Task.FromResult(new SystemOneAnswers());
    }
}
