using PartyTown.Grains;
using PartyTown.World.Growth;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>ADR 0027 Openings: a Locale becomes a drawn Place from a kit, keeping its id.</summary>
public partial class PlacesTest
{
    [Theory]
    [InlineData("the climbing gym", "gym")]
    [InlineData("Denise's work", "office")]
    [InlineData("Café de Jaren", "cafe")]
    [InlineData("the barber", null)]
    [InlineData("Vondelpark", null)]
    public void AKit_ComesFromTheName_WhenItSaysSo(string name, string? archetype)
        => Assert.Equal(archetype, Kits.FromName(name));

    [Fact]
    public async Task Opening_KeepsTheId_AndWhoeverWasAwayThereIsNowThere()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var work = await places.LocaleAsync("work", denise);
        await places.GoAwayAsync(denise, work, "at work");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        var opened = await places.OpenAsync(work, null, "author");

        Assert.NotNull(opened);
        Assert.Equal((work, PlaceKinds.Public, "office", "Denise's office"), (opened.Id, opened.Kind, opened.Archetype, opened.Name));
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal((work, "desks", "working", (string?)null), (body.PlaceId, body.SpotId, body.Activity, body.AwayAs));
        Assert.StartsWith("You're at Denise's office, sitting at the desks, working.", await places.GetSituationAsync(denise));
        Assert.Null(await places.OpenAsync(work, null, "author"));

        Assert.True(await places.CloseAsync(work));
        var back = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(("at work", (string?)null), (back.AwayAs, back.Activity));
        Assert.Equal(PlaceKinds.Locale, (await places.GetSnapshotAsync()).Places.Single(p => p.Id == work).Kind);
    }

    [Fact]
    public async Task VisitingSomeone_OpensTheirLocale_OnlyIfTheyreARegularThere()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var gym = await places.LocaleAsync("the climbing gym", denise);
        await places.GoAwayAsync(denise, gym, "out");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        await places.GetSnapshotAsync();

        Assert.Null(await places.JoinAsync(pieter, denise));

        _fx.Time.Advance(TimeSpan.FromDays(1));
        await places.GetVisitDaysAsync(denise);
        Assert.NotNull(await places.JoinAsync(pieter, denise));
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var snapshot = await places.GetSnapshotAsync();
        Assert.Equal(("gym", PlaceKinds.Public), (snapshot.Places.Single(p => p.Id == gym).Archetype, snapshot.Places.Single(p => p.Id == gym).Kind));
        Assert.Equal(gym, snapshot.Bodies.Single(b => b.ParticipantId == pieter).PlaceId);
    }

    [Fact]
    public async Task WithAutoOn_ALocalePastTheGates_Opens_OneADay()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var growth = _fx.GrainFactory.GetGrain<IGrowthGrain>(partyId);
        var bar = await places.LocaleAsync("the corner bar", denise);
        var pub = await places.LocaleAsync("the old pub", denise);
        await growth.SetAutoAsync(true);

        foreach (var day in new[] { 0, 1, 2 })
        {
            if (day > 0) _fx.Time.Advance(TimeSpan.FromDays(1));
            foreach (var who in new[] { denise, pieter })
            foreach (var locale in new[] { bar, pub })
            foreach (var source in new[] { FootfallSources.Day, FootfallSources.Anchor, FootfallSources.Talk })
                await growth.NoteAsync(locale, who, source);
        }

        var kinds = (await places.GetSnapshotAsync()).Places.Where(p => p.Id == bar || p.Id == pub).Select(p => p.Kind).ToList();
        Assert.Equal(1, kinds.Count(k => k == PlaceKinds.Public));
        Assert.Equal(1, kinds.Count(k => k == PlaceKinds.Locale));
    }

    [Fact]
    public async Task AThing_ThatTheCatalogueHas_IsDrawnAsIt_AndAMissIsQueued()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");

        var plant = await places.AddThingAsync(cafe.Id, denise, "a big potted plant");
        await places.AddThingAsync(cafe.Id, denise, "a poster of Lisbon at night");

        string? prefab = null;
        for (var i = 0; i < 50 && prefab is null; i++)
        {
            await Task.Delay(50);
            prefab = (await places.GetSnapshotAsync()).Furni.Single(f => f.FurniId == plant!.FurniId).Prefab;
        }
        Assert.Equal("k-potted-plant", prefab);
        Assert.Contains("a poster of Lisbon at night", await places.GetCatalogueMissesAsync());
    }

    [Fact]
    public async Task AnOpenedPlace_NobodyGoesTo_GoesQuiet_AndTalkWakesIt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var gym = await places.LocaleAsync("the climbing gym", denise);
        await places.OpenAsync(gym, null, "author");

        _fx.Time.Advance(TimeSpan.FromDays(8));
        Assert.True((await places.GetSnapshotAsync()).Places.Single(p => p.Id == gym).Dormant);

        var dm = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetOrCreateDirectChatAsync(denise, pieter);
        await _fx.GrainFactory.GetGrain<IChatGroupGrain>(dm.Id).SendNewMessageAsync(denise, "miss the climbing gym honestly", null, null);
        var dormant = true;
        for (var i = 0; i < 50 && dormant; i++)
        {
            await Task.Delay(50);
            dormant = (await places.GetSnapshotAsync()).Places.Single(p => p.Id == gym).Dormant;
        }
        Assert.False(dormant);
    }

    [Fact]
    public async Task ADistrict_WithTwoOpenedVenues_GetsItsOwnStreets()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var gym = await places.LocaleInAsync("the climbing gym", denise, "Amsterdam Zuid");
        var office = await places.LocaleInAsync("the office in Zuid", denise, "Amsterdam Zuid");

        await places.OpenAsync(gym, null, "author");
        Assert.DoesNotContain((await places.GetSnapshotAsync()).Places, p => p.Scene == "district");

        await places.OpenAsync(office, null, "author");
        var streets = Assert.Single((await places.GetSnapshotAsync()).Places, p => p.Scene == "district");
        Assert.Equal(("Amsterdam Zuid", "amsterdam-zuid", "in"), (streets.Name, streets.DistrictId, streets.At));
        Assert.Contains(gym.ToString(), streets.CustomScene);
        Assert.Contains(office.ToString(), streets.CustomScene);
    }
}
