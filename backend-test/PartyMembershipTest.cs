using BackendTest.Infrastructure;
using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>Membership intent commands and Room cast resolution through the real grains.</summary>
public class PartyMembershipTest : IClassFixture<PartyClusterFixture>
{
    private readonly PartyClusterFixture _fx;

    public PartyMembershipTest(PartyClusterFixture fx) => _fx = fx;

    private static PartyParticipant Link(Guid id, string name, DriverKind driver) => new() { Id = id, Name = name, Driver = driver };

    private async Task<IPartyGrain> NewPartyAsync()
    {
        var partyId = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty).AddParty(new PartyInfo { Id = partyId, Name = "p" });
        return _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
    }

    [Fact]
    public async Task AddParticipants_AddsOnlyAbsent_AndReturnsThem()
    {
        var party = await NewPartyAsync();
        var alice = Link(Guid.NewGuid(), "Alice", DriverKind.LLM);
        await party.AddParticipantsAsync(new List<PartyParticipant> { alice });

        var added = await party.AddParticipantsAsync(new List<PartyParticipant>
        {
            alice with { Driver = DriverKind.User },
            Link(Guid.NewGuid(), "Bob", DriverKind.LLM),
        });

        Assert.Equal(new[] { "Bob" }, added.Select(p => p.Name));
        var info = await party.GetParty();
        Assert.Equal(DriverKind.LLM, info.Participants.Single(p => p.Id == alice.Id).Driver);
        Assert.Contains(info.Participants, p => p.Id == Narrator.PersonaId);
    }

    [Fact]
    public async Task Upsert_Overwrites_Remove_ProtectsNarrator()
    {
        var party = await NewPartyAsync();
        var alice = Link(Guid.NewGuid(), "Alice", DriverKind.LLM);
        await party.AddParticipantsAsync(new List<PartyParticipant> { alice });

        await party.UpsertParticipantsAsync(new List<PartyParticipant> { alice with { Driver = DriverKind.User } });
        Assert.Equal(DriverKind.User, (await party.GetParty()).Participants.Single(p => p.Id == alice.Id).Driver);

        Assert.Equal(alice.Id, (await party.RemoveParticipantAsync(alice.Id))?.Id);
        Assert.Null(await party.RemoveParticipantAsync(alice.Id));
        Assert.Null(await party.RemoveParticipantAsync(Narrator.PersonaId));
        Assert.Contains((await party.GetParty()).Participants, p => p.Id == Narrator.PersonaId);
    }

    [Fact]
    public async Task Join_DropsIdsOutsidePartyCast()
    {
        var party = await NewPartyAsync();
        var alice = Link(Guid.NewGuid(), "Alice", DriverKind.LLM);
        await party.AddParticipantsAsync(new List<PartyParticipant> { alice });
        var room = _fx.GrainFactory.GetGrain<IChatGroupGrain>((await party.CreateChatGroup("kitchen")).Id);
        await room.SetParticipantIdsAsync(new HashSet<Guid>());

        var stranger = Guid.NewGuid();
        var members = await room.JoinAsync(new List<Guid> { alice.Id, stranger });

        Assert.Contains(alice.Id, members);
        Assert.DoesNotContain(stranger, members);
    }

    [Fact]
    public async Task RoomCast_UserDefaultOverriddenToLlm_SpeaksAsLibraryPersona()
    {
        var party = await NewPartyAsync();
        var vlad = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty)
            .AddPersona(new Persona(vlad, "Vlad", "You are Vlad.", "broody"));
        await party.AddParticipantsAsync(new List<PartyParticipant> { Link(vlad, "Vlad", DriverKind.User) });
        var room = _fx.GrainFactory.GetGrain<IChatGroupGrain>((await party.CreateChatGroup("kitchen")).Id);
        await room.JoinAsync(new List<Guid> { vlad });
        await room.SetDriverOverridesAsync(new Dictionary<Guid, DriverKind> { [vlad] = DriverKind.LLM });

        var self = (await room.GetCastAsync()).Speaker(vlad);

        Assert.NotNull(self);
        Assert.Equal("broody", self.Value.Bio);
        Assert.Equal("You are Vlad.", self.Value.SystemPrompt);
    }
}
