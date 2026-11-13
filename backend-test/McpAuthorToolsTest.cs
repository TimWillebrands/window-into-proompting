using Microsoft.Extensions.Options;
using ModelContextProtocol;
using PartyTown.Grains;
using PartyTown.Mcp;
using PartyTown.Services.Auth;
using PartyTown.World.Places;
using PartyTown.World.Relations;

namespace BackendTest;

/// <summary>The author's tools: a Persona's sheet and who lives with whom, for admins.</summary>
public partial class WorldLifeTest
{
    private AuthorTools AuthorToolsFor(Guid partyId) => new(_fx.GrainFactory, Options.Create(new McpOptions { World = partyId }));

    [Fact]
    public async Task AnAdminsAgent_RewritesASheet_KeepingWhatItLeavesOut()
    {
        var (partyId, _, denise, _) = await PlayerWorldAsync();
        var tools = AuthorToolsFor(partyId);

        await Assert.ThrowsAsync<McpException>(() => tools.EditPersonaAsync(Agent(), denise, "You are someone else."));
        var edited = await tools.EditPersonaAsync(Agent(admin: true), denise, "  You are Denise, and you live alone.  ");

        Assert.Equal(("You are Denise, and you live alone.", "A hacker."), (edited.SystemPrompt, edited.Bio));
        Assert.Equal(edited.SystemPrompt, (await tools.GetPersonaAsync(denise)).SystemPrompt);
        await Assert.ThrowsAsync<McpException>(() => tools.EditPersonaAsync(Agent(admin: true), denise, " "));
        await Assert.ThrowsAsync<McpException>(() => tools.GetPersonaAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AnAdminsAgent_MovesAPairInTogether_AndApart()
    {
        var (partyId, player, denise, _) = await PlayerWorldAsync();
        var tools = AuthorToolsFor(partyId);
        var relation = (await _fx.GrainFactory.GetGrain<IRelationsGrain>(partyId).DeclareAsync(denise, player, RelationKind.Married, "you married in Boxtel", false))!;

        await Assert.ThrowsAsync<McpException>(() => tools.ListRelationsAsync(Agent()));
        Assert.Equal("They live together.", await tools.LiveTogetherAsync(Agent(admin: true), relation.Id, denise));

        var row = Assert.Single(await tools.ListRelationsAsync(Agent(admin: true)));
        Assert.Equal(("Denise", "Sparky", "you married in Boxtel", true), (row.A, row.B, row.Story, row.LiveTogether));
        Assert.Contains(player, (await PlacesOf(partyId).GetSnapshotAsync()).Places.HomeOf(denise)!.Living());

        Assert.Equal("They live apart.", await tools.LiveTogetherAsync(Agent(admin: true), relation.Id));
        Assert.False(Assert.Single(await tools.ListRelationsAsync(Agent(admin: true))).LiveTogether);
    }

    [Fact]
    public async Task AnAdminsAgent_EndsARelation_WhichStaysAsTheirPast()
    {
        var (partyId, player, denise, _) = await PlayerWorldAsync();
        var tools = AuthorToolsFor(partyId);
        var relation = (await _fx.GrainFactory.GetGrain<IRelationsGrain>(partyId).DeclareAsync(denise, player, RelationKind.Married, null, false))!;

        await Assert.ThrowsAsync<McpException>(() => tools.EndRelationAsync(Agent(), relation.Id));
        await tools.EndRelationAsync(Agent(admin: true), relation.Id);

        var row = Assert.Single(await tools.ListRelationsAsync(Agent(admin: true)));
        Assert.Equal((RelationKind.Married, false), (row.Kind, row.Current));
        await Assert.ThrowsAsync<McpException>(() => tools.EndRelationAsync(Agent(admin: true), relation.Id));
    }
}
