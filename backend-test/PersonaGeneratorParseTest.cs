using PartyTown.Controllers;
using PartyTown.Services.ResponsePipeline;

namespace BackendTest;

/// <summary>Generated persona parsing: lived format (ADR 0021) and the legacy mode-sheet format.</summary>
public class PersonaGeneratorParseTest
{
    private const string Lived = """
        # Roy Callahan

        ## Who you are
        You're 63, and you live in Port Townsend. Your wife Bev is a retired nurse. You spent thirty-one years in engine rooms. The bookshop never made money.

        ## People in your life
        - Bev, your wife: married thirty-eight years.
        """;

    private const string WithCard = """
        # Roy Callahan

        ## Who you are
        You're 63.

        ## Card
        bio: Roy is 63, a retired ship's engineer with big hands.
        chattiness: 0.35
        impulsivity: 1.4
        """;

    private const string Legacy = """
        # System Prompt: Eiko Tanaka (The Tradition-Reformer)

        ## 1. Persona Definition
        Eiko merges *serene* gravitas with restless ambition. She sketches revolution.

        ## 2. Core Drive
        Something.
        """;

    [Fact]
    public void Lived_NameFromHeading_BioFromWhoYouAre()
    {
        var persona = PersonaController.ParseGeneratedPersona(Lived);
        Assert.Equal("Roy Callahan", persona.Name);
        Assert.StartsWith("# Roy Callahan", persona.SystemPrompt);
        Assert.Equal("You're 63, and you live in Port Townsend. Your wife Bev is a retired nurse. You spent thirty-one years in engine rooms.", persona.Bio);
        Assert.Null(persona.Chattiness);
    }

    [Fact]
    public void Card_FillsFields_AndStaysOutOfTheSheet()
    {
        var persona = PersonaController.ParseGeneratedPersona(WithCard);
        Assert.Equal("Roy is 63, a retired ship's engineer with big hands.", persona.Bio);
        Assert.Equal(0.35, persona.Chattiness);
        Assert.Equal(1, persona.Impulsivity);
        Assert.DoesNotContain("Card", persona.SystemPrompt);
        Assert.EndsWith("You're 63.", persona.SystemPrompt);
    }

    [Fact]
    public void Legacy_StillParses()
    {
        var persona = PersonaController.ParseGeneratedPersona(Legacy);
        Assert.Equal("Eiko Tanaka", persona.Name);
        Assert.Equal("Eiko merges serene gravitas with restless ambition. She sketches revolution.", persona.Bio);
    }

    [Fact]
    public void NoHeading_FallsBack()
        => Assert.Equal("Generated Persona", PersonaController.ParseGeneratedPersona("just some text.").Name);

    [Fact]
    public void Brief_CarriesSeedDrawAndWorld()
    {
        var brief = PersonaBrief.Compose("a tram driver", new Random(1), new[] { "the Café" }, new[] { ("Denise", (string?)"Denise codes."), ("Mara", null) });
        Assert.StartsWith("Seed: a tram driver", brief);
        Assert.Contains(PersonaBrief.Temperaments, t => brief.Contains(t));
        Assert.Contains(PersonaBrief.LifeStages, s => brief.Contains(s));
        Assert.Contains("Places here: the Café", brief);
        Assert.Contains("- Denise: Denise codes.", brief);
        Assert.Contains("- Mara\n", brief + "\n");
    }
}
