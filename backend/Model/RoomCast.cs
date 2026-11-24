namespace PartyTown.Model;

[GenerateSerializer, Alias(nameof(RoomCast))]
public sealed record class RoomCast
{
    [Id(0)] public List<CastMember> Members { get; init; } = new();

    [Id(2)] public string Kind { get; init; } = ChatGroupKinds.Group;

    public bool IsDirect => Kind == ChatGroupKinds.Direct;

    public static RoomCast Create(
        IEnumerable<CastMember> partyCast,
        IReadOnlySet<Guid> memberIds,
        string? kind)
        => new()
        {
            Members = partyCast.Where(c => memberIds.Contains(c.Id)).ToList(),
            Kind = string.IsNullOrEmpty(kind) ? ChatGroupKinds.Group : kind,
        };

    public CastMember? Member(Guid id) => Members.FirstOrDefault(c => c.Id == id);

    public DriverKind? EffectiveOf(Guid id) => Member(id)?.DefaultDriver;

    public List<ParticipantView> Views()
        => Members.Select(c => c.ToView(c.DefaultDriver)).ToList();

    public SelfView? Speaker(Guid id)
        => Member(id) is { DefaultDriver: DriverKind.LLM } c
            ? c.ToSelfView(DriverKind.LLM)
            : null;
}
