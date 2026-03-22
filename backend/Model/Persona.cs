namespace PartyTown.Model;

[GenerateSerializer, Alias(nameof(PersonaMetadata))]
public record class PersonaMetadata : IPartyActor
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    public bool IsUser => false;

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

    public Persona()
    {
    }

    public Persona(Guid id, string name, string systemPrompt, string? bio)
        : base(id, name)
    {
        SystemPrompt = systemPrompt;
        Bio = bio;
    }
}

[GenerateSerializer, Alias(nameof(User))]
public sealed record class User : IPartyActor
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public string? Bio { get; set; }

    public bool IsUser => true;

    public User()
    {
    }

    public User(Guid id, string name, string? bio)
    {
        Id = id;
        Name = name;
        Bio = bio;
    }
}

interface IPartyActor
{
    Guid Id { get; }
    string Name { get; }
    bool IsUser { get; }
}
