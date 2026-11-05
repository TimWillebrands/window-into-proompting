using Orleans.Concurrency;
using PartyTown.World.Places;
using PartyTown.World.Self;

namespace PartyTown.World.Relations;

/// <summary>
/// One Party's Relations (key = Party id, ADR 0035): the one place a pair's facts are written, so both sides agree.
/// Drives Places for living together; Places never calls back.
/// </summary>
public sealed class RelationsGrain(
    [PersistentState(stateName: "relations", storageName: "parties")]
    IPersistentState<RelationBook> state,
    TimeProvider time,
    ILogger<RelationsGrain> logger)
    : Grain, IRelationsGrain
{
    private const int MaxStory = 200;
    private const double Closer = 0.9;
    private const double Stung = 0.3;
    private const double Hurt = 0.1;

    private RelationBook Book => state.State;
    private IPlacesGrain Places => GrainFactory.GetGrain<IPlacesGrain>(this.GetPrimaryKey());

    public Task<RelationBook> GetAsync() => Task.FromResult(Book);

    public async Task<Relation?> DeclareAsync(Guid aId, Guid bId, RelationKind kind, string? story, bool past)
    {
        if (aId == bId) return null;
        var now = Now();
        var text = Clean(story);
        if (!past && Book.CurrentBetween(aId, bId) is { } current)
        {
            current.Kind = kind;
            current.Story = text;
            await state.WriteStateAsync();
            return current;
        }
        var relation = new Relation { AId = aId, BId = bId, Kind = kind, Story = text, Since = now, EndedAt = past ? now : null };
        Book.Relations.Add(relation);
        await state.WriteStateAsync();
        return relation;
    }

    public async Task<bool> EndAsync(Guid aId, Guid bId)
    {
        if (Book.CurrentBetween(aId, bId) is not { } relation) return false;
        if (relation.LiveTogether) await Places.SeparateAsync(aId, bId);
        Close(relation);
        Book.Asks.RemoveAll(a => a.Yes is null && (a.FromId == aId || a.ToId == aId) && (a.FromId == bId || a.ToId == bId));
        await state.WriteStateAsync();
        if (relation.IsRomantic())
        {
            await ShiftAsync(aId, bId, Hurt);
            await ShiftAsync(bId, aId, Hurt);
        }
        return true;
    }

    public async Task<bool> ForgetAsync(Guid relationId)
    {
        if (Book.Relations.FirstOrDefault(r => r.Id == relationId) is not { } relation) return false;
        if (relation.LiveTogether) await Places.SeparateAsync(relation.AId, relation.BId);
        Book.Relations.Remove(relation);
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> SetHomeAsync(Guid relationId, Guid? hostId)
    {
        if (Book.Relations.FirstOrDefault(r => r.Id == relationId && r.IsCurrent()) is not { } relation) return false;
        if (hostId is not { } host)
        {
            if (relation.LiveTogether) await Places.SeparateAsync(relation.AId, relation.BId);
            relation.LiveTogether = false;
        }
        else
        {
            if (!relation.Involves(host) || !await Places.MoveInAsync(relation.Other(host), host)) return false;
            relation.LiveTogether = true;
        }
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> NewHomeAsync(Guid relationId, Guid hostId, string description)
    {
        if (Book.Relations.FirstOrDefault(r => r.Id == relationId && r.IsCurrent()) is not { } relation || !relation.Involves(hostId)) return false;
        if (await Places.BuildHomeAsync(hostId, relation.Other(hostId), description) is null) return false;
        relation.LiveTogether = true;
        await state.WriteStateAsync();
        return true;
    }

    public async Task<bool> AskAsync(Guid fromId, Guid toId, AskKind kind)
    {
        var now = Now();
        if (!Book.MayAsk(fromId, toId, kind, now)) return false;
        Book.Asks.RemoveAll(a => now - a.At > RelationRules.AskAgainAfter.TotalMilliseconds);
        Book.Asks.Add(new RelationAsk { FromId = fromId, ToId = toId, Kind = kind, At = now });
        await state.WriteStateAsync();
        return true;
    }

    public async Task<RelationAsk?> AnswerAsync(Guid fromId, Guid toId, bool yes)
    {
        var now = Now();
        if (Book.OpenTo(toId, now).FirstOrDefault(a => a.FromId == fromId) is not { } ask) return null;
        ask.Yes = yes && await ApplyAsync(ask, now);
        ask.AnsweredAt = now;
        await state.WriteStateAsync();
        await ShiftAsync(fromId, toId, ask.Yes == true ? Closer : Stung);
        if (ask.Yes == true) await ShiftAsync(toId, fromId, Closer);
        return ask;
    }

    public async Task LeaveAsync(Guid participantId)
    {
        foreach (var relation in Book.Relations.Where(r => r.IsCurrent() && r.Involves(participantId))) Close(relation);
        Book.Asks.RemoveAll(a => a.FromId == participantId || a.ToId == participantId);
        await state.WriteStateAsync();
    }

    /// <summary>A yes takes effect; false when things changed since the asking and it no longer can.</summary>
    private async Task<bool> ApplyAsync(RelationAsk ask, long now)
    {
        var between = Book.CurrentBetween(ask.FromId, ask.ToId);
        switch (ask.Kind)
        {
            case AskKind.GoOut:
                if (Book.HasPartner(ask.FromId) || Book.HasPartner(ask.ToId)) return false;
                if (between is null)
                {
                    Book.Relations.Add(new Relation { AId = ask.FromId, BId = ask.ToId, Kind = RelationKind.Together, Since = now });
                    return true;
                }
                between.Kind = RelationKind.Together;
                between.Story = null;
                between.Since = now;
                return true;
            case AskKind.MoveIn:
                if (between?.IsRomantic() != true || between.LiveTogether || !await Places.MoveInAsync(ask.ToId, ask.FromId)) return false;
                between.LiveTogether = true;
                return true;
            default:
                if (between is not { Kind: RelationKind.Together }) return false;
                between.Kind = RelationKind.Married;
                between.Story = null;
                return true;
        }
    }

    private void Close(Relation relation)
    {
        relation.EndedAt = Now();
        relation.Story = null;
        relation.LiveTogether = false;
    }

    private async Task ShiftAsync(Guid who, Guid toward, double closer)
    {
        try { await GrainFactory.GetGrain<IPersonaTiesGrain>(WorldKeys.Life(this.GetPrimaryKey(), who)).ShiftWarmthAsync(toward, closer); }
        catch (Exception ex) { logger.LogDebug(ex, "Warmth shift failed for {Participant}", who); }
    }

    private static string? Clean(string? story)
        => string.IsNullOrWhiteSpace(story) ? null : WorldStream.Clip(story.Trim(), MaxStory);

    private long Now() => time.GetUtcNow().ToUnixTimeMilliseconds();
}

[Alias("PartyTown.World.Relations.IRelationsGrain")]
public interface IRelationsGrain : IGrainWithGuidKey
{
    /// <summary>Read-only; interleaves so grains this one calls can read it back.</summary>
    [AlwaysInterleave, Alias("GetAsync")] Task<RelationBook> GetAsync();
    /// <summary>Author: state what a pair is, or was with <paramref name="past"/>. Replaces their current kind and story.</summary>
    [Alias("DeclareAsync")] Task<Relation?> DeclareAsync(Guid aId, Guid bId, RelationKind kind, string? story, bool past);
    /// <summary>Their current Relation becomes a past one, and a shared Home splits.</summary>
    [Alias("EndAsync")] Task<bool> EndAsync(Guid aId, Guid bId);
    /// <summary>Author: strike it, as if it never was.</summary>
    [Alias("ForgetAsync")] Task<bool> ForgetAsync(Guid relationId);
    /// <summary>Author: the other one moves in with <paramref name="hostId"/>; null splits a shared Home.</summary>
    [Alias("SetHomeAsync")] Task<bool> SetHomeAsync(Guid relationId, Guid? hostId);
    /// <summary>Author: a Home designed from <paramref name="description"/>, owned by the host; the Homes they had are removed.</summary>
    [Alias("NewHomeAsync")] Task<bool> NewHomeAsync(Guid relationId, Guid hostId, string description);
    [Alias("AskAsync")] Task<bool> AskAsync(Guid fromId, Guid toId, AskKind kind);
    /// <summary>The Ask as it stands after answering; null when none is open.</summary>
    [Alias("AnswerAsync")] Task<RelationAsk?> AnswerAsync(Guid fromId, Guid toId, bool yes);
    /// <summary>Someone left the World: what they were part of is over.</summary>
    [Alias("LeaveAsync")] Task LeaveAsync(Guid participantId);
}
