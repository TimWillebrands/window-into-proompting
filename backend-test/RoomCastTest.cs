using PartyTown.Model;

namespace BackendTest;

/// <summary>Pure tests for <see cref="RoomCast"/> — the Effective Driver rule's single home.</summary>
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

    private static RoomCast Cast(IEnumerable<Guid> members, Dictionary<Guid, DriverKind>? overrides = null, string? kind = null)
        => RoomCast.Create(Party, members.ToHashSet(), overrides ?? new(), kind);

    [Fact]
    public void UserDefault_OverriddenToLlm_SpeaksWithFullLibraryPayload()
    {
        // CONTEXT.md's own example: Vlad-as-LLM in the kitchen though he's normally the User.
        var cast = Cast(new[] { Vlad, Denise }, new() { [Vlad] = DriverKind.LLM });

        var self = cast.Speaker(Vlad);

        Assert.NotNull(self);
        Assert.Equal("Vlad bio", self.Value.Bio);
        Assert.Equal("You are Vlad.", self.Value.SystemPrompt);
        Assert.Equal(0.8, self.Value.Chattiness);
    }

    [Fact]
    public void Speaker_NullWhenUserEffectiveOrAbsent()
    {
        var cast = Cast(new[] { Vlad, Denise }, new() { [Denise] = DriverKind.User });

        Assert.Null(cast.Speaker(Vlad));
        Assert.Null(cast.Speaker(Denise));
        Assert.Null(cast.Speaker(Tim));
        Assert.Null(cast.EffectiveOf(Tim));
    }

    [Fact]
    public void Views_PresentOnly_WithEffectiveDriver()
    {
        var cast = Cast(new[] { Vlad, Denise }, new() { [Vlad] = DriverKind.LLM });

        var views = cast.Views().ToDictionary(v => v.Id, v => v.Driver);

        Assert.Equal(2, views.Count);
        Assert.Equal(DriverKind.LLM, views[Vlad]);
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
        Assert.False(Cast(new[] { Vlad, Denise }, kind: ChatGroupKinds.Room).IsDirect);
        Assert.True(Cast(new[] { Vlad, Denise }, kind: ChatGroupKinds.Direct).IsDirect);
        Assert.False(Cast(new[] { Vlad, Denise }).IsDirect);
    }
}
