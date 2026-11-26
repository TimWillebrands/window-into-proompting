using System.Collections.Concurrent;
using System.Text;

namespace PartyTown.Services.ResponsePipeline;

public sealed class InFlightGeneration(CancellationTokenSource cts)
{
    private const int CharsPerTokenEstimate = 4;

    public CancellationTokenSource Cts { get; } = cts;

    private readonly object _lock = new();
    private InFlightPhase _phase = InFlightPhase.Weighing;
    private string _feeling = string.Empty;
    private string _intent = string.Empty;
    private string _replyingTo = string.Empty;
    private readonly StringBuilder _generatedText = new();
    private int _generatedChars;

    private bool _raceCancelled;
    private string _interruptingMessage = string.Empty;
    private string _interruptingSenderName = string.Empty;

    public void MarkGenerationStarted(string? feeling, string? intent, string? replyingTo = null)
    {
        lock (_lock)
        {
            _phase = InFlightPhase.Speaking;
            _feeling = feeling ?? string.Empty;
            _intent = intent ?? string.Empty;
            _replyingTo = replyingTo ?? string.Empty;
        }
    }

    public void AppendChunk(string chunk)
    {
        lock (_lock)
        {
            _generatedText.Append(chunk);
            _generatedChars = _generatedText.Length;
        }
    }

    public void ResetGeneratedText()
    {
        lock (_lock)
        {
            _generatedText.Clear();
            _generatedChars = 0;
        }
    }

    public void CancelForRace(string interruptingMessage, string interruptingSenderName)
    {
        lock (_lock)
        {
            _raceCancelled = true;
            _interruptingMessage = interruptingMessage ?? string.Empty;
            _interruptingSenderName = interruptingSenderName ?? string.Empty;
        }
        try { Cts.Cancel(); } catch (ObjectDisposedException) { }
    }

    public InFlightSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new InFlightSnapshot(
                _phase,
                _feeling,
                _intent,
                _replyingTo,
                _generatedText.ToString(),
                _generatedChars / CharsPerTokenEstimate,
                _raceCancelled,
                _interruptingMessage,
                _interruptingSenderName);
        }
    }
}

public enum InFlightPhase { Weighing, Speaking }

public readonly record struct RepairHint(
    int MissedMessageId,
    string MissedSenderName,
    string MissedContent);

public readonly record struct InFlightSnapshot(
    InFlightPhase Phase,
    string Feeling,
    string Intent,
    string ReplyingTo,
    string GeneratedText,
    int GeneratedTokens,
    bool RaceCancelled,
    string InterruptingMessage,
    string InterruptingSenderName);

public sealed class InFlightStore(TimeProvider? time = null)
{
    public static readonly TimeSpan RepairHintLasts = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<(Guid chatGroupId, int messageId), InFlightGeneration> _inFlight = new();

    private readonly ConcurrentDictionary<Guid, (RepairHint Hint, DateTimeOffset At)> _pendingRepairByGroup = new();

    private DateTimeOffset Now => (time ?? TimeProvider.System).GetUtcNow();

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _reading = new();

    public async Task<bool> ReadAsync(Guid chatGroupId, TimeSpan lag, CancellationToken ct)
    {
        using var mine = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_reading.TryGetValue(chatGroupId, out var older))
            try { older.Cancel(); } catch (ObjectDisposedException) { }
        _reading[chatGroupId] = mine;
        try
        {
            await Task.Delay(lag, mine.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            _reading.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(chatGroupId, mine));
        }
    }

    public InFlightGeneration Register(Guid chatGroupId, int messageId, CancellationTokenSource cts)
    {
        var inFlight = new InFlightGeneration(cts);
        _inFlight[(chatGroupId, messageId)] = inFlight;
        return inFlight;
    }

    public void Remove(Guid chatGroupId, int messageId)
    {
        if (_inFlight.TryRemove((chatGroupId, messageId), out var gen))
            gen.Cts.Dispose();
    }

    public IReadOnlyList<(int messageId, InFlightGeneration gen)> SnapshotForChatGroup(Guid chatGroupId)
    {
        return _inFlight
            .Where(kv => kv.Key.chatGroupId == chatGroupId)
            .Select(kv => (kv.Key.messageId, kv.Value))
            .ToList();
    }

    public RepairHint? ConsumeRepairHint(Guid chatGroupId)
        => _pendingRepairByGroup.TryRemove(chatGroupId, out var held) && Now - held.At <= RepairHintLasts ? held.Hint : null;

    public void SetRepairHint(Guid chatGroupId, RepairHint hint)
    {
        _pendingRepairByGroup[chatGroupId] = (hint, Now);
    }

    public Task CancelAllAsync()
    {
        foreach (var gen in _inFlight.Values)
        {
            try { gen.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        return Task.CompletedTask;
    }
}
