namespace PartyTown.World.Reflex;

[GenerateSerializer]
public enum PerceptKind
{
    Direct = 0,
    Line = 1,
    Speech = 2,
    Arrival = 3,
    Overheard = 4,
    Knock = 5,
}

[GenerateSerializer, Alias("PartyTown.World.Reflex.Percept")]
public sealed record Percept(
    [property: Id(0)] PerceptKind Kind,
    [property: Id(1)] Guid FromId,
    [property: Id(2)] string FromName,
    [property: Id(3)] bool FromHuman,
    [property: Id(4)] string Text = "",
    [property: Id(5)] Guid? RoomId = null,
    [property: Id(6)] bool Named = false,
    [property: Id(7)] bool Near = true,
    [property: Id(8)] Guid? PlaceId = null,
    [property: Id(9)] string? SpotId = null,
    [property: Id(10)] string? RoomName = null,
    [property: Id(11)] IReadOnlyList<string>? Context = null,
    [property: Id(12)] long? Deadline = null,
    [property: Id(13)] string? FromAura = null,
    [property: Id(14)] int Traded = 0,
    [property: Id(15)] IReadOnlyList<string>? Traders = null,
    [property: Id(16)] int Own = 0,
    [property: Id(17)] int Recent = 0,
    [property: Id(18)] string? Thermostat = null,
    [property: Id(19)] bool WindingDown = false);

[GenerateSerializer, Alias("PartyTown.World.Reflex.ReflexVerdict")]
public sealed record ReflexVerdict(
    [property: Id(0)] bool Proceed,
    [property: Id(1)] bool Deliberate,
    [property: Id(2)] long LagMs,
    [property: Id(3)] string Note)
{
    public const string Holding = "holding:";

    public static readonly ReflexVerdict Through = new(true, false, 0, string.Empty);

    public static ReflexVerdict Reply(long lagMs, bool deliberate, string note) => new(true, deliberate, lagMs, note);

    public static ReflexVerdict Taken(string note) => new(false, false, 0, note);
}

public static class ReflexOptions
{
    public const string Now = "now";
    public const string Brief = "brief";
    public const string Soon = "soon";
    public const string Leave = "leave";
    public const string Speak = "speak";
    public const string Listen = "listen";
    public const string Greet = "greet";
    public const string Nod = "nod";
    public const string CarryOn = "carry-on";
    public const string Move = "move";
    public const string GoOver = "go-over";
    public const string CallOut = "call-out";
    public const string Open = "open";
}

[GenerateSerializer, Alias("PartyTown.World.Reflex.Intention")]
public sealed class Intention
{
    [Id(0)] public Guid RoomId { get; set; }
    [Id(1)] public Guid FromId { get; set; }
    [Id(2)] public string FromName { get; set; } = string.Empty;
    [Id(3)] public string Gist { get; set; } = string.Empty;
    [Id(4)] public string Doing { get; set; } = string.Empty;
    [Id(5)] public long HeldAt { get; set; }
    [Id(6)] public long DueAt { get; set; }
    [Id(7)] public long? OwedBy { get; set; }
    [Id(8)] public long ForgetAt { get; set; }
    [Id(9)] public bool Acking { get; set; }
    [Id(10)] public int Tries { get; set; }
    [Id(11)] public string? Latest { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Reflex.ReflexState")]
public sealed class ReflexState
{
    [Id(0)] public List<Intention> Intentions { get; set; } = new();
}
