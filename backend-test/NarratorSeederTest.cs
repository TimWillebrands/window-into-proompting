using BackendTest.Infrastructure;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Seeding;

namespace BackendTest;

public class NarratorSeederTest : IClassFixture<PartyClusterFixture>
{
    private readonly PartyClusterFixture _fx;

    public NarratorSeederTest(PartyClusterFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task RunAsync_SeedsNarratorLibraryPersona()
    {
        await new NarratorSeeder().RunAsync(_fx.GrainFactory, default);

        var root = _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
        Assert.True(await root.HasPersonaId(Narrator.PersonaId));

        var persona = await _fx.GrainFactory.GetGrain<IPersonaGrain>(Narrator.PersonaId).GetPersona();
        Assert.Equal(Narrator.DisplayName, persona.Name);
        Assert.Equal(Narrator.SystemPrompt, persona.SystemPrompt);
    }

    [Fact]
    public async Task RunAsync_IsIdempotent()
    {
        var seeder = new NarratorSeeder();
        await seeder.RunAsync(_fx.GrainFactory, default);
        await seeder.RunAsync(_fx.GrainFactory, default);

        var root = _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
        Assert.True(await root.HasPersonaId(Narrator.PersonaId));
    }

    [Fact]
    public async Task RunAsync_BackfillsExistingPartyWithNarratorParticipant()
    {
        var partyId = Guid.NewGuid();
        var partyRoot = _fx.GrainFactory.GetGrain<IPartyRootGrain>(Guid.Empty);
        await partyRoot.AddParty(new PartyInfo { Id = partyId, Name = "legacy-party" });

        var before = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetParty();
        Assert.DoesNotContain(before.Participants, p => p.Id == Narrator.PersonaId);

        await new NarratorSeeder().RunAsync(_fx.GrainFactory, default);

        var after = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetParty();
        Assert.Contains(after.Participants, p =>
            p.Id == Narrator.PersonaId && p.Driver == DriverKind.System);
    }
}
