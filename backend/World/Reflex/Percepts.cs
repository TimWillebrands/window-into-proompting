namespace PartyTown.World.Reflex;

/// <summary>The channel something reached a Persona through; every modality lands in one of these.</summary>
[GenerateSerializer]
public enum PerceptKind
{
    /// <summary>A private message.</summary>
    Direct = 0,
    /// <summary>A line typed in a Room.</summary>
    Line = 1,
    /// <summary>Said out loud within Earshot.</summary>
    Speech = 2,
    /// <summary>A Body arriving where you are.</summary>
    Arrival = 3,
    /// <summary>Your name caught from talk you can't make out.</summary>
    Overheard = 4,
    /// <summary>Someone at your door.</summary>
    Knock = 5,
}

/// <summary>One thing that reached a Persona (ADR 0031), whatever the modality.</summary>
/// <param name="Named">Their name is in it: the one signal that gets through nearly any load.</param>
/// <param name="Near">Right next to them, rather than across the room or next door.</param>
/// <param name="Deadline">Past this the caller has stopped waiting and replied its own way: a verdict then must not act.</param>
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
    [property: Id(12)] long? Deadline = null);

/// <summary>What the caller does next: run its normal reply, or leave it to Reflex.</summary>
/// <param name="Proceed">Reply through the normal path; false means Reflex dealt with it.</param>
/// <param name="Deliberate">System One was torn: System Two decides, no auto-respond.</param>
/// <param name="LagMs">How long reading takes before a reply starts.</param>
/// <param name="Note">Why, for the thought log.</param>
[GenerateSerializer, Alias("PartyTown.World.Reflex.ReflexVerdict")]
public sealed record ReflexVerdict(
    [property: Id(0)] bool Proceed,
    [property: Id(1)] bool Deliberate,
    [property: Id(2)] long LagMs,
    [property: Id(3)] string Note)
{
    /// <summary>Starts every note that means "seen, and they'll get back to you"; Messenger reads it.</summary>
    public const string Holding = "holding:";

    /// <summary>Reflex has no view: the normal path runs untouched.</summary>
    public static readonly ReflexVerdict Through = new(true, false, 0, string.Empty);

    public static ReflexVerdict Reply(long lagMs, bool deliberate, string note) => new(true, deliberate, lagMs, note);

    public static ReflexVerdict Taken(string note) => new(false, false, 0, note);
}

/// <summary>What a Persona can do about a Percept; each is one option put to System One.</summary>
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

/// <summary>Something a Persona meant to get back to (prospective memory, ADR 0031).</summary>
[GenerateSerializer, Alias("PartyTown.World.Reflex.Intention")]
public sealed class Intention
{
    [Id(0)] public Guid RoomId { get; set; }
    [Id(1)] public Guid FromId { get; set; }
    [Id(2)] public string FromName { get; set; } = string.Empty;
    /// <summary>What they said, as remembered.</summary>
    [Id(3)] public string Gist { get; set; } = string.Empty;
    /// <summary>What they were doing when they put it off.</summary>
    [Id(4)] public string Doing { get; set; } = string.Empty;
    [Id(5)] public long HeldAt { get; set; }
    /// <summary>When it comes back to mind; a busy moment pushes it to the next breakpoint.</summary>
    [Id(6)] public long DueAt { get; set; }
    /// <summary>A human is waiting: it happens by then, busy or not.</summary>
    [Id(7)] public long? OwedBy { get; set; }
    /// <summary>Nobody's owed it: past this it just slips their mind.</summary>
    [Id(8)] public long ForgetAt { get; set; }
    /// <summary>A quick line is on its way: that line doesn't settle this.</summary>
    [Id(9)] public bool Acking { get; set; }
    /// <summary>Attempts at getting back that failed before anything was said.</summary>
    [Id(10)] public int Tries { get; set; }
}

[GenerateSerializer, Alias("PartyTown.World.Reflex.ReflexState")]
public sealed class ReflexState
{
    [Id(0)] public List<Intention> Intentions { get; set; } = new();
}
