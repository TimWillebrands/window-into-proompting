using PartyTown.World;
using PartyTown.World.Apps;

namespace BackendTest;

/// <summary>The Board closed: only what's new and what concerns you reaches the Persona.</summary>
public class BoardGlanceTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();
    private static bool NamesMe(string? text) => text?.Contains("Denise") == true;

    private static BoardThread Thread(Guid author, long at, params (Guid By, long At, string Body)[] replies) => new()
    {
        Id = Guid.NewGuid(), AuthorId = author, AuthorName = author == Me ? "Denise" : "Ana", Title = "t", Body = "opening",
        CreatedAt = at, LastActivityAt = replies.Length == 0 ? at : replies[^1].At,
        Replies = replies.Select(r => new BoardReply { AuthorId = r.By, AuthorName = r.By == Me ? "Denise" : "Ana", Body = r.Body, CreatedAt = r.At }).ToList(),
    };

    [Fact]
    public void NewSince_CountsOnlyOthersAfterYouLooked()
    {
        var all = new List<BoardThread>
        {
            Thread(Ana, 50, (Me, 60, "mine"), (Ana, 120, "theirs")),
            Thread(Ana, 150),
            Thread(Me, 160),
        };

        Assert.Equal((1, 1), BoardApp.NewSince(all, Me, 100));
    }

    [Fact]
    public void Notifications_ReplyToYou_AndMentions_ButNotWhatYouSaw()
    {
        var replied = Thread(Me, 10, (Ana, 200, "fair point"));
        var named = Thread(Ana, 20, (Ana, 210, "what does Denise think?"));
        var seen = Thread(Me, 30, (Ana, 50, "old news"));
        var unrelated = Thread(Ana, 40, (Ana, 220, "nothing for you"));

        var found = BoardApp.Notifications(new List<BoardThread> { replied, named, seen, unrelated }, Me, NamesMe, 100).ToList();

        Assert.Equal(2, found.Count);
        Assert.Contains(found, n => n.Thread == replied && n.How == "replied to you");
        Assert.Contains(found, n => n.Thread == named && n.How == "mentioned you");
    }

    [Fact]
    public void Notifications_AfterYourOwnReply_Stop()
    {
        var answered = Thread(Ana, 10, (Ana, 200, "Denise?"), (Me, 210, "here"));

        Assert.Empty(BoardApp.Notifications(new List<BoardThread> { answered }, Me, NamesMe, 0));
    }

    [Fact]
    public void Concerned_ThreadPeopleAndNamed_NotTheAuthor()
    {
        var bo = Guid.NewGuid();
        var cy = Guid.NewGuid();
        var thread = Thread(Ana, 10, (Me, 20, "hi"));
        var personas = new List<(Guid, string)> { (Me, "Denise Vos"), (Ana, "Ana"), (bo, "Bo"), (cy, "Cy") };

        var who = BoardApp.Concerned(thread, Ana, "what do you think, bo?", personas);

        Assert.Equal(new[] { Me, bo }, who);
    }
}
