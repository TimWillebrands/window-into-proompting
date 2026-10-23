using PartyTown.World;
using PartyTown.World.Apps;

namespace BackendTest;

/// <summary>Partyboard as a feed: the 280 cap, Respect! targeting and notifications, title-less posts.</summary>
public class PartyboardTest
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid Ana = Guid.NewGuid();

    [Fact]
    public void ClipToLength_PrefersSentenceEnd_ElseWordWithEllipsis()
    {
        Assert.Equal("short", BoardApp.ClipToLength("short", 280));

        var sentences = string.Concat(Enumerable.Repeat("This is fine. ", 30));
        var clipped = BoardApp.ClipToLength(sentences, 280);
        Assert.True(clipped.Length <= 280);
        Assert.EndsWith(".", clipped);

        var words = string.Join(" ", Enumerable.Repeat("word", 100));
        var cut = BoardApp.ClipToLength(words, 280);
        Assert.True(cut.Length <= 280);
        Assert.EndsWith("word…", cut);
    }

    [Fact]
    public void RespectTarget_IsNewestReplyByOthers_ElseThePost_NeverYourself()
    {
        var reply = new BoardReply { Id = Guid.NewGuid(), AuthorId = Ana, AuthorName = "Ana" };
        var thread = new BoardThread { AuthorId = Ana, AuthorName = "Ana", Replies = { reply, new BoardReply { AuthorId = Me } } };
        Assert.Equal((reply.Id, "Ana"), BoardApp.RespectTarget(thread, Me));

        Assert.Equal(((Guid?)null, "Ana"), BoardApp.RespectTarget(new BoardThread { AuthorId = Ana, AuthorName = "Ana" }, Me));
        Assert.Null(BoardApp.RespectTarget(new BoardThread { AuthorId = Me }, Me));
    }

    [Fact]
    public void RespectSince_CountsOnlyNewRespectOnYourOwnLines()
    {
        var mine = new BoardThread { AuthorId = Me, Respect = { new BoardRespect { GiverId = Ana, GiverName = "Ana", At = 200 } } };
        var theirs = new BoardThread
        {
            AuthorId = Ana,
            Respect = { new BoardRespect { GiverId = Guid.NewGuid(), GiverName = "Bo", At = 300 } },
            Replies = { new BoardReply { AuthorId = Me, Respect = { new BoardRespect { GiverId = Guid.NewGuid(), GiverName = "Cy", At = 50 } } } },
        };

        Assert.Equal(new[] { "Ana" }, BoardApp.RespectSince(new List<BoardThread> { mine, theirs }, Me, 100));
    }

    [Fact]
    public void PassedOn_CountsARespectToo()
    {
        var thread = new BoardThread { Id = Guid.NewGuid(), LastActivityAt = 1_000 };
        var respect = new WorldActivity { App = "board", Action = "respect", TargetId = thread.Id, Ts = 2_000 };
        Assert.True(BoardApp.PassedOn(new[] { respect }, thread));
    }

    [Fact]
    public void Gist_IsTheFirstLineClipped()
    {
        Assert.Equal("first line", BoardGrain.Gist("  first line\nsecond"));
        Assert.True(BoardGrain.Gist(new string('x', 200)).Length <= 61);
    }
}
