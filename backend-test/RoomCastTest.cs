using PartyTown.Model;

namespace BackendTest;

public class RoomCastTest
{
    private static readonly Guid Vlad = Guid.NewGuid();
    private static readonly Guid Denise = Guid.NewGuid();
    private static readonly Guid Tim = Guid.NewGuid();

    private static CastMember Member(Guid id, string name, DriverKind driver) =>
        CastMember.Create(
            new PartyParticipant { Id = id, Name = name, Driver = driver },
            new Persona(id, name, $"You are {name}.", $"{name} bio", chattiness: 0.8));

    private static readonly CastMember[] Party =
    {
        Member(Vlad, "Vlad", DriverKind.User),
        Member(Denise, "Denise", DriverKind.LLM),
        Member(Tim, "Tim", DriverKind.LLM),
    };

    private static RoomCast Cast(IEnumerable<Guid> members, string? kind = null)
        => RoomCast.Create(Party, members.ToHashSet(), kind);

    [Fact]
    public void LlmDefault_SpeaksWithFullLibraryPayload()
    {
        var cast = Cast(new[] { Vlad, Denise });

        var self = cast.Speaker(Denise);

        Assert.NotNull(self);
        Assert.Equal("Denise bio", self.Value.Bio);
        Assert.Equal("You are Denise.", self.Value.SystemPrompt);
        Assert.Equal(0.8, self.Value.Chattiness);
    }

    [Fact]
    public void Speaker_NullWhenUserDrivenOrAbsent()
    {
        var cast = Cast(new[] { Vlad, Denise });

        Assert.Null(cast.Speaker(Vlad));
        Assert.Null(cast.Speaker(Tim));
        Assert.Null(cast.EffectiveOf(Tim));
        Assert.Equal(DriverKind.User, cast.EffectiveOf(Vlad));
    }

    [Fact]
    public void Views_PresentOnly_WithDefaultDriver()
    {
        var cast = Cast(new[] { Vlad, Denise });

        var views = cast.Views().ToDictionary(v => v.Id, v => v.Driver);

        Assert.Equal(2, views.Count);
        Assert.Equal(DriverKind.User, views[Vlad]);
        Assert.Equal(DriverKind.LLM, views[Denise]);
    }

    [Fact]
    public void EmptyMembership_MeansNobody()
    {
        var cast = Cast(Array.Empty<Guid>());

        Assert.Empty(cast.Views());
        Assert.Null(cast.Speaker(Denise));
    }

    [Fact]
    public void IsDirect_ByKind_NotHeadCount()
    {
        Assert.False(Cast(new[] { Vlad, Denise }, kind: ChatGroupKinds.Group).IsDirect);
        Assert.True(Cast(new[] { Vlad, Denise }, kind: ChatGroupKinds.Direct).IsDirect);
        Assert.False(Cast(new[] { Vlad, Denise }).IsDirect);
    }
}
