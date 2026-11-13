namespace PartyTown.World.Self;

/// <summary>
/// Pure rules for a Persona's own Happenings (ADR 0055): something happens in their life away from the screen (family, home,
/// work, body), theirs alone, so what's new doesn't only come from the street.
/// </summary>
public static class OwnHappenings
{
    public const string Action = "lived";
    /// <summary>One every this many minutes, to someone awake; off when ambient Happenings are.</summary>
    public const int EveryMinutes = 40;
    /// <summary>Nobody gets two within this.</summary>
    public static readonly TimeSpan Rest = TimeSpan.FromHours(6);
    /// <summary>How long it's in their Right now.</summary>
    public static readonly TimeSpan Lingers = TimeSpan.FromHours(12);
    /// <summary>How long the next Day still hears of it.</summary>
    public static readonly TimeSpan Carried = TimeSpan.FromHours(36);
    public const int KindMemory = 3;

    /// <summary>What can happen to someone: each has a stake and leaves them a move.</summary>
    public static readonly string[] Kinds =
    {
        "someone from their own life (family, an old friend, a neighbour) gets in touch with news that changes something for them",
        "a letter, bill, parcel or message arrives for them that they'll have to decide about",
        "something at their home breaks, leaks or runs out, and needs sorting",
        "their work takes a turn: a client, colleague or boss wants something of them, or something they made lands well or badly",
        "their body: they're coming down with something, hurt something small, or can't shake a bad night",
        "an invitation, offer or chance comes their way that won't wait long",
        "one of the things going on in their life takes a real step: it moves on, gets stuck for good, or is done",
        "something small and good comes their way out of the blue",
    };

    /// <summary>Someone awake whose last one is past <see cref="Rest"/>, never-had first; null when nobody's due.</summary>
    public static Guid? Pick(IReadOnlyList<(Guid Id, long LastAt)> awake, long now, Random random)
    {
        var due = awake.Where(a => now - a.LastAt >= Rest.TotalMilliseconds).ToList();
        if (due.Count == 0) return null;
        var never = due.Where(a => a.LastAt == 0).ToList();
        var from = never.Count > 0 ? never : due;
        return from[random.Next(from.Count)].Id;
    }

    public static int PickKind(IReadOnlyCollection<int> lately, Random random)
    {
        var fresh = Enumerable.Range(0, Kinds.Length).Where(i => !lately.Contains(i)).ToList();
        return fresh.Count == 0 ? random.Next(Kinds.Length) : fresh[random.Next(fresh.Count)];
    }

    public static string Brief(string name, string kind) => $$"""
You are the narrator of {{name}}'s life away from the screen: their family, home, work, body and the people in their life outside the people they chat with. Let ONE thing happen to them now.
This time: {{kind}}.
It has to matter to them: something to deal with, decide, take up or do without. Small enough for an ordinary week, big enough that they'd mention it to a friend. Make it new, not more of what's already on their mind, unless this time is about one of those things.
Keep to who they are: their people, trade, home and money as given. People from their life the sheet implies (a sister, a client, a landlord) may appear.
Stop at the thing itself: don't say what they do, think or feel about it. Plain words, nothing poetic.
Output ONLY JSON: {"happening": "<one or two sentences to them, second person, present tense: 'Your sister calls: …'>", "summary": "<the same in under 15 words, a past-tense verb phrase about them without their name: 'got a call from their sister: …'>"}
""";

    /// <summary>The Narrator's material: who they are and what their day already holds.</summary>
    public static string Material(string name, string sheet, SelfState day, DateTimeOffset local)
    {
        var minute = local.Hour * 60 + local.Minute;
        var lines = new List<string> { $"Who {name} is:\n{sheet.Trim()}", string.Empty, $"It is {local:dddd HH:mm}: {HappeningRules.DayPart(local)}." };
        var past = day.Beats.Where(b => b.Minute <= minute).Select(b => b.Text).ToList();
        if (past.Count > 0) lines.Add($"Their day so far: {string.Join("; ", past)}");
        if (!string.IsNullOrWhiteSpace(day.Preoccupation)) lines.Add($"Nagging at them today: {day.Preoccupation}");
        if (day.Threads.Count > 0) lines.Add($"Going on in their life lately: {string.Join("; ", day.Threads)}");
        if (!string.IsNullOrWhiteSpace(day.Own)) lines.Add($"Last thing that happened to them (not this again): {day.Own}");
        return string.Join("\n", lines);
    }

    /// <summary>Trimmed text; null when there's nothing usable.</summary>
    public static string? Clean(string? raw, int max)
    {
        var text = raw?.Trim().Trim('"').Trim();
        return string.IsNullOrWhiteSpace(text) || text.Length < 8 ? null : WorldStream.Clip(text, max);
    }

    /// <summary>Their Right now line while it lingers; null otherwise.</summary>
    public static string? Line(SelfState day, DateTimeOffset now)
        => string.IsNullOrWhiteSpace(day.Own) || now.ToUnixTimeMilliseconds() - day.OwnAt >= Lingers.TotalMilliseconds
            ? null
            : $"Happened in your own life {WorldStream.Ago(day.OwnAt, now)}: {day.Own}";

    /// <summary>For the next Day call, while it's recent enough to carry.</summary>
    public static string Carry(SelfState before, DateTimeOffset now)
        => string.IsNullOrWhiteSpace(before.Own) || now.ToUnixTimeMilliseconds() - before.OwnAt >= Carried.TotalMilliseconds
            ? string.Empty
            : $"\nHappened to you lately: {before.Own}";
}
