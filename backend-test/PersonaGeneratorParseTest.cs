using PartyTown.Controllers;

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
        var (name, prompt, bio) = PersonaController.ParseGeneratedPersona(Lived);
        Assert.Equal("Roy Callahan", name);
        Assert.StartsWith("# Roy Callahan", prompt);
        Assert.Equal("You're 63, and you live in Port Townsend. Your wife Bev is a retired nurse. You spent thirty-one years in engine rooms.", bio);
    }

    [Fact]
    public void Legacy_StillParses()
    {
        var (name, _, bio) = PersonaController.ParseGeneratedPersona(Legacy);
        Assert.Equal("Eiko Tanaka", name);
        Assert.Equal("Eiko merges serene gravitas with restless ambition. She sketches revolution.", bio);
    }

    [Fact]
    public void NoHeading_FallsBack()
        => Assert.Equal("Generated Persona", PersonaController.ParseGeneratedPersona("just some text.").Name);
}
