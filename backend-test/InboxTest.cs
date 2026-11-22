using PartyTown.Model;
using PartyTown.World;
using PartyTown.World.Notifications;

namespace BackendTest;

public class InboxTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static readonly Guid Bo = Guid.NewGuid();

    private static string NameOf(Guid id) => id == Me ? "Denise Smit" : id == Ana ? "Ana" : "Bo";

    private static BoardReply Reply(Guid by, long at, string body, params BoardRespect[] respect) => new()
    {
        Id = Guid.NewGuid(), AuthorId = by, AuthorName = NameOf(by), Body = body, CreatedAt = at, Respect = respect.ToList(),
    };

    private static BoardThread Thread(Guid author, long at, params BoardReply[] replies) => new()
    {
        Id = Guid.NewGuid(), AuthorId = author, AuthorName = NameOf(author), Title = "t", Body = "opening",
        CreatedAt = at, LastActivityAt = replies.Length == 0 ? at : replies[^1].CreatedAt, Replies = replies.ToList(),
    };

    private static List<PlayerNotification> Board(params BoardThread[] threads)
        => Inbox.FromBoard(threads, Me, "Denise Smit", 100).ToList();

    [Fact]
    public void Board_RepliesInYourChain_AreOneNotification_NamingWhoReplied()
    {
        var mine = Thread(Me, 10, Reply(Ana, 200, "fair"), Reply(Bo, 210, "agreed"));

        var n = Assert.Single(Board(mine));

        Assert.Equal(NotificationKinds.BoardReply, n.Kind);
        Assert.Equal("Bo and Ana replied on your post", n.Title);
        Assert.Equal("agreed", n.Body);
        Assert.Equal(210, n.At);
        Assert.Equal(mine.Id, n.TargetId);
    }

    [Fact]
    public void Board_ChainYouRepliedIn_CountsOnlyWhatCameAfterYou()
    {
        var theirs = Thread(Ana, 10, Reply(Bo, 150, "before you"), Reply(Me, 160, "my two cents"), Reply(Ana, 220, "after you"));

        var n = Assert.Single(Board(theirs));

        Assert.Equal("Ana replied on Ana's post", n.Title);
        Assert.Equal("after you", n.Body);
    }

    [Fact]
    public void Board_NothingAfterYourLastWord_OrBeforeTheWindow_IsQuiet()
    {
        var answered = Thread(Me, 10, Reply(Ana, 200, "hi"), Reply(Me, 210, "hey"));
        var old = Thread(Me, 10, Reply(Ana, 50, "long ago"));
        var notYours = Thread(Ana, 10, Reply(Bo, 200, "nothing for you"));

        Assert.Empty(Board(answered, old, notYours));
    }

    [Fact]
    public void Board_MentionByFirstName_AsAWord_EvenOutsideYourChains()
    {
        var named = Thread(Ana, 10, Reply(Bo, 200, "what does denise think?"));
        var post = Thread(Ana, 205);
        post.Body = "Denise, coffee later?";
        var inside = Thread(Bo, 10, Reply(Ana, 200, "Denisestraat is closed"));

        var found = Board(named, post, inside);

        Assert.Equal(2, found.Count);
        Assert.All(found, n => Assert.Equal(NotificationKinds.BoardMention, n.Kind));
        Assert.Contains(found, n => n.TargetId == named.Id && n.Title == "Bo mentioned you on Partyboard");
        Assert.Contains(found, n => n.TargetId == post.Id && n.Title == "Ana mentioned you on Partyboard");
    }

    [Fact]
    public void Board_RespectOnYourLines_OnePerGiver_NotYourOwn()
    {
        var mine = Thread(Me, 10, Reply(Me, 20, "and another thing", new BoardRespect { GiverId = Bo, GiverName = "Bo", At = 300 }));
        mine.Respect.Add(new BoardRespect { GiverId = Ana, GiverName = "Ana", At = 250 });
        mine.Respect.Add(new BoardRespect { GiverId = Me, GiverName = "Denise Smit", At = 260 });
        mine.Respect.Add(new BoardRespect { GiverId = Bo, GiverName = "Bo", At = 40 });

        var found = Board(mine);

        Assert.Equal(2, found.Count);
        Assert.All(found, n => Assert.Equal(NotificationKinds.Respect, n.Kind));
        Assert.Contains(found, n => n.Title == "Ana gave you Respect!" && n.Body == "opening");
        Assert.Contains(found, n => n.Title == "Bo gave you Respect!" && n.Body == "and another thing");
    }

    [Fact]
    public void Board_NewReplyAfterReading_GetsANewId()
    {
        var mine = Thread(Me, 10, Reply(Ana, 200, "one"));
        var before = Assert.Single(Board(mine)).Id;
        mine.Replies.Add(Reply(Ana, 230, "two"));

        Assert.NotEqual(before, Assert.Single(Board(mine)).Id);
    }

    private static ChatMessage Message(int id, Guid from, long at, string content) => new()
    {
        MessageId = id, SenderId = from, SendAt = at, Content = content,
    };

    [Fact]
    public void Dm_CountsMessagesSinceYourLastOne()
    {
        var dm = new DmWaiting(Guid.NewGuid(), Ana, "Ana", new List<ChatMessage>
        {
            Message(1, Ana, 150, "hey"), Message(2, Me, 160, "hi!"), Message(3, Ana, 200, "you around?"), Message(4, Ana, 210, "hello?"),
        });

        var n = Inbox.FromDm(dm, Me, 100)!;

        Assert.Equal("Ana sent you 2 messages", n.Title);
        Assert.Equal("hello?", n.Body);
        Assert.Equal($"dm:{dm.ChatGroupId}:4", n.Id);
        Assert.Equal("messenger", n.App);
    }

    [Fact]
    public void Dm_YouHadTheLastWord_OrItFailed_IsQuiet()
    {
        var answered = new DmWaiting(Guid.NewGuid(), Ana, "Ana", new List<ChatMessage> { Message(1, Ana, 150, "hey"), Message(2, Me, 160, "hi") });
        var failed = new DmWaiting(Guid.NewGuid(), Ana, "Ana", new List<ChatMessage> { new() { MessageId = 1, SenderId = Ana, SendAt = 200, Error = "boom" } });

        Assert.Null(Inbox.FromDm(answered, Me, 100));
        Assert.Null(Inbox.FromDm(failed, Me, 100));
    }

    [Fact]
    public void Gatherings_OnlyStartedOnesYouSaidYoudAttend()
    {
        Gathering G(GatheringStatus status, params Guid[] attendees) => new()
        {
            Id = Guid.NewGuid(), Title = "Pub quiz", HostId = Ana, HostName = "Ana", RoomName = "kitchen", StartsAt = 200,
            Status = status, AttendeeIds = attendees.ToList(),
        };
        var going = G(GatheringStatus.Started, Me, Ana);

        var n = Assert.Single(Inbox.FromGatherings(new[] { going, G(GatheringStatus.Started, Ana), G(GatheringStatus.Planned, Me), G(GatheringStatus.Over, Me) }, Me, 100));

        Assert.Equal("“Pub quiz” is starting", n.Title);
        Assert.Equal("In #kitchen. You said you'd be there.", n.Body);
        Assert.Equal("open-party", n.App);
    }

    [Fact]
    public void Gifts_FromOthersInTheWindow()
    {
        var n = Assert.Single(Inbox.FromGifts(new[]
        {
            new GiftReceived(Ana, "Ana", "Red scarf", 200), new GiftReceived(Bo, "Bo", "Old hat", 50),
        }, Me, 100));

        Assert.Equal("Ana gave you a present", n.Title);
        Assert.Equal("Red scarf. It's in your Wardrobe.", n.Body);
    }

    [Fact]
    public void Feed_NewestFirst_UnreadAfterTheCursorAndNotOpened()
    {
        PlayerNotification N(string id, long at) => new(id, NotificationKinds.Dm, at, "t", "b", "messenger");

        var feed = Inbox.Feed(new[] { N("a", 100), N("b", 300), N("c", 200), N("d", 400) }, 150, new[] { "d" });

        Assert.Equal(new[] { "d", "b", "c", "a" }, feed.Items.Select(n => n.Id));
        Assert.Equal(new[] { false, true, true, false }, feed.Items.Select(n => n.Unread));
        Assert.Equal(2, feed.Unread);
    }

    [Fact]
    public void Feed_LeavesOutWhatWasDismissedOrCleared()
    {
        PlayerNotification N(string id, long at) => new(id, NotificationKinds.Dm, at, "t", "b", "messenger");
        var state = new NotificationsState { ClearedAt = 150, Dismissed = new List<string> { "c" } };

        var feed = Inbox.Feed(new[] { N("a", 100), N("b", 300), N("c", 200), N("d", 400) }, state);

        Assert.Equal(new[] { "d", "b" }, feed.Items.Select(n => n.Id));
        Assert.Equal(2, feed.Unread);
    }

    [Theory]
    [InlineData("Al", "also", false)]
    [InlineData("Al", "hey Al!", true)]
    [InlineData("Ö", "Ö", false)]
    [InlineData("Zoë Jansen", "zoë?", true)]
    public void Mentions_MatchTheFirstNameAsAWord(string name, string text, bool mentioned)
        => Assert.Equal(mentioned, Inbox.Mentions(text, name));
}
