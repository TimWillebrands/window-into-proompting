namespace PartyTown.World;

/// <summary>
/// Partyboard's store: the Party-wide social feed (posts, replies, Respect). Plain persisted state (ADR 0018) —
/// posts are content, not conversational turn history.
/// </summary>
public sealed class BoardGrain(
    [PersistentState(stateName: "board", storageName: "parties")]
    IPersistentState<BoardState> state)
    : Grain, IBoardGrain
{
    private const int MaxThreads = 200;

    public Task<List<BoardThread>> GetThreadsAsync()
        => Task.FromResult(state.State.Threads.OrderByDescending(t => t.LastActivityAt).ToList());

    public Task<BoardThread?> GetThreadAsync(Guid threadId)
        => Task.FromResult(state.State.Threads.FirstOrDefault(t => t.Id == threadId));

    public async Task<BoardThread> CreateThreadAsync(Guid authorId, string authorName, string body)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thread = new BoardThread
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            AuthorName = authorName,
            Title = Gist(body),
            Body = body.Trim(),
            CreatedAt = now,
            LastActivityAt = now,
        };
        state.State.Threads.Add(thread);
        if (state.State.Threads.Count > MaxThreads)
            state.State.Threads = state.State.Threads.OrderByDescending(t => t.LastActivityAt).Take(MaxThreads).ToList();
        await state.WriteStateAsync();
        await this.GetStreamProvider(Services.Streaming.PartyStreamIds.Provider)
            .PublishWorldAsync(this.GetPrimaryKey(), "board", thread);
        return thread;
    }

    public async Task<BoardThread?> ReplyAsync(Guid threadId, Guid authorId, string authorName, string body)
    {
        var thread = state.State.Threads.FirstOrDefault(t => t.Id == threadId);
        if (thread is null) return null;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        thread.Replies.Add(new BoardReply
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            AuthorName = authorName,
            Body = body.Trim(),
            CreatedAt = now,
        });
        thread.LastActivityAt = now;
        await state.WriteStateAsync();
        await this.GetStreamProvider(Services.Streaming.PartyStreamIds.Provider)
            .PublishWorldAsync(this.GetPrimaryKey(), "board", thread);
        return thread;
    }

    public async Task<BoardThread?> RespectAsync(Guid threadId, Guid? replyId, Guid giverId, string giverName, bool given)
    {
        var thread = state.State.Threads.FirstOrDefault(t => t.Id == threadId);
        if (thread is null) return null;
        var respect = replyId is { } rid ? thread.Replies.FirstOrDefault(r => r.Id == rid)?.Respect : thread.Respect;
        if (respect is null) return null;

        var had = respect.FindIndex(r => r.GiverId == giverId);
        if (given == had >= 0) return thread;
        if (given) respect.Add(new BoardRespect { GiverId = giverId, GiverName = giverName, At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        else respect.RemoveAt(had);
        await state.WriteStateAsync();
        await this.GetStreamProvider(Services.Streaming.PartyStreamIds.Provider)
            .PublishWorldAsync(this.GetPrimaryKey(), "board", thread);
        return thread;
    }

    /// <summary>Short label for a post (activity log, memory, notifications); posts have no titles.</summary>
    public static string Gist(string body) => WorldStream.Clip(body.Trim().Split('\n', 2)[0], 60);
}

[Alias("PartyTown.World.IBoardGrain")]
public interface IBoardGrain : IGrainWithGuidKey
{
    [Alias("GetThreadsAsync")] Task<List<BoardThread>> GetThreadsAsync();
    [Alias("GetThreadAsync")] Task<BoardThread?> GetThreadAsync(Guid threadId);
    [Alias("CreateThreadAsync")] Task<BoardThread> CreateThreadAsync(Guid authorId, string authorName, string body);
    [Alias("ReplyAsync")] Task<BoardThread?> ReplyAsync(Guid threadId, Guid authorId, string authorName, string body);

    /// <summary>Give (or take back) Respect on a post, or on one of its replies; idempotent. Null when it's gone.</summary>
    [Alias("RespectAsync")] Task<BoardThread?> RespectAsync(Guid threadId, Guid? replyId, Guid giverId, string giverName, bool given);
}

[GenerateSerializer, Alias("PartyTown.World.BoardState")]
public sealed class BoardState
{
    [Id(0)] public List<BoardThread> Threads { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.BoardThread")]
public sealed class BoardThread
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid AuthorId { get; set; }
    [Id(2)] public string AuthorName { get; set; } = string.Empty;
    /// <summary>Gist of the body; posts have no titles (forum-era ones keep theirs).</summary>
    [Id(3)] public string Title { get; set; } = string.Empty;
    [Id(4)] public string Body { get; set; } = string.Empty;
    [Id(5)] public long CreatedAt { get; set; }
    [Id(6)] public long LastActivityAt { get; set; }
    [Id(7)] public List<BoardReply> Replies { get; set; } = new();
    [Id(8)] public List<BoardRespect> Respect { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.BoardReply")]
public sealed class BoardReply
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid AuthorId { get; set; }
    [Id(2)] public string AuthorName { get; set; } = string.Empty;
    [Id(3)] public string Body { get; set; } = string.Empty;
    [Id(4)] public long CreatedAt { get; set; }
    [Id(5)] public List<BoardRespect> Respect { get; set; } = new();
}

/// <summary>Hyves-style Respect!: a like, from someone, at some time.</summary>
[GenerateSerializer, Alias("PartyTown.World.BoardRespect")]
public sealed class BoardRespect
{
    [Id(0)] public Guid GiverId { get; set; }
    [Id(1)] public string GiverName { get; set; } = string.Empty;
    [Id(2)] public long At { get; set; }
}
