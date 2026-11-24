using System.Text.Json.Serialization;

namespace PartyTown.Model;

[GenerateSerializer, Alias(nameof(DriverKind))]
[JsonConverter(typeof(JsonStringEnumConverter<DriverKind>))]
public enum DriverKind
{
    User = 0,

    LLM = 1,

    System = 2,
}

[GenerateSerializer, Alias(nameof(PartyParticipant))]
public sealed record class PartyParticipant
{
    [Id(0)]
    public Guid Id { get; set; } = Guid.Empty;

    [Id(1)]
    public string Name { get; set; } = string.Empty;

    [Id(3)]
    public DriverKind Driver { get; set; } = DriverKind.LLM;
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

    [Id(4)]
    public string Kind { get; set; } = ChatGroupKinds.Group;

    [Id(5)]
    public List<Guid>? MemberIds { get; set; }

    [Id(6)]
    public Guid? CreatedBy { get; set; }
}

public static class ChatGroupKinds
{
    public const string Group = "room";
    public const string Direct = "dm";
    public const string Place = "place";
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
