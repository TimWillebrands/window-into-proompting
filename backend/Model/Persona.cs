namespace PartyTown.Model;

[GenerateSerializer, Alias(nameof(PersonaMetadata))]
public record class PersonaMetadata
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    public PersonaMetadata()
    {
    }

    public PersonaMetadata(Guid id, string name)
    {
        Id = id;
        Name = name;
    }
}

[GenerateSerializer, Alias(nameof(Persona))]
public sealed record class Persona : PersonaMetadata
{
    [Id(2)]
    public string SystemPrompt { get; set; } = string.Empty;

    [Id(3)]
    public string? Bio { get; set; }

    [Id(4)]
    public double Chattiness { get; set; } = 0.5;

    [Id(5)]
    public double Impulsivity { get; set; } = 0.5;

    [Id(6)]
    public string? Aura { get; set; }

    public const int MaxAura = 64;

    public static string? CleanAura(string? aura)
    {
        var line = string.Join(' ', (aura ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length == 0 ? null : line[..Math.Min(line.Length, MaxAura)];
    }

    public Persona()
    {
    }

    public Persona(Guid id, string name, string systemPrompt, string? bio, double chattiness = 0.5, double impulsivity = 0.5)
        : base(id, name)
    {
        SystemPrompt = systemPrompt;
        Bio = bio;
        Chattiness = chattiness;
        Impulsivity = impulsivity;
    }
}
