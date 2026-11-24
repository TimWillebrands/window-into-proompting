using System.Text.RegularExpressions;
using PartyTown.Model;

namespace PartyTown.World.Notifications;

public static class NotificationKinds
{
    public const string Dm = "dm";
    public const string Group = "group";
    public const string BoardReply = "board-reply";
    public const string BoardMention = "board-mention";
    public const string Respect = "respect";
    public const string Gathering = "gathering";
    public const string Gift = "gift";
}

public sealed record PlayerNotification(
    string Id,
    string Kind,
    long At,
    string Title,
    string Body,
    string App,
    Guid? TargetId = null,
    Guid? FromId = null,
    string? FromName = null,
    bool Unread = false);

public sealed record NotificationFeed(List<PlayerNotification> Items, long SeenAt, int Unread);

public sealed record GiftReceived(Guid FromId, string FromName, string Item, long At);

public sealed record DmWaiting(Guid ChatGroupId, Guid WithId, string WithName, IReadOnlyList<ChatMessage> Recent);

public sealed record GroupWaiting(Guid ChatGroupId, string Name, IReadOnlyList<ChatMessage> Recent);

public static class Inbox
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(3);
    public const int Max = 50;

    public static NotificationFeed Feed(IEnumerable<PlayerNotification> all, long seenAt, IReadOnlyCollection<string> read)
        => Feed(all, new NotificationsState { SeenAt = seenAt, Read = read.ToList() });

    public static NotificationFeed Feed(IEnumerable<PlayerNotification> all, NotificationsState state)
    {
        var (seenAt, read) = (state.SeenAt, state.Read);
        var items = all
            .Where(n => n.At > state.ClearedAt && !state.Dismissed.Contains(n.Id))
            .OrderByDescending(n => n.At)
            .Take(Max)
            .Select(n => n with { Unread = IsUnread(n, seenAt, read) })
            .ToList();
        return new NotificationFeed(items, seenAt, items.Count(n => n.Unread));
    }

    public static bool IsUnread(PlayerNotification n, long seenAt, IReadOnlyCollection<string> read)
        => n.At > seenAt && !read.Contains(n.Id);

    public static bool Mentions(string? text, string name)
        => FirstName(name) is { Length: >= 2 } first && !string.IsNullOrEmpty(text)
            && Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(first)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase);

    private static string FirstName(string name) => name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

    public static IEnumerable<PlayerNotification> FromBoard(IReadOnlyList<BoardThread> threads, Guid self, string selfName, long since)
    {
        foreach (var t in threads)
        {
            var lastOwn = t.Replies.FindLastIndex(r => r.AuthorId == self);
            var yours = t.AuthorId == self;
            var youIn = yours || lastOwn >= 0;
            var waiting = t.Replies.Skip(lastOwn + 1).Where(r => r.AuthorId != self && r.CreatedAt > since).ToList();
            var where = yours ? "your post" : $"{t.AuthorName}'s post";

            if (waiting.LastOrDefault(r => Mentions(r.Body, selfName)) is { } named)
                yield return new PlayerNotification($"board:{t.Id}:{named.Id}", NotificationKinds.BoardMention, named.CreatedAt,
                    $"{named.AuthorName} mentioned you on Partyboard", WorldStream.Clip(named.Body, 160), "board", t.Id, named.AuthorId, named.AuthorName);
            else if (youIn && waiting.Count > 0)
                yield return new PlayerNotification($"board:{t.Id}:{waiting[^1].Id}", NotificationKinds.BoardReply, waiting[^1].CreatedAt,
                    $"{Names(waiting.Select(r => r.AuthorName))} replied on {where}",
                    WorldStream.Clip(waiting[^1].Body, 160), "board", t.Id, waiting[^1].AuthorId, waiting[^1].AuthorName);
            else if (!youIn && t.CreatedAt > since && Mentions(t.Body, selfName))
                yield return new PlayerNotification($"board:{t.Id}:post", NotificationKinds.BoardMention, t.CreatedAt,
                    $"{t.AuthorName} mentioned you on Partyboard", WorldStream.Clip(t.Body, 160), "board", t.Id, t.AuthorId, t.AuthorName);

            var lines = (yours ? new[] { (Id: t.Id, Body: t.Body, Respect: t.Respect) } : Array.Empty<(Guid Id, string Body, List<BoardRespect> Respect)>())
                .Concat(t.Replies.Where(r => r.AuthorId == self).Select(r => (r.Id, r.Body, r.Respect)));
            foreach (var line in lines)
            foreach (var r in line.Respect.Where(r => r.GiverId != self && r.At > since))
                yield return new PlayerNotification($"respect:{line.Id}:{r.GiverId}", NotificationKinds.Respect, r.At,
                    $"{r.GiverName} gave you Respect!", WorldStream.Clip(line.Body, 160), "board", t.Id, r.GiverId, r.GiverName);
        }
    }

    public static PlayerNotification? FromDm(DmWaiting dm, Guid self, long since)
    {
        var lastOwn = dm.Recent.ToList().FindLastIndex(m => m.SenderId == self);
        var waiting = dm.Recent.Skip(lastOwn + 1)
            .Where(m => m.SenderId != self && m.Error is null && !string.IsNullOrWhiteSpace(m.Content) && (m.SendAt ?? 0) > since)
            .ToList();
        if (waiting.Count == 0) return null;
        var latest = waiting[^1];
        return new PlayerNotification($"dm:{dm.ChatGroupId}:{latest.MessageId}", NotificationKinds.Dm, latest.SendAt ?? 0,
            waiting.Count == 1 ? $"{dm.WithName} sent you a message" : $"{dm.WithName} sent you {waiting.Count} messages",
            WorldStream.Clip(latest.Content, 160), "messenger", dm.ChatGroupId, dm.WithId, dm.WithName);
    }

    public static PlayerNotification? FromGroup(GroupWaiting group, Guid self, long since, Func<Guid, string> nameOf)
    {
        var lastOwn = group.Recent.ToList().FindLastIndex(m => m.SenderId == self);
        var waiting = group.Recent.Skip(lastOwn + 1)
            .Where(m => m.SenderId != self && m.Error is null && !string.IsNullOrWhiteSpace(m.Content) && (m.SendAt ?? 0) > since)
            .ToList();
        if (waiting.Count == 0) return null;
        var latest = waiting[^1];
        return new PlayerNotification($"group:{group.ChatGroupId}:{latest.MessageId}", NotificationKinds.Group, latest.SendAt ?? 0,
            $"{Names(waiting.Select(m => nameOf(m.SenderId)))} wrote in {group.Name}",
            $"{nameOf(latest.SenderId)}: {WorldStream.Clip(latest.Content, 160)}", "messenger", group.ChatGroupId, latest.SenderId, nameOf(latest.SenderId));
    }

    public static IEnumerable<PlayerNotification> FromGatherings(IEnumerable<Gathering> gatherings, Guid self, long since)
        => gatherings
            .Where(g => g.Status == GatheringStatus.Started && g.AttendeeIds.Contains(self) && g.StartsAt > since)
            .Select(g => new PlayerNotification($"gathering:{g.Id}", NotificationKinds.Gathering, g.StartsAt,
                $"“{g.Title}” is starting", $"{Capitalise(g.Where)}. You said you'd be there.",
                "places", g.PlaceId ?? g.RoomId, g.HostId, g.HostName));

    public static IEnumerable<PlayerNotification> FromGifts(IEnumerable<GiftReceived> gifts, Guid self, long since)
        => gifts
            .Where(g => g.FromId != self && g.At > since)
            .Select(g => new PlayerNotification($"gift:{g.FromId}:{g.At}", NotificationKinds.Gift, g.At,
                $"{g.FromName} gave you a present", $"{g.Item}. It's in your Wardrobe.", "wardrobe", null, g.FromId, g.FromName));

    public static string Names(IEnumerable<string> names)
    {
        var distinct = names.Reverse().Distinct().ToList();
        return distinct.Count switch
        {
            0 => "Someone",
            1 => distinct[0],
            2 => $"{distinct[0]} and {distinct[1]}",
            _ => $"{distinct[0]} and {distinct.Count - 1} others",
        };
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
