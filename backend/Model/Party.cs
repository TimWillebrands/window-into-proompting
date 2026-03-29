namespace PartyTown.Model;

// Domain terminology:
//   Party      = root-of-universe (top-level container for participants, chat groups, and state)
//   Persona    = inhabitant-of-said-universe (AI character with name/bio/system prompt)
//   ChatGroup  = app where personas and user(s) talk within a universe (named conversation thread)

[GenerateSerializer, Alias(nameof(PartyParticipant))]
public sealed record class PartyParticipant
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public bool IsUser { get; set; }
}

[GenerateSerializer, Alias(nameof(PartyInfo))]
public record class PartyInfo
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public List<PartyParticipant> Participants { get; set; } = [];
}

[GenerateSerializer, Alias(nameof(ChatGroupInfo))]
public sealed record class ChatGroupInfo
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(2)]
    public long CreatedAt { get; set; }
}

public sealed record class CreateChatGroupRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed record class CreatePartyRequest
{
    public string PartyName { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
}

public sealed record class UpdatePartyParticipantsRequest
{
    public List<PartyParticipant> Participants { get; set; } = [];
}

public sealed record class PromptRequest
{
    public Guid ChatGroupId { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public Guid? SenderId { get; set; }
}

public sealed record class ProceedRequest
{
    public Guid ChatGroupId { get; set; }
    public Guid? SenderId { get; set; }
}

public sealed record class RepromptRequest
{
    public Guid ChatGroupId { get; set; }
    public Guid? SenderId { get; set; }
    public string? SenderName { get; set; }
}
