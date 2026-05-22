using System.Collections.Concurrent;
using System.Text;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Race-relevant state for one in-flight generation. Mutated in place as the work
/// progresses through Decision → Generation → done. <see cref="Snapshot"/> takes a
/// consistent read under lock for the race trigger; mutations from the streaming
/// loop also acquire the lock so concurrent reads see coherent (gut, preview, text,
/// tokens) tuples.
/// </summary>
internal sealed class InFlightGeneration(CancellationTokenSource cts)
{
    // Char-to-token approximation (~4 chars per token, English-leaning). Crude but stable
    // — the race math only needs this for "are we past PNR yet?" not exact accounting.
    // Replace with a real tokenizer only if traces show wrong PNR triggers.
    private const int CharsPerTokenEstimate = 4;

    public CancellationTokenSource Cts { get; } = cts;

    private readonly object _lock = new();
    private InFlightPhase _phase = InFlightPhase.Decision;
    private string _gutReaction = string.Empty;
    private string _wouldSayPreview = string.Empty;
    private readonly StringBuilder _generatedText = new();
    private int _generatedChars;

    // Set by the race when it elects to cancel this generation; consumed in the
    // OperationCanceledException catch to distinguish race-cancel (→ emote) from
    // external cancel via PartyGrain.CancelGenerationAsync (→ red error).
    private bool _raceCancelled;
    private string _interruptingMessage = string.Empty;
    private string _interruptingSenderName = string.Empty;

    public void MarkGenerationStarted(string gutReaction, string wouldSayPreview)
    {
        lock (_lock)
        {
            _phase = InFlightPhase.Speaking;
            _gutReaction = gutReaction ?? string.Empty;
            _wouldSayPreview = wouldSayPreview ?? string.Empty;
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

    /// <summary>Mark this generation as race-cancelled before triggering the CTS, so the
    /// catch can route to the emote path. Captures the interrupting message context for
    /// the emote-generation prompt.</summary>
    public void MarkRaceCancelled(string interruptingMessage, string interruptingSenderName)
    {
        lock (_lock)
        {
            _raceCancelled = true;
            _interruptingMessage = interruptingMessage ?? string.Empty;
            _interruptingSenderName = interruptingSenderName ?? string.Empty;
        }
    }

    public InFlightSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new InFlightSnapshot(
                _phase,
                _gutReaction,
                _wouldSayPreview,
                _generatedText.ToString(),
                _generatedChars / CharsPerTokenEstimate,
                _raceCancelled,
                _interruptingMessage,
                _interruptingSenderName);
        }
    }
}

internal enum InFlightPhase { Decision, Speaking }

internal readonly record struct InFlightSnapshot(
    InFlightPhase Phase,
    string GutReaction,
    string WouldSayPreview,
    string GeneratedText,
    int GeneratedTokens,
    bool RaceCancelled,
    string InterruptingMessage,
    string InterruptingSenderName);

/// <summary>
/// Per-PersonaGrain in-flight generation tracker. One instance per activation.
/// Thread-safe — ConcurrentDictionary operations are lock-free for readers.
/// </summary>
internal sealed class InFlightStore
{
    // Keyed per *generation* (chatGroupId, messageId), not per chat group. Earlier
    // (per-chat-group) keying caused message N+1 to cancel message N's still-running
    // decision/speaking, surfacing as a phantom "cancelled" appraisal on legitimate work
    // and an empty assistant slot for any persona slow enough to overlap a follow-up.
    private readonly ConcurrentDictionary<(Guid chatGroupId, int messageId), InFlightGeneration> _inFlight = new();

    // Levelt-style repair hints, keyed by chat group. Set when a new message arrives
    // during in-flight generation and the race elects NOT to cancel (either past PNR
    // or salience didn't justify interruption). Consumed once on the next decision pass
    // for that chat group, then cleared regardless of decision outcome.
    private readonly ConcurrentDictionary<Guid, RepairHint> _pendingRepairByGroup = new();

    /// <summary>Register a new in-flight generation. Returns the created record.</summary>
    public InFlightGeneration Register(Guid chatGroupId, int messageId, CancellationTokenSource cts)
    {
        var inFlight = new InFlightGeneration(cts);
        _inFlight[(chatGroupId, messageId)] = inFlight;
        return inFlight;
    }

    /// <summary>Remove an in-flight generation and dispose its CTS.</summary>
    public void Remove(Guid chatGroupId, int messageId)
    {
        if (_inFlight.TryRemove((chatGroupId, messageId), out var gen))
            gen.Cts.Dispose();
    }

    /// <summary>Snapshot all in-flight generations for a chat group.</summary>
    public IReadOnlyList<(int messageId, InFlightGeneration gen)> SnapshotForChatGroup(Guid chatGroupId)
    {
        return _inFlight
            .Where(kv => kv.Key.chatGroupId == chatGroupId)
            .Select(kv => (kv.Key.messageId, kv.Value))
            .ToList();
    }

    /// <summary>Consume and clear any pending repair hint for a chat group.</summary>
    public RepairHint? ConsumeRepairHint(Guid chatGroupId)
    {
        return _pendingRepairByGroup.TryRemove(chatGroupId, out var hint) ? hint : null;
    }

    /// <summary>Set a repair hint for a chat group.</summary>
    public void SetRepairHint(Guid chatGroupId, RepairHint hint)
    {
        _pendingRepairByGroup[chatGroupId] = hint;
    }

    /// <summary>Cancel every in-flight generation. Idempotent w.r.t. disposed CTSes.</summary>
    public Task CancelAllAsync()
    {
        foreach (var gen in _inFlight.Values)
        {
            try { gen.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        return Task.CompletedTask;
    }
}
