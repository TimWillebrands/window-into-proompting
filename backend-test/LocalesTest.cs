using PartyTown.World.Places;

namespace BackendTest;

/// <summary>ADR 0027 Locales: offscreen Places a Body is away at; plus ADR 0026's purpose, staff spots and visit days.</summary>
public partial class PlacesTest
{
    [Fact]
    public async Task GoingAway_WalksOutToALocale_ThatIsNeverATalkPlace_AndComesBack()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");
        await places.MoveAsync(denise, cafe.Id, "booth");
        await places.MoveAsync(pieter, cafe.Id, "booth");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        var office = await places.LocaleAsync("the office in Zuid", denise);
        Assert.Equal(office, await places.LocaleAsync("Office in Zuid", pieter));
        Assert.NotNull(await places.GoAwayAsync(denise, office, "at work"));
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        var snapshot = await places.GetSnapshotAsync();
        var locale = snapshot.Places.Single(p => p.Id == office);
        Assert.Equal(PlaceKinds.Locale, locale.Kind);
        Assert.Null(locale.TalkRoomId);
        Assert.Equal(office, snapshot.Bodies.Single(b => b.ParticipantId == denise).PlaceId);
        Assert.StartsWith("You're at work at the office in Zuid. That's offscreen", await places.GetSituationAsync(denise));
        Assert.DoesNotContain("Denise", await places.GetSituationAsync(pieter));

        // Nobody walks over to a Locale: not by moving, not by joining.
        Assert.Null(await places.MoveAsync(pieter, office, null));
        Assert.Null(await places.JoinAsync(pieter, denise));

        await places.ReturnAsync(denise);
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var back = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);
        Assert.Equal(cafe.Id, back.PlaceId);
        Assert.Null(back.AwayAs);
    }

    [Fact]
    public async Task PlainWork_IsYourOwnLocale_AndADrawnPlaceNameResolvesToThePlace()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");

        var hers = await places.LocaleAsync("work", denise);
        Assert.NotEqual(hers, await places.LocaleAsync("work", pieter));
        Assert.Equal("Denise's work", (await places.GetSnapshotAsync()).Places.Single(p => p.Id == hers).Name);
        Assert.Equal(cafe.Id, await places.LocaleAsync("Café", null));

        // A drawn workplace: just walk there, behind the counter, working.
        await places.GoAwayAsync(pieter, cafe.Id, "working");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        var body = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == pieter);
        Assert.Equal(("counter", "working", (string?)null), (body.SpotId, body.Activity, body.AwayAs));
    }

    [Fact]
    public async Task ReturnAsync_LeavesABodyThatIsntAway_WhereItIs()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var before = (await places.GetSnapshotAsync()).Bodies.Single(b => b.ParticipantId == denise);

        var after = await places.ReturnAsync(denise);

        Assert.Equal(before.PlaceId, after?.PlaceId);
        Assert.False(after?.IsWalking);
    }

    [Fact]
    public async Task SeededPlaces_HaveAPurpose_AndStaffSpots_ShownInTheSituation()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var cafe = (await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café");

        Assert.Equal("where people get coffee and linger", cafe.Purpose);
        Assert.True(cafe.Spots.Single(s => s.Id == "counter").Staff);
        await places.MoveAsync(denise, cafe.Id, "booth");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.EndsWith("The Café is where people get coffee and linger.", await places.GetSituationAsync(denise));
    }

    [Fact]
    public async Task VisitDays_CountDistinctDaysPerPlace_ForAWeek()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var snapshot = await places.GetSnapshotAsync();
        var cafe = snapshot.Places.Single(p => p.Name == "the Café");
        var home = snapshot.Places.Single(p => p.OwnerId == denise);

        for (var day = 0; day < 3; day++)
        {
            await places.MoveAsync(denise, cafe.Id, "booth");
            _fx.Time.Advance(TimeSpan.FromMinutes(2));
            await places.MoveAsync(denise, home.Id, "sofa");
            _fx.Time.Advance(TimeSpan.FromMinutes(2));
            await places.GetSnapshotAsync();
            _fx.Time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(4));
        }

        var days = await places.GetVisitDaysAsync(denise);
        Assert.Equal(3, days[cafe.Id]);
        Assert.True(days[home.Id] >= 3);

        _fx.Time.Advance(TimeSpan.FromDays(8));
        await places.GetSnapshotAsync();
        Assert.False((await places.GetVisitDaysAsync(denise)).ContainsKey(cafe.Id));
    }
}
