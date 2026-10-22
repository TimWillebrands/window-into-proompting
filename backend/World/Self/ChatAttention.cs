using PartyTown.Model;

namespace PartyTown.World.Self;

/// <summary>How a chat sits in someone's mind at a glance (DM or Room), before they open it.</summary>
/// <param name="InExchange">You're mid back-and-forth: the live lines are in mind.</param>
/// <param name="Unread">Lines from others since your last word.</param>
/// <param name="Held">What's in mind without opening the chat: the live exchange, or what you just read back.</param>
/// <param name="MoreAbove">There's history above what you hold; scrolling back would show it.</param>
public sealed record ChatGlance(bool InExchange, int Unread, ChatMessage? Latest, IReadOnlyList<ChatMessage> Held, bool MoreAbove);

/// <summary>What of a chat a person holds in mind: the live back-and-forth, plus the few lines on screen above it.</summary>
public static class ChatAttention
{
    /// <summary>A pause this long ends a back-and-forth; what came before is scrolled-up history.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(20);

    /// <summary>What you read stays in mind this long.</summary>
    public static readonly TimeSpan InMind = TimeSpan.FromMinutes(15);

    /// <summary>Lines visible above the current exchange when you open the chat.</summary>
    public const int OnScreen = 4;

    /// <summary>Even a long exchange: only this many recent lines are held.</summary>
    public const int MaxHeld = 40;

    /// <summary>One scroll back reads this many lines above what's on screen.</summary>
    public const int ReadBack = 30;

    /// <summary>The held tail of <paramref name="history"/> (oldest first) and how many earlier lines fell out of view; a fresh read-back reaches further.</summary>
    public static (List<ChatMessage> Held, int Hidden) Window(IReadOnlyList<ChatMessage> history, int? readFrom = null)
    {
        var from = Math.Max(Math.Max(0, SessionStart(history) - OnScreen), history.Count - MaxHeld);
        if (readFrom is { } r)
        {
            var read = IndexOfFirst(history, r);
            from = Math.Max(Math.Min(from, read), history.Count - MaxHeld - ReadBack);
        }
        return (history.Skip(from).ToList(), from);
    }

    /// <summary>Index of the first line of the trailing exchange; unsent stubs (no SendAt) count as part of it.</summary>
    public static int SessionStart(IReadOnlyList<ChatMessage> history)
    {
        var i = history.Count - 1;
        while (i > 0)
        {
            var later = history[i].SendAt;
            var earlier = history[i - 1].SendAt;
            if (later is { } l && earlier is { } e && l - e >= (long)SessionGap.TotalMilliseconds) break;
            i--;
        }
        return Math.Max(i, 0);
    }

    /// <summary>The read-back still in mind for a chat, if you scrolled it recently.</summary>
    public static int? ReadFrom(AttentionLook? look, DateTimeOffset now)
        => look is { FromMessageId: { } from } && now.ToUnixTimeMilliseconds() - look.At < (long)InMind.TotalMilliseconds ? from : null;

    /// <summary>Where a read-back from here would start: <see cref="ReadBack"/> lines above the oldest one held.</summary>
    public static int? ScrollTarget(IReadOnlyList<ChatMessage> history, int? readFrom)
    {
        var (_, hidden) = Window(history, readFrom);
        return hidden == 0 ? null : history[Math.Max(0, hidden - ReadBack)].MessageId;
    }

    /// <summary>A chat at a glance from <paramref name="self"/>'s side; <paramref name="recent"/> is oldest first.</summary>
    public static ChatGlance Glance(IReadOnlyList<ChatMessage> recent, Guid self, DateTimeOffset now, int? readFrom = null)
    {
        var latest = recent.Count == 0 ? null : recent[^1];
        if (latest is null) return new ChatGlance(false, 0, null, Array.Empty<ChatMessage>(), false);

        var start = SessionStart(recent);
        var lastOwn = LastIndexOf(recent, self);
        var live = now - DateTimeOffset.FromUnixTimeMilliseconds(latest.SendAt ?? now.ToUnixTimeMilliseconds()) < SessionGap;
        var inExchange = live && lastOwn >= start;
        var unread = recent.Count - 1 - lastOwn;

        IReadOnlyList<ChatMessage> held = Array.Empty<ChatMessage>();
        var hidden = recent.Count;
        if (inExchange || readFrom is not null)
            (held, hidden) = Window(recent, readFrom);
        return new ChatGlance(inExchange, unread, latest, held, hidden > 0);
    }

    private static int LastIndexOf(IReadOnlyList<ChatMessage> messages, Guid sender)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
            if (messages[i].SenderId == sender) return i;
        return -1;
    }

    private static int IndexOfFirst(IReadOnlyList<ChatMessage> history, int messageId)
    {
        for (var i = 0; i < history.Count; i++)
            if (history[i].MessageId >= messageId) return i;
        return history.Count;
    }
}
