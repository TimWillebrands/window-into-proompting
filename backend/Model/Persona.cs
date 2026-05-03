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

    /// <summary>
    /// 0..1 dial mixed into the chaos urge component in PersonaDecisionService.
    /// 0 = brooding, only speaks when pulled; 1 = always wants to chime in.
    /// Default 0.5 keeps existing personas behaviorally neutral on first run.
    /// </summary>
    [Id(4)]
    public double Chattiness { get; set; } = 0.5;

    /// <summary>
    /// 0..1 dial controlling commitment-to-in-flight-utterance vs. responsiveness-to-new-info.
    /// 0 = deliberative (easily interrupted by new info; cancels and re-decides; tends to repair
    /// when ignored info turns out to matter). 1 = impulsive (commits hard once generation is
    /// underway; barrels through; rarely repairs). Drives the stop-signal race in PersonaGrain
    /// when a new message arrives during in-flight generation. Default 0.5.
    /// </summary>
    [Id(5)]
    public double Impulsivity { get; set; } = 0.5;

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
