namespace PartyTown.World.Self;

public static class Hooks
{
    private const int StoryLength = 90;

    public static string? Pick(IReadOnlyList<string> openers, Random? random = null)
        => openers.Count == 0 ? null : openers[(random ?? Random.Shared).Next(openers.Count)];

    public static string Like(string story) => WorldStream.Clip(story, StoryLength);

    public static List<string> Told(string label, IEnumerable<string> openers)
        => openers.Where(o => !string.IsNullOrWhiteSpace(o) && label.Contains(Like(o), StringComparison.Ordinal)).ToList();

    public static string Post(string? story)
        => story is null ? "Write a new post on Partyboard" : $"Post on Partyboard about something from your own day (like: {Like(story)})";

    public static string Message(string who, string? story)
        => story is null ? $"Send {who} a private message" : $"Message {who} about something from your own day (like: {Like(story)})";

    public static string PickUp(string who, string theySaid) => $"Pick up what {who} said, right here (\"{WorldStream.Clip(theySaid, 80)}\")";

    public static string Tell(string who, string story) => $"Tell {who}, right here, about something from your own day (like: {Like(story)})";

    public static string Saw(string who, string what) => $"Say something to {who} about what just happened here ({Like(what)})";

    public static string PostAbout(string where, string what) => $"Post on Partyboard about what just happened {where} (you saw it: {Like(what)})";

    public static string Ask(string who, string? theyreDoing)
        => theyreDoing is null ? $"Talk to {who}, right here" : $"Ask {who} what they're up to, right here ({theyreDoing})";

    public static string AllRight(string who, string bearing) => $"Ask {who} if they're all right, right here ({bearing})";

    public static string Diary(string? onYourMind)
        => onYourMind is null ? "Write in your diary and take some time to reflect" : $"Write in your diary about what's on your mind (like: {Like(onYourMind)})";

    public static string Rest(string? beat)
        => beat is null ? "Do nothing for now" : $"Do nothing in particular, just carry on with your day ({Like(beat)})";

    public const string RestOnShift = "Do nothing in particular for a moment; you're still on shift";

    public const string ShiftWins = "You're on shift right now: that wins over whatever your day had planned for this hour.";

    public const string BreakToEat = "A break to eat is part of a shift: you go, you eat, you come back.";
}
