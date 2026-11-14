using PartyTown.Services.SystemOne;
using PartyTown.World.Growth;
using PartyTown.World.Places;

namespace BackendTest;

/// <summary>One place, many names: new names resolve to a Place already there, talk credits it, and a duplicate folds instead of opening.</summary>
public partial class PlacesTest
{
    private static readonly string[] SomeDistricts = { "the centre", "Amstelveen" };

    [Theory]
    [InlineData("small Shinto shrine in Amstelveen", "small shinto shrine")]
    [InlineData("the Café on the corner", "cafe corner")]
    [InlineData("the market in Amsterdam", "market")]
    public void CoreWords_SayWhatAPlaceIs_NotWhere(string name, string words)
        => Assert.Equal(words.Split(' ').Order(), PlacesGrain.CoreWords(name, SomeDistricts).Order());

    [Fact]
    public void ADrawnPlace_CoversALocale_WhoseWordsAreAllInItsName_InItsDistrict()
    {
        var shrine = new Place { Name = "small Shinto shrine in Amstelveen", Kind = PlaceKinds.Public, DistrictId = "amstelveen" };
        Assert.True(PlacesGrain.Covers(shrine, new Place { Name = "the Shinto shrine", DistrictId = "amstelveen" }, SomeDistricts));
        Assert.True(PlacesGrain.Covers(shrine, new Place { Name = "the shrine" }, SomeDistricts));
        Assert.False(PlacesGrain.Covers(shrine, new Place { Name = "the Shinto shrine", DistrictId = "centre" }, SomeDistricts));
        Assert.False(PlacesGrain.Covers(new Place { Name = "the Café" }, new Place { Name = "the Café on the corner" }, SomeDistricts));
    }

    [Fact]
    public void Shortlist_HasPlacesSharingAWord_OrDrawnOfTheSameKind()
    {
        var places = new List<Place>
        {
            new() { Name = "the Café", Kind = PlaceKinds.Public, Archetype = "cafe" },
            new() { Name = "Koffiehuis Noord", Kind = PlaceKinds.Public, Archetype = "cafe" },
            new() { Name = "the climbing gym", Kind = PlaceKinds.Locale },
            new() { Name = "the corner bar", Kind = PlaceKinds.Locale },
            new() { Name = "Denise's place", Kind = PlaceKinds.Home },
        };
        var names = PlacesGrain.Shortlist("the café on the corner", places, SomeDistricts).Select(p => p.Name).ToList();
        Assert.Equal(new[] { "the Café", "the corner bar", "Koffiehuis Noord" }, names);
    }

    [Theory]
    [InlineData("the Café (the centre)", 0.8, "the Café (the centre)")]
    [InlineData("the Café (the centre)", 0.4, null)]
    [InlineData("somewhere else", 0.9, null)]
    public void PickSame_TakesOnlyAConfidentPlace(string best, double p, string? picked)
    {
        var answers = new SystemOneAnswers();
        answers.Choices["same"] = new Dictionary<string, double> { [best] = p, ["other"] = (1 - p) / 2 };
        Assert.Equal(picked, PlacesGrain.PickSame(answers));
    }

    [Fact]
    public async Task ANewName_ForAPlaceAlreadyThere_ResolvesToIt_AndIsRemembered()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var shrine = await places.LocaleInAsync("small Shinto shrine in Amstelveen", denise, "Amstelveen");
        await places.OpenAsync(shrine, "park", "author");

        Assert.Equal(shrine, await places.LocaleInAsync("the Shinto shrine", pieter, "Amstelveen"));
        Assert.Equal(shrine, await places.LocaleAsync("Shinto shrine", pieter));
        Assert.NotEqual(shrine, await places.LocaleAsync("the climbing gym", pieter));

        var snapshot = await places.GetSnapshotAsync();
        Assert.Contains("the Shinto shrine", snapshot.Places.Single(p => p.Id == shrine).AlsoCalled);
        Assert.DoesNotContain(snapshot.Places, p => p.Name == "the Shinto shrine");
    }

    [Fact]
    public async Task ALocale_ThatIsAPlaceAlreadyDrawn_FoldsIntoIt_InsteadOfOpening()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var locale = await places.LocaleInAsync("the Shinto shrine", denise, "Amstelveen");
        var shrine = await places.LocaleInAsync("small Shinto shrine in Amstelveen", denise, "Amstelveen");
        Assert.NotEqual(locale, shrine);
        await places.OpenAsync(shrine, "park", "author");
        await places.GoAwayAsync(denise, locale, "out");
        _fx.Time.Advance(TimeSpan.FromMinutes(2));

        var opened = await places.OpenAsync(locale, null, "footfall");

        Assert.Equal(shrine, opened?.Id);
        var snapshot = await places.GetSnapshotAsync();
        var folded = snapshot.Places.Single(p => p.Id == locale);
        Assert.Equal((PlaceKinds.Locale, (Guid?)shrine), (folded.Kind, folded.SameAs));
        Assert.Equal(shrine, snapshot.Bodies.Single(b => b.ParticipantId == denise).PlaceId);
        Assert.Equal(shrine, await places.LocaleAsync("the Shinto shrine", denise));
        Assert.Null(await places.OpenAsync(locale, null, "author"));
    }

    [Fact]
    public async Task TalkAboutADrawnPlace_GivesNoFootfall_ToALocaleThatSoundsLikeIt()
    {
        var (partyId, denise, pieter) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var corner = await places.LocaleAsync("the Café on the corner", denise);
        Assert.NotEqual((await places.GetSnapshotAsync()).Places.Single(p => p.Name == "the Café").Id, corner);

        await places.NoteTopicsAsync(new List<string> { "café" }, new List<Guid> { denise });
        await places.NoteTopicsAsync(new List<string> { "café on the corner" }, new List<Guid> { pieter });

        var growth = _fx.GrainFactory.GetGrain<IGrowthGrain>(partyId);
        FootfallReading? reading = null;
        for (var i = 0; i < 50 && reading is null; i++)
        {
            await Task.Delay(50);
            reading = (await growth.GetReadingsAsync()).SingleOrDefault(r => r.LocaleId == corner);
        }
        Assert.Equal(new[] { pieter }, reading?.Contributors);
    }

    [Fact]
    public async Task PlacesYouGo_HasEveryDrawnPlace_WithItsDistrictWhenElsewhere()
    {
        var (partyId, denise, _) = await _fx.CreateWorldAsync();
        var places = _fx.Places(partyId);
        var shrine = await places.LocaleInAsync("small Shinto shrine in Amstelveen", denise, "Amstelveen");
        await places.OpenAsync(shrine, "park", "author");

        var known = await places.GetKnownPlacesAsync(denise);

        Assert.Contains("small Shinto shrine in Amstelveen (Amstelveen)", known);
        Assert.Contains("the Café", known);
    }
}
