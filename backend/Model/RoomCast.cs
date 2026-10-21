namespace PartyTown.Model;

/// <summary>
/// Who is in one Room and who drives them: the present Participants joined with the
/// library, plus the Room's override map. Single home of the Effective Driver rule.
/// </summary>
[GenerateSerializer, Alias(nameof(RoomCast))]
public sealed record class RoomCast
{
    /// <summary>Present Participants only (Room membership ∩ Party cast).</summary>
    [Id(0)] public List<CastMember> Members { get; init; } = new();

    [Id(1)] public Dictionary<Guid, DriverKind> Overrides { get; init; } = new();

    [Id(2)] public string Kind { get; init; } = ChatGroupKinds.Room;

    /// <summary>Messenger thread (ADR 0018) — by Kind, never by head count.</summary>
    public bool IsDirect => Kind == ChatGroupKinds.Direct;

    /// <summary>Pure factory: an empty membership means nobody is present.</summary>
    public static RoomCast Create(
        IEnumerable<CastMember> partyCast,
        IReadOnlySet<Guid> memberIds,
        IReadOnlyDictionary<Guid, DriverKind> overrides,
        string? kind)
        => new()
        {
            Members = partyCast.Where(c => memberIds.Contains(c.Id)).ToList(),
            Overrides = overrides.ToDictionary(kv => kv.Key, kv => kv.Value),
            Kind = string.IsNullOrEmpty(kind) ? ChatGroupKinds.Room : kind,
        };

    public CastMember? Member(Guid id) => Members.FirstOrDefault(c => c.Id == id);

    /// <summary>Effective Driver of a present member; null when not in the Room.</summary>
    public DriverKind? EffectiveOf(Guid id) => Member(id)?.EffectiveIn(Overrides);

    /// <summary>Present members as consumers see them — Effective Driver only.</summary>
    public List<ParticipantView> Views()
        => Members.Select(c => c.ToView(c.EffectiveIn(Overrides))).ToList();

    /// <summary>The Response pipeline's self, or null unless present and LLM-Effective.</summary>
    public SelfView? Speaker(Guid id)
        => Member(id) is { } c && c.EffectiveIn(Overrides) == DriverKind.LLM
            ? c.ToSelfView(DriverKind.LLM)
            : null;
}
