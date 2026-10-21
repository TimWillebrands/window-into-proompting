using System.Text.Json;
using Orleans.Streams;
using PartyTown.Services.Streaming;

namespace PartyTown.World;

/// <summary>One thing a Persona (or the Player) did somewhere in the World — the activity log line.</summary>
[GenerateSerializer, Alias("PartyTown.World.WorldActivity")]
public sealed record class WorldActivity
{
    [Id(0)] public Guid Id { get; init; } = Guid.NewGuid();
    [Id(1)] public long Ts { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    [Id(2)] public Guid PersonaId { get; init; }
    [Id(3)] public string PersonaName { get; init; } = string.Empty;
    /// <summary>App id: rooms, messenger, board, diary, life (rest/errors).</summary>
    [Id(4)] public string App { get; init; } = string.Empty;
    /// <summary>Short verb: say, dm, post, reply, diary, rest, capture, error.</summary>
    [Id(5)] public string Action { get; init; } = string.Empty;
    [Id(6)] public string Summary { get; init; } = string.Empty;
    /// <summary>The Initiative thought that led here; null for non-Life activity.</summary>
    [Id(7)] public string? Thought { get; init; }
    /// <summary>Room/DM/thread the activity happened in, for click-through.</summary>
    [Id(8)] public Guid? TargetId { get; init; }
}

/// <summary>Clock + budget snapshot for the World control surface.</summary>
[GenerateSerializer, Alias("PartyTown.World.WorldStatus")]
public sealed record class WorldStatus
{
    [Id(0)] public Guid PartyId { get; init; }
    [Id(1)] public bool Running { get; init; }
    [Id(2)] public int TickSeconds { get; init; }
    [Id(3)] public int MaxActionsPerHour { get; init; }
    [Id(4)] public int ActionsLastHour { get; init; }
    [Id(5)] public Guid? PlayerId { get; init; }
    [Id(6)] public List<Guid> BusyPersonaIds { get; init; } = new();
    [Id(7)] public long? LastTickAt { get; init; }
    [Id(8)] public int AmbientMinutes { get; init; }
}

public static class WorldStream
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Push a World surface change onto the Party stream (→ realtime hub "world.{kind}").</summary>
    public static Task PublishWorldAsync(this IStreamProvider provider, Guid partyId, string kind, object payload)
        => provider
            .GetStream<PartyStreamEvent>(PartyStreamIds.PartyEventsNamespace, PartyStreamIds.PartyEventId(partyId))
            .OnNextAsync(new PartyStreamEvent
            {
                Type = "world",
                WorldKind = kind,
                WorldJson = JsonSerializer.Serialize(payload, Web),
            });

    public static string Ago(long unixMs, DateTimeOffset now)
    {
        var d = now - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        if (d.TotalSeconds < 60) return "just now";
        if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalHours < 24) return $"{(int)d.TotalHours} h ago";
        return $"{(int)d.TotalDays} d ago";
    }

    public static string Clip(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var t = s.ReplaceLineEndings(" ").Trim();
        return t.Length <= max ? t : t[..max].TrimEnd() + "…";
    }
}
