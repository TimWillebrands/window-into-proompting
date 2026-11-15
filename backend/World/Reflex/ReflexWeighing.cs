using PartyTown.Services.SystemOne;

namespace PartyTown.World.Reflex;

public sealed record Sense(
    string Name, string? Bio, string? Doing, string? Mood, double Load, double Battery, Bond Bond, IReadOnlyList<string> Between, string? Standing = null);

public static class ReflexWeighing
{
    public const string Key = "act";

    public static string First(string name) => name.Split(' ')[0];

    public static IReadOnlyDictionary<string, string?> Options(Percept p, string name)
    {
        var from = First(p.FromName);
        return p.Kind switch
        {
            PerceptKind.Direct => Deferrable(from, name),
            PerceptKind.Line when p.Named => Deferrable(from, name),
            PerceptKind.Line => new Dictionary<string, string?>
            {
                [ReflexOptions.Speak] = $"{name} has something to add and joins in",
                [ReflexOptions.Listen] = $"{name} reads along and stays out of it",
            },
            PerceptKind.Speech => new Dictionary<string, string?>
            {
                [ReflexOptions.Speak] = $"{name} answers or joins in",
                [ReflexOptions.Listen] = $"{name} listens and says nothing",
                [ReflexOptions.Move] = $"{name} moves away from {from}",
            },
            PerceptKind.Arrival when !p.Near => new Dictionary<string, string?>
            {
                [ReflexOptions.GoOver] = $"{name} goes over to say hi to {from}",
                [ReflexOptions.Nod] = $"{name} gives {from} a wave from where they are",
                [ReflexOptions.CarryOn] = $"{name} doesn't make anything of it",
            },
            PerceptKind.Arrival => new Dictionary<string, string?>
            {
                [ReflexOptions.Greet] = $"{name} says hello and starts talking to {from}",
                [ReflexOptions.Nod] = $"{name} gives {from} a nod or a wave and goes on with what they were doing",
                [ReflexOptions.CarryOn] = $"{name} doesn't look up: polite not to make anything of it",
                [ReflexOptions.Move] = $"{name} would rather not be near {from} and moves away",
            },
            PerceptKind.Overheard => new Dictionary<string, string?>
            {
                [ReflexOptions.GoOver] = $"{name} goes over to find out what {from} is saying",
                [ReflexOptions.CallOut] = $"{name} calls out to {from} from where they are",
                [ReflexOptions.CarryOn] = $"{name} lets it go",
            },
            PerceptKind.Knock => new Dictionary<string, string?>
            {
                [ReflexOptions.Open] = $"{name} goes and lets {from} in",
                [ReflexOptions.CarryOn] = $"{name} doesn't answer the door for now",
            },
            _ => new Dictionary<string, string?> { [ReflexOptions.CarryOn] = null },
        };
    }

    private static Dictionary<string, string?> Deferrable(string from, string name) => new()
    {
        [ReflexOptions.Now] = $"{name} answers {from} right away, dropping what they're doing",
        [ReflexOptions.Brief] = $"{name} sends one quick line that they saw it, and answers properly later",
        [ReflexOptions.Soon] = $"{name} finishes what they're doing first, then answers",
        [ReflexOptions.Leave] = $"{name} leaves it unanswered for now",
    };

    public static string Question(string name) => $"What does {name}, being who they are, most plausibly do about this right now?";

    public static string Event(Percept p, string name)
    {
        var who = First(p.FromName);
        return p.Kind switch
        {
            PerceptKind.Direct => $"{who} sends {name} a private message: \"{WorldStream.Clip(p.Text, 300)}\"",
            PerceptKind.Line => $"In {(string.IsNullOrWhiteSpace(p.RoomName) ? "a group chat" : $"#{p.RoomName}")}, {who} writes: \"{WorldStream.Clip(p.Text, 300)}\"",
            PerceptKind.Speech => $"{who} says out loud, close to {name}: \"{WorldStream.Clip(p.Text, 300)}\"",
            PerceptKind.Arrival => p.Near
                ? $"{who} just walked up and is now right next to {name}.{(p.FromAura is { Length: > 0 } aura ? $" Up close: {aura.TrimEnd('.')}." : string.Empty)}"
                : $"{who} just came in, on the other side of the place.",
            PerceptKind.Overheard => $"{name} catches their own name from {who}, too far off to make out the words. It sounds like {(string.IsNullOrWhiteSpace(p.Text) ? "chatting" : p.Text)}.",
            PerceptKind.Knock => $"{who} is knocking on {name}'s door.",
            _ => $"{who} did something nearby.",
        };
    }

    public static string State(Percept p, Sense s)
    {
        var lines = new List<string> { $"{s.Name}.{(string.IsNullOrWhiteSpace(s.Bio) ? "" : $" {WorldStream.Clip(s.Bio, 240)}")}" };
        lines.Add($"Right now {s.Name} is {(string.IsNullOrWhiteSpace(s.Doing) ? "not doing anything in particular" : s.Doing.TrimEnd('.'))} ({Occupied(s.Load)}).");
        if (!string.IsNullOrWhiteSpace(s.Mood)) lines.Add($"Mood: {s.Mood}.");
        if (s.Battery < 0.35) lines.Add($"{s.Name} is worn out from talking to people today.");
        lines.Add(Relation(p, s));
        if (s.Between.Count > 0) lines.Add($"On {s.Name}'s mind about them: {string.Join(" ", s.Between.Select(b => WorldStream.Clip(b, 140)))}");
        if (p.Context is { Count: > 0 } context) lines.Add($"Just before this:\n{string.Join("\n", context)}");
        lines.Add($"What just happened: {Event(p, s.Name)}");
        return string.Join("\n", lines);
    }

    private static string Occupied(double load) => load switch
    {
        < 0.25 => "free to be distracted",
        < 0.6 => "somewhat occupied",
        _ => "fully occupied",
    };

    private static string Relation(Percept p, Sense s)
    {
        var from = First(p.FromName);
        if (s.Bond.Exchanges <= 0) return s.Standing is { } only ? $"{s.Name} and {from}: {only}." : $"{s.Name} doesn't really know {from}.";
        var lately = Self.Ties.Lately(s.Bond.Warmth) is { } l ? $"; {l}" : string.Empty;
        var last = s.Bond.HoursApart is { } h ? $"; last talked {(h < 1 ? "within the hour" : h < 24 ? $"{(int)h} h ago" : $"{(int)(h / 24)} days ago")}" : string.Empty;
        var standing = s.Standing is { } line ? $"{line}; " : string.Empty;
        return $"{s.Name} and {from}: {standing}{Self.Ties.Closeness(s.Bond.Exchanges)}{last}{lately}.";
    }

    public static IReadOnlyDictionary<string, SystemOneQuestion> Ask(Percept p, string name)
        => new Dictionary<string, SystemOneQuestion> { [Key] = new ChoiceQuestion(Question(name), Options(p, name)) };
}

public static class ReflexVoice
{
    public static string Ack(string? doing)
        => $"Send one short line only: you saw this, you're in the middle of {(string.IsNullOrWhiteSpace(doing) ? "something" : doing)}, and you'll get back to them.";

    public static string Greet(string first) => $"{first} just came up to you. Say hello, the way you would to them, and leave it open.";

    public static string CallOver(string first) => $"You just heard {first} say your name across the room. Call over to them.";

    public static string GetBack(string first, string said, string doing)
        => $"Get back to {first}, a while after they wrote: {said}. You were {doing} then. Answer what they said; only say why you took a while if it comes up naturally.";
}
