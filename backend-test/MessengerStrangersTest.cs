using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Apps;
using PartyTown.World.Relations;
using PartyTown.World.Self;

namespace BackendTest;

/// <summary>Nobody messages a stranger: a new DM goes only to someone they've talked with or have a Relation with.</summary>
public partial class WorldLifeTest
{
    private async Task<AppPerception> MessengerSeenByAsync(Guid partyId, Guid who, IReadOnlySet<Guid> players)
    {
        var cast = await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).GetCastAsync();
        var ctx = new LifeContext(partyId, cast.First(c => c.Id == who), cast, players, new List<ChatGroupInfo>(), new List<WorldActivity>(),
            DateTimeOffset.UtcNow, new Dictionary<Guid, AttentionLook>());
        return (await new MessengerApp(_fx.GrainFactory).PerceiveAsync(ctx, CancellationToken.None))!;
    }

    [Fact]
    public async Task Messenger_OffersNoStranger_NotEvenTheHuman()
    {
        var (partyId, player, denise, _) = await PlayerWorldAsync();

        var seen = await MessengerSeenByAsync(partyId, denise, new HashSet<Guid> { player });

        Assert.DoesNotContain(seen.Affordances, a => a.Verb == "open");
    }

    [Fact]
    public async Task Messenger_OffersSomeoneTheyveTalkedWith_OrHaveARelationWith()
    {
        var (partyId, player, denise, _) = await PlayerWorldAsync();
        var pieter = Guid.NewGuid();
        await _fx.GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(new Persona(pieter, "Pieter", "You are Pieter.", "An architect."));
        await _fx.GrainFactory.GetGrain<IPartyGrain>(partyId).AddParticipantsAsync(new List<PartyParticipant> { new() { Id = pieter, Name = "Pieter", Driver = DriverKind.LLM } });

        await _fx.GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(partyId, denise)).NoteExchangeAsync(new List<Guid> { player }, direct: true);
        await _fx.GrainFactory.GetGrain<IRelationsGrain>(partyId).DeclareAsync(denise, pieter, RelationKind.Friends, null, past: true);

        var seen = await MessengerSeenByAsync(partyId, denise, new HashSet<Guid> { player });

        Assert.Equal(new[] { player, pieter }.OrderBy(id => id), seen.Affordances.Where(a => a.Verb == "open").Select(a => a.TargetId!.Value).OrderBy(id => id));
    }
}
