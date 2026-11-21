namespace PartyTown.World.Self;

public static class OwnHappenings
{
    public const string Action = "lived";
    public const int EveryMinutes = 40;
    public static readonly TimeSpan Rest = TimeSpan.FromHours(6);
    public static readonly TimeSpan Lingers = TimeSpan.FromHours(12);
    public static readonly TimeSpan Carried = TimeSpan.FromHours(36);
    public const int KindMemory = 3;
    public const int Body = 4;

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

    public static List<Guid> Due(IReadOnlyList<(Guid Id, long LastAt)> awake, long now)
        => awake.Where(a => now - a.LastAt >= Rest.TotalMilliseconds).Select(a => a.Id).ToList();

    public static Guid? Pick(IReadOnlyList<(Guid Id, long LastAt)> awake, long now, Random random, IReadOnlyCollection<Guid>? breaking = null)
    {
        var due = awake.Where(a => now - a.LastAt >= Rest.TotalMilliseconds).ToList();
        if (due.Count == 0) return null;
        var broken = breaking is null ? new List<(Guid Id, long LastAt)>() : due.Where(a => breaking.Contains(a.Id)).ToList();
        var never = due.Where(a => a.LastAt == 0).ToList();
        var from = broken.Count > 0 ? broken : never.Count > 0 ? never : due;
        return from[random.Next(from.Count)].Id;
    }

    public static int PickKind(IReadOnlyCollection<int> lately, Random random)
    {
        var fresh = Enumerable.Range(0, Kinds.Length).Where(i => !lately.Contains(i)).ToList();
        return fresh.Count == 0 ? random.Next(Kinds.Length) : fresh[random.Next(fresh.Count)];
    }

    public static int KindFor(bool breaking, IReadOnlyCollection<int> lately, Random random)
        => breaking ? Body : PickKind(lately, random);

    public static string Brief(string name, string kind) => $$"""
You are the narrator of {{name}}'s life away from the screen: their family, home, work, body and the people in their life outside the people they chat with. Let ONE thing happen to them now.
This time: {{kind}}.
It has to matter to them: something to deal with, decide, take up or do without. Small enough for an ordinary week, big enough that they'd mention it to a friend. Make it new, not more of what's already on their mind, unless this time is about one of those things.
Keep to who they are: their people, trade, home and money as given. People from their life the sheet implies (a sister, a client, a landlord) may appear.
Stop at the thing itself: don't say what they do, think or feel about it. Plain words, nothing poetic.
Output ONLY JSON: {"happening": "<one or two sentences to them, second person, present tense: 'Your sister calls: …'>", "summary": "<the same in under 15 words, a past-tense verb phrase about them without their name: 'got a call from their sister: …'>"}
""";

    public static string Material(string name, string sheet, SelfState day, DateTimeOffset local, string? body = null)
    {
        var minute = local.Hour * 60 + local.Minute;
        var lines = new List<string> { $"Who {name} is:\n{sheet.Trim()}", string.Empty, $"It is {local:dddd HH:mm}: {HappeningRules.DayPart(local)}." };
        var past = day.Beats.Where(b => b.Minute <= minute).Select(b => b.Text).ToList();
        if (past.Count > 0) lines.Add($"Their day so far: {string.Join("; ", past)}");
        if (!string.IsNullOrWhiteSpace(day.Preoccupation)) lines.Add($"Nagging at them today: {day.Preoccupation}");
        if (day.Threads.Count > 0) lines.Add($"Going on in their life lately: {string.Join("; ", day.Threads)}");
        if (!string.IsNullOrWhiteSpace(day.Own)) lines.Add($"Last thing that happened to them (not this again): {day.Own}");
        if (!string.IsNullOrWhiteSpace(body)) lines.Add($"How they've treated their body lately: {body}.");
        return string.Join("\n", lines);
    }

    public static string? Clean(string? raw, int max)
    {
        var text = raw?.Trim().Trim('"').Trim();
        return string.IsNullOrWhiteSpace(text) || text.Length < 8 ? null : WorldStream.Clip(text, max);
    }

    public static string? Line(SelfState day, DateTimeOffset now)
        => string.IsNullOrWhiteSpace(day.Own) || now.ToUnixTimeMilliseconds() - day.OwnAt >= Lingers.TotalMilliseconds
            ? null
            : $"Happened in your own life {WorldStream.Ago(day.OwnAt, now)}: {day.Own}";

    public static string Carry(SelfState before, DateTimeOffset now)
        => string.IsNullOrWhiteSpace(before.Own) || now.ToUnixTimeMilliseconds() - before.OwnAt >= Carried.TotalMilliseconds
            ? string.Empty
            : $"\nHappened to you lately: {before.Own}";
}
