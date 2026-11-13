using System.ComponentModel;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World.Relations;

namespace PartyTown.Mcp;

/// <summary>The author's side (ADR 0046): a Persona's sheet, as PersonaController has it, and who's what to whom (ADR 0035).</summary>
[McpServerToolType]
public sealed class AuthorTools(IGrainFactory grains, IOptions<McpOptions> mcp)
{
    /// <summary>Set by Guard before each tool runs.</summary>
    private Guid World { get; set; }
    private const string Whom = "Participant id from list_participants.";

    [McpServerTool(Name = "get_persona", ReadOnly = true), Description("Someone's sheet as authored: name, bio, aura and system prompt. The World reads a distilled version of the prompt.")]
    public Task<PersonaSheet> GetPersonaAsync([Description(Whom)] Guid participantId) => Guard(async () =>
        PersonaSheet.Of(await PersonaAsync(participantId)));

    [McpServerTool(Name = "edit_persona"), Description("Admins: rewrite someone's system prompt and/or bio; what you leave out stays. The sheet is who they are in every World: Nature, not what's going on today.")]
    public Task<PersonaSheet> EditPersonaAsync(
        ClaimsPrincipal user,
        [Description(Whom)] Guid participantId,
        [Description("The whole new system prompt.")] string? systemPrompt = null,
        [Description("The whole new bio: plain fact, third person, 2-3 sentences.")] string? bio = null) => Guard(async () =>
    {
        RequireAdmin(user);
        if (systemPrompt is not null && string.IsNullOrWhiteSpace(systemPrompt)) throw new McpException("A system prompt can't be blank.");
        var persona = await PersonaAsync(participantId);
        await grains.GetGrain<IPersonaRootGrain>(Guid.Empty).AddPersona(persona with
        {
            SystemPrompt = systemPrompt?.Trim() ?? persona.SystemPrompt,
            Bio = bio is null ? persona.Bio : bio.Trim(),
        });
        return PersonaSheet.Of(await grains.GetGrain<IPersonaGrain>(participantId).GetPersona());
    });

    [McpServerTool(Name = "list_relations", ReadOnly = true), Description("Admins: who's what to whom in this World, in their own words, and who shares a Home; ids for live_together.")]
    public Task<List<RelationRow>> ListRelationsAsync(ClaimsPrincipal user) => Guard(async () =>
    {
        RequireAdmin(user);
        var names = (await grains.GetGrain<IPartyGrain>(World).GetCastAsync()).ToDictionary(c => c.Id, c => c.Name);
        return (await grains.GetGrain<IRelationsGrain>(World).GetAsync()).Relations
            .Select(r => new RelationRow(r.Id, names.GetValueOrDefault(r.AId, "?"), r.AId, names.GetValueOrDefault(r.BId, "?"), r.BId,
                r.Kind, r.Story, r.EndedAt is null, r.LiveTogether))
            .ToList();
    });

    [McpServerTool(Name = "live_together"), Description("Admins: one of a current Relation moves into the other's Home (hostId, one of the pair); no hostId splits a shared Home.")]
    public Task<string> LiveTogetherAsync(
        ClaimsPrincipal user,
        [Description("Relation id from list_relations.")] Guid relationId,
        [Description("Whose Home they share. Leave out to split.")] Guid? hostId = null) => Guard(async () =>
    {
        RequireAdmin(user);
        if (!await grains.GetGrain<IRelationsGrain>(World).SetHomeAsync(relationId, hostId))
            throw new McpException("Couldn't: it's not a current Relation, the host isn't one of them, or one of them shares another Home already.");
        return hostId is null ? "They live apart." : "They live together.";
    });

    [McpServerTool(Name = "end_relation"), Description("Admins: a current Relation is over. It stays on record as their past, and a shared Home splits.")]
    public Task<string> EndRelationAsync(ClaimsPrincipal user, [Description("Relation id from list_relations.")] Guid relationId) => Guard(async () =>
    {
        RequireAdmin(user);
        var relations = grains.GetGrain<IRelationsGrain>(World);
        var relation = (await relations.GetAsync()).Relations.FirstOrDefault(r => r.Id == relationId && r.EndedAt is null)
            ?? throw new McpException("No current Relation by that id.");
        if (!await relations.EndAsync(relation.AId, relation.BId)) throw new McpException("Couldn't end it.");
        return "It's over; it stays as their past.";
    });

    private async Task<Persona> PersonaAsync(Guid participantId)
    {
        if (participantId == Narrator.PersonaId || (await grains.GetGrain<IPartyGrain>(World).GetCastAsync()).All(c => c.Id != participantId))
            throw new McpException("No one by that id in this World.");
        return await grains.GetGrain<IPersonaGrain>(participantId).GetPersona();
    }

    private static void RequireAdmin(ClaimsPrincipal user)
    {
        if (!Actors.IsAdmin(user)) throw new McpException("Only admins may do that.");
    }

    private async Task<T> Guard<T>(Func<Task<T>> run)
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        World = mcp.Value.World ?? await root.GetCurrentAsync();
        if (await root.IsStoppedAsync(World)) throw new McpException(StoppedWorldFilter.Title);
        return await run();
    }
}

public sealed record PersonaSheet(Guid Id, string Name, string? Bio, string? Aura, string SystemPrompt)
{
    public static PersonaSheet Of(Persona p) => new(p.Id, p.Name, p.Bio, p.Aura, p.SystemPrompt);
}

public sealed record RelationRow(Guid Id, string A, Guid AId, string B, Guid BId, RelationKind Kind, string? Story, bool Current, bool LiveTogether);
