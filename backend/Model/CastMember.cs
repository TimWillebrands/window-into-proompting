namespace PartyTown.Model;

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
    [Id(7)] public string? Aura { get; init; }

    public static CastMember Create(PartyParticipant link, Persona? persona)
    {
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
            Impulsivity = persona.Impulsivity,
            Aura = persona.Aura
        };
    }

    public ParticipantView ToView(DriverKind effective)
        => new(Id, Name, effective);

    public SelfView ToSelfView(DriverKind effective)
        => new(Id, Name, effective, Bio, SystemPrompt, Chattiness, Impulsivity);

    public DriverKind EffectiveIn(IReadOnlyDictionary<Guid, DriverKind> overrides)
        => overrides.TryGetValue(Id, out var kind) ? kind : DefaultDriver;
}

public readonly record struct ParticipantView(Guid Id, string Name, DriverKind Driver);

public readonly record struct SelfView(
    Guid Id,
    string Name,
    DriverKind Driver,
    string? Bio,
    string? SystemPrompt,
    double Chattiness,
    double Impulsivity);
