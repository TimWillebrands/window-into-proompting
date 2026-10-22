using PartyTown.World.Places;

namespace BackendTest;

/// <summary>ADR 0027: a thing's description picks a catalogue model when it names one; otherwise it's a miss.</summary>
public class CatalogueMatchTest
{
    [Theory]
    [InlineData("a big potted plant for the window", "k-potted-plant")]
    [InlineData("a bookcase full of manga", "k-bookcase")]
    [InlineData("an old wooden barrel", "k-barrel")]
    [InlineData("bar stools", "k-bar-stool")]
    public void ADescription_NamingAModel_PicksIt(string description, string expected)
        => Assert.Equal(expected, CatalogueMatch.Candidates(description, CatalogueMatch.Catalogue).First().Name);

    [Theory]
    [InlineData("a poster of Lisbon at night")]
    [InlineData("a framed photo of her grandmother")]
    public void ADescription_TheCatalogueCantDraw_IsAMiss(string description)
        => Assert.Empty(CatalogueMatch.Candidates(description, CatalogueMatch.Catalogue));

    [Fact]
    public void OnlyCatalogueModels_AreCandidates()
        => Assert.All(CatalogueMatch.Catalogue, p => Assert.Matches("^(k|kl|q)-", p.Name));
}
