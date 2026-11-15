using PartyTown.Model;

namespace PartyTown.World.Self;

public sealed record ChatGlance(bool InExchange, int Unread, ChatMessage? Latest, IReadOnlyList<ChatMessage> Held, bool MoreAbove);

public static class ChatAttention
{
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(20);

    public static readonly TimeSpan InMind = TimeSpan.FromMinutes(15);

    public const int OnScreen = 4;

    public const int MaxHeld = 40;

    public const int ReadBack = 30;

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

    public static int? ReadFrom(AttentionLook? look, DateTimeOffset now)
        => look is { FromMessageId: { } from } && now.ToUnixTimeMilliseconds() - look.At < (long)InMind.TotalMilliseconds ? from : null;

    public static int? ScrollTarget(IReadOnlyList<ChatMessage> history, int? readFrom)
    {
        var (_, hidden) = Window(history, readFrom);
        return hidden == 0 ? null : history[Math.Max(0, hidden - ReadBack)].MessageId;
    }

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
