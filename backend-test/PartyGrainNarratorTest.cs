using BackendTest.Infrastructure;
using PartyTown.Grains;
using PartyTown.Model;

namespace BackendTest;

/// <summary>
/// Auto-grow invariant: every Party carries the singleton <see cref="Narrator"/> as a
/// <see cref="DriverKind.System"/> Participant. Whoever calls
/// <see cref="IPartyGrain.SetParticipants"/> doesn't have to know about Narrator — the
/// grain enforces it. Issue #75 / ADR 0012.
/// </summary>
public class PartyGrainNarratorTest : IClassFixture<PartyClusterFixture>
{
    private readonly PartyClusterFixture _fx;

    public PartyGrainNarratorTest(PartyClusterFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task SetParticipants_AddsNarratorWhenAbsent()
    {
        var partyId = Guid.NewGuid();
        var alice = new PartyParticipant { Id = Guid.NewGuid(), Name = "Alice", Driver = DriverKind.LLM };

        var root = _fx.GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await root.AddParty(new PartyInfo { Id = partyId, Name = "test-party" });

        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        await party.SetParticipants(new List<PartyParticipant> { alice });

        var result = await party.GetParty();
        Assert.Contains(result.Participants, p =>
            p.Id == Narrator.PersonaId && p.Driver == DriverKind.System && p.Name == Narrator.DisplayName);
        Assert.Contains(result.Participants, p => p.Id == alice.Id && p.Driver == DriverKind.LLM);
        Assert.Equal(2, result.Participants.Count);
    }

    [Fact]
    public async Task SetParticipants_IsIdempotent_WhenNarratorAlreadyPresent()
    {
        var partyId = Guid.NewGuid();
        var alice = new PartyParticipant { Id = Guid.NewGuid(), Name = "Alice", Driver = DriverKind.LLM };
        var narrator = new PartyParticipant
        {
            Id = Narrator.PersonaId,
            Name = Narrator.DisplayName,
            Driver = DriverKind.System,
        };

        var root = _fx.GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await root.AddParty(new PartyInfo { Id = partyId, Name = "test-party" });

        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        await party.SetParticipants(new List<PartyParticipant> { alice, narrator });
        await party.SetParticipants(new List<PartyParticipant> { alice, narrator }); // resubmit

        var result = await party.GetParty();
        Assert.Equal(2, result.Participants.Count);
        Assert.Single(result.Participants, p => p.Id == Narrator.PersonaId);
    }

    [Fact]
    public async Task SetParticipants_NormalizesNarratorDriver_WhenCallerMislabels()
    {
        // Even if the caller submits an entry with the Narrator's Id but the wrong Driver,
        // the grain forces it back to System. This protects the pipeline-guard invariant
        // (a Narrator row that accidentally carries Driver=LLM would otherwise start speaking).
        var partyId = Guid.NewGuid();
        var mislabelled = new PartyParticipant
        {
            Id = Narrator.PersonaId,
            Name = "Whatever",
            Driver = DriverKind.LLM,
        };

        var root = _fx.GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await root.AddParty(new PartyInfo { Id = partyId, Name = "test-party" });

        var party = _fx.GrainFactory.GetGrain<IPartyGrain>(partyId);
        await party.SetParticipants(new List<PartyParticipant> { mislabelled });

        var result = await party.GetParty();
        var n = Assert.Single(result.Participants, p => p.Id == Narrator.PersonaId);
        Assert.Equal(DriverKind.System, n.Driver);
        Assert.Equal(Narrator.DisplayName, n.Name);
    }
}
