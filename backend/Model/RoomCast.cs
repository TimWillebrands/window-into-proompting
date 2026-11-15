namespace PartyTown.Model;

[GenerateSerializer, Alias(nameof(RoomCast))]
public sealed record class RoomCast
{
    [Id(0)] public List<CastMember> Members { get; init; } = new();

    [Id(1)] public Dictionary<Guid, DriverKind> Overrides { get; init; } = new();

    [Id(2)] public string Kind { get; init; } = ChatGroupKinds.Room;

    public bool IsDirect => Kind == ChatGroupKinds.Direct;

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

    public DriverKind? EffectiveOf(Guid id) => Member(id)?.EffectiveIn(Overrides);

    public List<ParticipantView> Views()
        => Members.Select(c => c.ToView(c.EffectiveIn(Overrides))).ToList();

    public SelfView? Speaker(Guid id)
        => Member(id) is { } c && c.EffectiveIn(Overrides) == DriverKind.LLM
            ? c.ToSelfView(DriverKind.LLM)
            : null;
}
