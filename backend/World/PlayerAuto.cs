using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Auth;
using PartyTown.World.Desires;
using PartyTown.World.Places;
using PartyTown.World.Work;

namespace PartyTown.World;

/// <summary>Hands a Player's character to the agent stack and back (ADR 0037): only the Default Driver moves, ownership stays.</summary>
public sealed class PlayerAuto(IGrainFactory grains, IWorldLlm llm, ILogger<PlayerAuto> logger)
{
    /// <summary>What a new Player's sheet says until they describe their character.</summary>
    public const string HumanSheet = "A human visiting this world.";

    private const int MaxLines = 40;
    private const int MaxSheet = 2000;

    private const string SheetPrompt = """
        You describe a character in a small online world from what they've left behind, so someone else can carry on
        living as them. Third person, plain prose, 80-200 words: who they seem to be, what they care about, how they
        write. Stay close to the evidence and keep it ordinary where it's thin. No quotes, no contact details.
        Output only the description.
        """;

    /// <summary>The sheet written for them, kept for next time; null before their first time on auto.</summary>
    public async Task<string?> SheetAsync(Guid playerId)
        => (await grains.GetGrain<IPersonaGrain>(playerId).GetPersona()).SystemPrompt is { Length: > 0 } sheet && sheet != HumanSheet ? sheet : null;

    /// <summary>What they were asked to go after, until it's done, fades or is replaced.</summary>
    public async Task<string?> MissionAsync(Guid partyId, Guid playerId)
        => (await Desires(partyId, playerId).GetAsync()).Acquired.FirstOrDefault(IsMission)?.Text;

    /// <param name="mission">Replaces any running mission; blank clears it. Taking over ends it.</param>
    public async Task SetAsync(Guid partyId, Guid playerId, bool on, string? mission)
    {
        var party = grains.GetGrain<IPartyGrain>(partyId);
        var link = (await party.GetParty()).Participants.First(p => p.Id == playerId);
        var persona = grains.GetGrain<IPersonaGrain>(playerId);
        var places = grains.GetGrain<IPlacesGrain>(partyId);

        if (on)
        {
            if (await SheetAsync(playerId) is null)
            {
                await persona.SetSystemPrompt(await WriteSheetAsync(partyId, link));
                await persona.SetBio(null);
            }
            await places.HandOverAsync(playerId);
            await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, playerId)).ReseedAsync();
        }
        else await persona.CancelGenerationAsync();

        await party.UpsertParticipantsAsync(new List<PartyParticipant> { link with { Driver = on ? DriverKind.LLM : DriverKind.User } });
        await places.GetSnapshotAsync();
        await SetMissionAsync(partyId, playerId, on ? mission : null);
    }

    private IDesiresGrain Desires(Guid partyId, Guid playerId) => grains.GetGrain<IDesiresGrain>(WorldKeys.Life(partyId, playerId));

    private static bool IsMission(Desire d) => d.Source == DesireSource.Mission && d.Status == DesireStatus.Active;

    private async Task SetMissionAsync(Guid partyId, Guid playerId, string? mission)
    {
        var desires = Desires(partyId, playerId);
        var running = (await desires.GetAsync()).Acquired.Where(IsMission).ToList();
        var next = mission?.Trim();
        if (running.Any(d => d.Text == next)) return;
        foreach (var old in running) await desires.MarkAsync(old.Id, DesireStatus.Abandoned);

        var kind = next is not null && Economy.Catalog.Match(next, Economy.Catalog.All) is not null ? DesireKind.Having : DesireKind.Being;
        if (DesireRules.Parse(next, kind, DesireSource.Mission, DateTimeOffset.UtcNow) is { } desire) await desires.AddAsync(desire);
    }

    /// <summary>Their look, job and public lines. The sheet is public, so DMs stay out of it.</summary>
    public async Task<string> EvidenceAsync(Guid partyId, Guid playerId, string name)
    {
        var parts = new List<string> { $"Name: {name}" };
        var body = (await grains.GetGrain<IPlacesGrain>(partyId).GetSnapshotAsync()).Bodies.FirstOrDefault(b => b.ParticipantId == playerId);
        if (body?.Appearance is { } look) parts.Add($"Looks: {look.Summary ?? AppearanceRules.Describe(look)}");
        if ((await grains.GetGrain<IOccupationGrain>(WorldKeys.Life(partyId, playerId)).GetAsync()).Job is { } job) parts.Add($"Work: {job.Title}");

        var lines = new List<(long At, string Text)>();
        foreach (var group in (await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups()).Where(g => g.Kind != ChatGroupKinds.Direct))
            lines.AddRange((await grains.GetGrain<IChatGroupGrain>(group.Id).GetMessagesAsync())
                .Where(m => m.SenderId == playerId && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => (m.SendAt ?? 0, m.Content!)));
        foreach (var thread in await grains.GetGrain<IBoardGrain>(partyId).GetThreadsAsync())
        {
            if (thread.AuthorId == playerId) lines.Add((thread.CreatedAt, thread.Body));
            lines.AddRange(thread.Replies.Where(r => r.AuthorId == playerId).Select(r => (r.CreatedAt, r.Body)));
        }
        var recent = lines.OrderByDescending(l => l.At).Take(MaxLines).Select(l => $"- {WorldStream.Clip(l.Text, 200)}").ToList();
        if (recent.Count > 0) parts.Add($"Things they've said in public:\n{string.Join("\n", recent)}");
        return string.Join("\n\n", parts);
    }

    /// <summary>Who they've been so far, read off their trace; with no model, one line the distilling fleshes out.</summary>
    private async Task<string> WriteSheetAsync(Guid partyId, PartyParticipant link)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            var text = await llm.For(link.Id).CompleteAsync(JobComplexity.CharacterVoice, SheetPrompt, await EvidenceAsync(partyId, link.Id, link.Name), cts.Token, 0.5);
            if (!string.IsNullOrWhiteSpace(text)) return WorldStream.Clip(text.Trim(), MaxSheet);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Writing a sheet for {Player} failed", link.Name);
        }
        return $"{link.Name} is an ordinary person living in this world.";
    }
}

public static class PlayerAutoExtensions
{
    /// <summary>Everyone a human owns or drives here; see <see cref="AccessRules.Humans"/>.</summary>
    public static async Task<IReadOnlySet<Guid>> HumansAsync(this IGrainFactory grains, Guid partyId)
        => AccessRules.Humans(
            (await grains.GetGrain<IPartyGrain>(partyId).GetParty()).Participants,
            await grains.GetGrain<IWorldClockGrain>(partyId).GetClaimedAsync());

    /// <summary>Owned by a human, driven by the agent.</summary>
    public static async Task<bool> IsOnAutoAsync(this IGrainFactory grains, Guid partyId, Guid playerId)
        => (await grains.GetGrain<IPartyGrain>(partyId).GetParty()).Participants.Any(p => p.Id == playerId && p.Driver == DriverKind.LLM)
            && await grains.GetGrain<IWorldClockGrain>(partyId).IsClaimedAsync(playerId);
}
