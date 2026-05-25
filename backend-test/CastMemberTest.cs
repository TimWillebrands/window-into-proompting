using PartyTown.Model;

namespace BackendTest;

/// <summary>
/// Pure tests for <see cref="CastMember.Create"/> and the
/// <see cref="CastMember.ToView"/> / <see cref="CastMember.ToSelfView"/> projections.
/// No grain dependency, no TestCluster.
/// </summary>
public class CastMemberTest
{
    private static Persona LibraryPersona(Guid id, string libraryName, string bio = "lib bio") =>
        new(id, libraryName, $"You are {libraryName}.", bio, chattiness: 0.7, impulsivity: 0.4);

    [Fact]
    public void Create_UserLink_ReturnsSparseUserDriven_RegardlessOfLibrary()
    {
        // A User-driven link is sparse — bio and system prompt are intentionally null even
        // when a library entry exists, because the human types the messages.
        var id = Guid.NewGuid();
        var link = new PartyParticipant { Id = id, Name = "Tim", Driver = DriverKind.User };
        var libraryHit = LibraryPersona(id, "Tim-the-Persona");

        var sparseNoLib = CastMember.Create(link, persona: null);
        var sparseWithLib = CastMember.Create(link, libraryHit);

        foreach (var c in new[] { sparseNoLib, sparseWithLib })
        {
            Assert.Equal(id, c.Id);
            Assert.Equal("Tim", c.Name);
            Assert.Equal(DriverKind.User, c.DefaultDriver);
            Assert.Null(c.Bio);
            Assert.Null(c.SystemPrompt);
            Assert.Equal(0.5, c.Chattiness);
            Assert.Equal(0.5, c.Impulsivity);
        }
    }

    [Fact]
    public void Create_LlmLinkWithLibraryHit_FullJoin_NameComesFromLink()
    {
        // Name on the join is the per-Party display name from the link, NOT the library
        // entry's library name. Locks the deliberate behaviour change from the old
        // BuildGenerationParticipantsAsync which used the library entry's name.
        var id = Guid.NewGuid();
        var link = new PartyParticipant { Id = id, Name = "Vlad in this Party", Driver = DriverKind.LLM };
        var lib = LibraryPersona(id, "Vlad-library-name", bio: "broody");

        var c = CastMember.Create(link, lib);

        Assert.Equal(id, c.Id);
        Assert.Equal("Vlad in this Party", c.Name);
        Assert.Equal(DriverKind.LLM, c.DefaultDriver);
        Assert.Equal("broody", c.Bio);
        Assert.Equal("You are Vlad-library-name.", c.SystemPrompt);
        Assert.Equal(0.7, c.Chattiness);
        Assert.Equal(0.4, c.Impulsivity);
    }

    [Fact]
    public void Create_LlmLinkWithLibraryMiss_ReturnsSparseLlmDefault()
    {
        // Defensive fallback for an LLM link without a library entry (deleted Persona,
        // race during reload, etc.). Sparse, but still LLM-default — the pipeline can
        // skip it via the User-Effective short-circuit only if explicitly overridden.
        var id = Guid.NewGuid();
        var link = new PartyParticipant { Id = id, Name = "Mystery", Driver = DriverKind.LLM };

        var c = CastMember.Create(link, persona: null);

        Assert.Equal(id, c.Id);
        Assert.Equal("Mystery", c.Name);
        Assert.Equal(DriverKind.LLM, c.DefaultDriver);
        Assert.Null(c.Bio);
        Assert.Null(c.SystemPrompt);
        Assert.Equal(0.5, c.Chattiness);
        Assert.Equal(0.5, c.Impulsivity);
    }

    [Fact]
    public void ToView_CarriesSuppliedEffectiveDriver_NotDefault()
    {
        // The Effective Driver is resolved at the call site (PersonaGrain consulting the
        // Room override map). The CastMember does not "know" it; ToView accepts the
        // resolved value verbatim.
        var id = Guid.NewGuid();
        var link = new PartyParticipant { Id = id, Name = "Vlad", Driver = DriverKind.LLM };
        var c = CastMember.Create(link, LibraryPersona(id, "Vlad"));

        var asUser = c.ToView(DriverKind.User);
        var asLlm = c.ToView(DriverKind.LLM);

        Assert.Equal(DriverKind.User, asUser.Driver);
        Assert.Equal(DriverKind.LLM, asLlm.Driver);
        Assert.Equal(id, asUser.Id);
        Assert.Equal("Vlad", asUser.Name);
    }

    [Fact]
    public void ToSelfView_CarriesSuppliedEffectiveDriverAndFullLibraryPayload()
    {
        var id = Guid.NewGuid();
        var link = new PartyParticipant { Id = id, Name = "Vlad", Driver = DriverKind.LLM };
        var c = CastMember.Create(link, LibraryPersona(id, "Vlad", bio: "deep"));

        var s = c.ToSelfView(DriverKind.LLM);

        Assert.Equal(id, s.Id);
        Assert.Equal("Vlad", s.Name);
        Assert.Equal(DriverKind.LLM, s.Driver);
        Assert.Equal("deep", s.Bio);
        Assert.Equal("You are Vlad.", s.SystemPrompt);
        Assert.Equal(0.7, s.Chattiness);
        Assert.Equal(0.4, s.Impulsivity);
    }
}
