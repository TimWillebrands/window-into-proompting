using BackendTest.Infrastructure;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Seeding;

namespace BackendTest;

/// <summary>
/// Behavioural tests for <see cref="NarratorSeeder"/>: ensures the singleton library Narrator
/// Persona exists, and that every existing Party has a Narrator-Participant after back-fill.
/// Issue #75 / ADR 0012.
///
/// One fixture per class, so PersonaRootGrain and PartyRootGrain state persists across the
/// three tests below. Tests are written to be order-independent (the seeder is idempotent and
/// each test uses a fresh Party id).
/// </summary>
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
        // Simulate a legacy Party that pre-dates this PR: created via AddParty but never
        // routed through SetParticipants. The seeder must add Narrator on its behalf.
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
