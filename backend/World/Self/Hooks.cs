namespace PartyTown.World.Self;

/// <summary>The persona's own material on every channel's options: System One picks from labels, so none wins on vividness alone.</summary>
public static class Hooks
{
    private const int StoryLength = 90;

    /// <summary>One of today's openers, or null when they've all been told.</summary>
    public static string? Pick(IReadOnlyList<string> openers, Random? random = null)
        => openers.Count == 0 ? null : openers[(random ?? Random.Shared).Next(openers.Count)];

    public static string Like(string story) => WorldStream.Clip(story, StoryLength);

    /// <summary>Openers an acted-on label carried: once told, on any channel, they're told.</summary>
    public static List<string> Told(string label, IEnumerable<string> openers)
        => openers.Where(o => !string.IsNullOrWhiteSpace(o) && label.Contains(Like(o), StringComparison.Ordinal)).ToList();

    public static string Post(string? story)
        => story is null ? "Write a new post on Partyboard" : $"Post on Partyboard about something from your own day (like: {Like(story)})";

    public static string Message(string who, string? story)
        => story is null ? $"Send {who} a private message" : $"Message {who} about something from your own day (like: {Like(story)})";

    public static string PickUp(string who, string theySaid) => $"Pick up what {who} said, right here (\"{WorldStream.Clip(theySaid, 80)}\")";

    public static string Tell(string who, string story) => $"Tell {who}, right here, about something from your own day (like: {Like(story)})";

    /// <summary>Something that just happened where they both are: shared news, not one person's story.</summary>
    public static string Saw(string who, string what) => $"Say something to {who} about what just happened here ({Like(what)})";

    public static string PostAbout(string where, string what) => $"Post on Partyboard about what just happened {where} (you saw it: {Like(what)})";

    public static string Ask(string who, string? theyreDoing)
        => theyreDoing is null ? $"Talk to {who}, right here" : $"Ask {who} what they're up to, right here ({theyreDoing})";

    public static string Diary(string? onYourMind)
        => onYourMind is null ? "Write in your diary and take some time to reflect" : $"Write in your diary about what's on your mind (like: {Like(onYourMind)})";

    /// <summary>Doing nothing in particular is doing what the day has you doing.</summary>
    public static string Rest(string? beat)
        => beat is null ? "Do nothing for now" : $"Do nothing in particular, just carry on with your day ({Like(beat)})";
}
