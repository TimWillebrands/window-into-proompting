namespace PartyTown.Model;

// DriverKind is defined in Party.cs alongside PartyParticipant — its primary storage home.

/// <summary>
/// Internal materialised join of a Participant link with its Persona library entry,
/// built by PartyGrain and projected into views before reaching consumers. Carries the
/// Default Driver only; the Effective Driver is resolved by <see cref="RoomCast"/>.
/// </summary>
[GenerateSerializer, Alias(nameof(CastMember))]
public sealed record class CastMember
{
    [Id(0)] public Guid Id { get; init; }
    [Id(1)] public string Name { get; init; } = string.Empty;
    [Id(2)] public DriverKind DefaultDriver { get; init; }
    [Id(3)] public string? Bio { get; init; }
    [Id(4)] public string? SystemPrompt { get; init; }
    [Id(5)] public double Chattiness { get; init; } = 0.5;
    [Id(6)] public double Impulsivity { get; init; } = 0.5;

    /// <summary>
    /// Pure factory. No I/O. System links and library misses are sparse; any other link
    /// carries the full join, since a Room override can make a User default LLM-Effective.
    /// Name always comes from the link — the Participant owns the per-Party display name.
    /// </summary>
    public static CastMember Create(PartyParticipant link, Persona? persona)
    {
        // System (Narrator) never auto-speaks in any Room (ADR 0012), so it never needs the join.
        if (link.Driver == DriverKind.System || persona is null)
            return new CastMember
            {
                Id = link.Id,
                Name = link.Name,
                DefaultDriver = link.Driver
            };

        return new CastMember
        {
            Id = link.Id,
            Name = link.Name,
            DefaultDriver = link.Driver,
            Bio = persona.Bio,
            SystemPrompt = persona.SystemPrompt,
            Chattiness = persona.Chattiness,
            Impulsivity = persona.Impulsivity
        };
    }

    public ParticipantView ToView(DriverKind effective)
        => new(Id, Name, effective);

    public SelfView ToSelfView(DriverKind effective)
        => new(Id, Name, effective, Bio, SystemPrompt, Chattiness, Impulsivity);

    /// <summary>
    /// Effective Driver in a Room: the override if one exists for this Persona,
    /// otherwise the Participant's Default Driver. Single home for the resolution
    /// rule so consumers of <see cref="ToView"/>/<see cref="ToSelfView"/> never see
    /// the default/override distinction.
    /// </summary>
    public DriverKind EffectiveIn(IReadOnlyDictionary<Guid, DriverKind> overrides)
        => overrides.TryGetValue(Id, out var kind) ? kind : DefaultDriver;
}

/// <summary>
/// Public projection of a Participant for any consumer that only needs identity +
/// who is driving. <see cref="Driver"/> is the already-resolved Effective Driver —
/// consumers cannot see the default/override distinction.
/// </summary>
public readonly record struct ParticipantView(Guid Id, string Name, DriverKind Driver);

/// <summary>
/// Public projection of the acting Persona for the Response pipeline's Speaking
/// phase. Carries the full persona-library payload (bio, system prompt, dials) so
/// the speaker has rich self-data, without exposing the same to other Participants'
/// views. <see cref="Driver"/> is the already-resolved Effective Driver.
/// </summary>
public readonly record struct SelfView(
    Guid Id,
    string Name,
    DriverKind Driver,
    string? Bio,
    string? SystemPrompt,
    double Chattiness,
    double Impulsivity);
