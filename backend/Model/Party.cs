using System.ComponentModel.DataAnnotations;

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

    [Id(3)]
    public string? Scenario { get; set; }
}

public sealed record class CreateChatGroupRequest
{
    public string Name { get; set; } = string.Empty;

    [MaxLength(ChatGroupLimits.MaxScenarioLength)]
    public string? Scenario { get; set; }
}

public sealed record class UpdateChatGroupScenarioRequest
{
    [MaxLength(ChatGroupLimits.MaxScenarioLength)]
    public string? Scenario { get; set; }
}

public static class ChatGroupLimits
{
    public const int MaxScenarioLength = 2000;
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

/// <summary>
/// Replace the Room's membership (Personas present in this Room). Driver lives on the
/// Participant (Party scope) or in the Room's Driver override map — not here.
/// </summary>
public sealed record class UpdateChatGroupParticipantIdsRequest
{
    public List<Guid> ParticipantIds { get; set; } = [];
}

/// <summary>
/// Replace the Room's Driver override map wholesale. Each entry says
/// "in this Room, Persona X is driven by Y". Empty list clears all overrides.
/// </summary>
public sealed record class UpdateChatGroupDriverOverridesRequest
{
    public List<DriverOverrideEntry> Overrides { get; set; } = [];
}

public sealed record class DriverOverrideEntry
{
    public Guid PersonaId { get; set; }
    public DriverKind Kind { get; set; }
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
