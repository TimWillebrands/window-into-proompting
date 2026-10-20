namespace PartyTown.World;

/// <summary>
/// The Board App's store: a Party-wide asynchronous forum. Plain persisted state (ADR 0018) —
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

    public async Task<BoardThread> CreateThreadAsync(Guid authorId, string authorName, string title, string body)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var thread = new BoardThread
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            AuthorName = authorName,
            Title = WorldStream.Clip(title, 140),
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
}

[Alias("PartyTown.World.IBoardGrain")]
public interface IBoardGrain : IGrainWithGuidKey
{
    [Alias("GetThreadsAsync")] Task<List<BoardThread>> GetThreadsAsync();
    [Alias("GetThreadAsync")] Task<BoardThread?> GetThreadAsync(Guid threadId);
    [Alias("CreateThreadAsync")] Task<BoardThread> CreateThreadAsync(Guid authorId, string authorName, string title, string body);
    [Alias("ReplyAsync")] Task<BoardThread?> ReplyAsync(Guid threadId, Guid authorId, string authorName, string body);
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
    [Id(3)] public string Title { get; set; } = string.Empty;
    [Id(4)] public string Body { get; set; } = string.Empty;
    [Id(5)] public long CreatedAt { get; set; }
    [Id(6)] public long LastActivityAt { get; set; }
    [Id(7)] public List<BoardReply> Replies { get; set; } = new();
}

[GenerateSerializer, Alias("PartyTown.World.BoardReply")]
public sealed class BoardReply
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public Guid AuthorId { get; set; }
    [Id(2)] public string AuthorName { get; set; } = string.Empty;
    [Id(3)] public string Body { get; set; } = string.Empty;
    [Id(4)] public long CreatedAt { get; set; }
}
