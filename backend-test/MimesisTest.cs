using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.SystemOne;
using PartyTown.World;
using PartyTown.World.Desires;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Mimetic desire (ADR 0026): caught from people you know, capped hard, and sometimes a rivalry.</summary>
public class MimesisTest : IClassFixture<WorldLifeTest.Fixture>
{
    private readonly WorldLifeTest.Fixture _fx;

    public MimesisTest(WorldLifeTest.Fixture fx) => _fx = fx;

    [Fact]
    public void Rules_CapWhoAndHowOften()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(MimesisRules.Knows(9.5));
        Assert.True(MimesisRules.Knows(10));
        Assert.True(MimesisRules.MayCatch(null, now));
        Assert.False(MimesisRules.MayCatch(now.AddDays(-1).ToUnixTimeMilliseconds(), now));
        Assert.True(MimesisRules.MayCatch(now.AddDays(-4).ToUnixTimeMilliseconds(), now));
        Assert.True(MimesisRules.Names("probably Hana tbh", "Hana Sato"));
        Assert.False(MimesisRules.Names("nobody", "Hana"));
    }

    private Mimesis With(string kind, string want, string about)
    {
        var llm = new Mock<IWorldLlm>();
        llm.Setup(l => l.CompleteJsonAsync(It.IsAny<JobComplexity>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<double?>(), It.IsAny<IReadOnlyList<string>?>()))
            .ReturnsAsync(new JsonObject { ["kind"] = kind, ["want"] = want, ["about"] = about });
        return new Mimesis(_fx.GrainFactory, llm.Object, NullLogger<Mimesis>.Instance);
    }

    private async Task<(Guid Party, ParticipantView Denise, ParticipantView Pieter, ParticipantView Hana)> WorldAsync()
    {
        var (partyId, denise) = await _fx.CreateWorldAsync(withRoom: true);
        var hana = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(hana, "Hana", "You are Hana.", "A baker."));
        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant> { new() { Id = hana, Name = "Hana", Driver = DriverKind.LLM } });
        var cast = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        ParticipantView V(string name) => cast.Single(c => c.Name == name) is var c ? new ParticipantView(c.Id, c.Name, DriverKind.LLM) : default;
        return (partyId, V("Denise"), V("Pieter"), V("Hana"));
    }

    private async Task KnowAsync(Guid partyId, Guid who, Guid other)
    {
        for (var i = 0; i < 12; i++) await _fx.GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, who)).NoteExchangeAsync(new List<Guid> { other }, direct: true);
    }

    private static SystemOneAnswers Yes(ParticipantView who, ParticipantView from)
    {
        var answers = new SystemOneAnswers();
        answers.Nouls[Mimesis.Key(who, from)] = 0.9;
        return answers;
    }

    [Fact]
    public async Task OnlyPeopleYouKnow_AreImitated_AndOnlyOnceInAWhile()
    {
        var (party, denise, pieter, hana) = await WorldAsync();
        var mimesis = With("being", "learn to make proper croissants", "nobody");
        Assert.Empty(await mimesis.EligibleAsync(party, new[] { (denise, pieter) }, DateTimeOffset.UtcNow));

        await KnowAsync(party, denise.Id, pieter.Id);
        var eligible = await mimesis.EligibleAsync(party, new[] { (denise, pieter) }, DateTimeOffset.UtcNow);
        Assert.Single(eligible);
        await mimesis.CatchAsync(party, "transcript", eligible, Yes(denise, pieter), new[] { denise, pieter, hana });

        var caught = (await _fx.GrainFactory.GetGrain<IDesiresGrain>(WorldKeys.Life(party, denise.Id)).GetAsync()).Acquired.Single();
        Assert.Equal(DesireSource.Mimesis, caught.Source);
        Assert.Equal(pieter.Id, caught.FromId);
        Assert.Equal("learn to make proper croissants", caught.Text);
        Assert.Empty(await mimesis.EligibleAsync(party, new[] { (denise, pieter) }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task WantingTheSamePersonsAttention_StartsOneRivalry()
    {
        var (party, denise, pieter, hana) = await WorldAsync();
        await KnowAsync(party, denise.Id, pieter.Id);
        await _fx.GrainFactory.GetGrain<IDesiresGrain>(WorldKeys.Life(party, pieter.Id))
            .AddAsync(new Desire { Kind = DesireKind.Having, Text = "Hana's attention", TargetId = hana.Id, Since = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        var mimesis = With("having", "more of Hana's time", "Hana");
        var eligible = await mimesis.EligibleAsync(party, new[] { (denise, pieter) }, DateTimeOffset.UtcNow);
        await mimesis.CatchAsync(party, "transcript", eligible, Yes(denise, pieter), new[] { denise, pieter, hana });

        Assert.Equal(new[] { denise.Id, pieter.Id }, await _fx.GrainFactory.GetGrain<IMimesisGrain>(party).GetRivalsAsync());
        Assert.True((await _fx.GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(party, pieter.Id)).GetTieAsync(denise.Id)).Warmth < 0);
        Assert.False(await _fx.GrainFactory.GetGrain<IMimesisGrain>(party).TryRivalryAsync(hana.Id, pieter.Id));
    }
}
