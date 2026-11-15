using PartyTown.Model;

namespace BackendTest;

public class CastMemberTest
{
    private static Persona LibraryPersona(Guid id, string libraryName, string bio = "lib bio") =>
        new(id, libraryName, $"You are {libraryName}.", bio, chattiness: 0.7, impulsivity: 0.4);

    [Fact]
    public void Create_UserLinkWithLibraryHit_CarriesJoin_ForLlmOverrides()
    {
        var id = Guid.NewGuid();
        var link = new PartyParticipant { Id = id, Name = "Vlad", Driver = DriverKind.User };

        var c = CastMember.Create(link, LibraryPersona(id, "Vlad-lib", bio: "broody"));

        Assert.Equal(DriverKind.User, c.DefaultDriver);
        Assert.Equal("Vlad", c.Name);
        Assert.Equal("broody", c.Bio);
        Assert.Equal("You are Vlad-lib.", c.SystemPrompt);
        Assert.Equal(0.7, c.Chattiness);
    }

    [Fact]
    public void Create_SystemLinkOrLibraryMiss_IsSparse()
    {
        var id = Guid.NewGuid();
        var system = new PartyParticipant { Id = id, Name = "Narrator", Driver = DriverKind.System };
        var user = new PartyParticipant { Id = id, Name = "Tim", Driver = DriverKind.User };

        foreach (var c in new[] { CastMember.Create(system, LibraryPersona(id, "N")), CastMember.Create(user, persona: null) })
        {
            Assert.Null(c.Bio);
            Assert.Null(c.SystemPrompt);
            Assert.Equal(0.5, c.Chattiness);
        }
    }

    [Fact]
    public void Create_LlmLinkWithLibraryHit_FullJoin_NameComesFromLink()
    {
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
