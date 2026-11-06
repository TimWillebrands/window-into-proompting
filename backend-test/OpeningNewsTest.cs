using PartyTown.World;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>Openings as news (ADR 0027): a Happening reaches everyone's memory, so only footfall earns one.</summary>
public class OpeningNewsTest : IClassFixture<WorldLifeTest.Fixture>
{
    private readonly WorldLifeTest.Fixture _fx;

    public OpeningNewsTest(WorldLifeTest.Fixture fx) => _fx = fx;

    private async Task<int> Happenings(Guid partyId)
    {
        var clock = _fx.GrainFactory.GetGrain<IWorldClockGrain>(partyId);
        for (var i = 0; i < 40; i++)
        {
            var found = (await clock.GetActivitiesAsync(50, null)).Count(a => a.Action == "happening" && a.Summary.Contains("on the map"));
            if (found > 0) return found;
            await Task.Delay(25);
        }
        return 0;
    }

    private async Task OpenAsync(Guid partyId, Guid who, string reason)
    {
        var places = _fx.GrainFactory.GetGrain<IPlacesGrain>(partyId);
        Assert.NotNull(await places.OpenAsync(await places.LocaleAsync("the climbing gym", who), null, reason));
    }

    [Fact]
    public async Task AnAuthorOpening_TellsNobody()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        await OpenAsync(partyId, denise, "author");
        Assert.Equal(0, await Happenings(partyId));
    }

    [Fact]
    public async Task AnOpeningFromFootfall_IsStillNews()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync();
        await OpenAsync(partyId, denise, "footfall");
        Assert.Equal(1, await Happenings(partyId));
    }
}
