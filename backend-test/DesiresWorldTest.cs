using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Economy;

namespace BackendTest;

/// <summary>Desires in a real cluster (ADR 0026): intrinsic ones go per Party, acquired ones crowd out, wanting gets bought.</summary>
public class DesiresWorldTest : IClassFixture<WorldLifeTest.Fixture>
{
    private readonly WorldLifeTest.Fixture _fx;

    public DesiresWorldTest(WorldLifeTest.Fixture fx) => _fx = fx;

    [Fact]
    public async Task IntrinsicDesire_IsFulfilledPerPartyNotForTheWholePersona()
    {
        var (partyA, denise) = await _fx.CreateWorldAsync();
        var mine = (await _fx.GrainFactory.GetGrain<IIntrinsicDesiresGrain>(denise)
            .SetAsync(new List<Desire> { new() { Kind = DesireKind.Being, Text = "get your sourdough right" } })).Single();
        var here = _fx.GrainFactory.GetGrain<IDesiresGrain>(WorldKeys.Life(partyA, denise));
        Assert.True(await here.MarkAsync(mine.Id, DesireStatus.Fulfilled));
        Assert.Equal(DesireStatus.Fulfilled, (await here.GetAsync()).Intrinsic.Single().Status);

        var elsewhere = _fx.GrainFactory.GetGrain<IDesiresGrain>(WorldKeys.Life(Guid.NewGuid(), denise));
        Assert.Equal(DesireStatus.Active, (await elsewhere.GetAsync()).Intrinsic.Single().Status);
    }

    [Fact]
    public async Task AcquiredDesires_CrowdOutTheOldest()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var grain = _fx.GrainFactory.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, denise));
        for (var i = 0; i < 4; i++)
            await grain.AddAsync(new Desire { Kind = DesireKind.Being, Text = $"want {i}", Since = DateTimeOffset.UtcNow.AddMinutes(i).ToUnixTimeMilliseconds() });
        var acquired = (await grain.GetAsync()).Acquired;
        Assert.Equal(DesireStatus.Faded, acquired.Single(d => d.Text == "want 0").Status);
        Assert.Equal(3, acquired.Count(d => d.Status == DesireStatus.Active));
    }

    [Fact]
    public async Task BuyingTheWantedThing_FulfilsIt()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        var key = WorldKeys.Life(partyId, denise);
        var wanted = DesireRules.Parse("a navy hoodie", DesireKind.Having, DesireSource.Play, DateTimeOffset.UtcNow)!;
        await _fx.GrainFactory.GetGrain<IDesiresGrain>(key).AddAsync(wanted);
        var wardrobe = new WardrobeApp(_fx.GrainFactory, Mock.Of<ISystemOneJudge>(), NullLogger<WardrobeApp>.Instance);

        Assert.NotNull(await wardrobe.BuyAsync(partyId, denise, Catalog.Find("top:hoodie:navy")!));
        Assert.Equal(DesireStatus.Fulfilled, (await _fx.GrainFactory.GetGrain<IDesiresGrain>(key).GetAsync()).Acquired.Single().Status);
    }
}
