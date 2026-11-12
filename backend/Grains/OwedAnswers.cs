using PartyTown.Model;

namespace PartyTown.Grains;

/// <summary>A human's lines a Room still owes an answer (ADR 0031); in memory, so a restart may drop one but never doubles it.</summary>
public sealed class OwedAnswers(TimeSpan forgetAfter)
{
    private sealed class Line(Guid from, HashSet<Guid> listeners, long at)
    {
        public Guid From { get; } = from;
        public HashSet<Guid> Listeners { get; } = listeners;
        public HashSet<Guid> Waiting { get; } = new(listeners);
        public Dictionary<Guid, double> Urges { get; } = new();
        public long At { get; } = at;
        public bool Failed { get; set; }
    }

    private readonly Dictionary<int, Line> _lines = new();

    public bool Owes(int lineId) => _lines.ContainsKey(lineId);

    /// <summary>Every listener's turn on some line has ended: worth a look.</summary>
    public bool AnyDue => _lines.Values.Any(l => l.Waiting.Count == 0);

    /// <summary>A human said <paramref name="lineId"/>; each of <paramref name="listeners"/> takes a turn on it.</summary>
    public void Expect(int lineId, Guid from, IEnumerable<Guid> listeners, long now)
    {
        foreach (var stale in _lines.Where(kv => now - kv.Value.At > forgetAfter.TotalMilliseconds).Select(kv => kv.Key).ToList())
            _lines.Remove(stale);
        var heard = listeners.Where(id => id != from).ToHashSet();
        if (heard.Count > 0) _lines[lineId] = new Line(from, heard, now);
    }

    /// <summary>How much a listener wanted to answer it; the keenest is asked again.</summary>
    public void Urge(int lineId, Guid who, double? urge)
    {
        if (urge is { } u && _lines.TryGetValue(lineId, out var line)) line.Urges[who] = u;
    }

    /// <summary>A listener's turn on it ended; a failure (a stop pressed, a provider down) is not a decline.</summary>
    public void Ended(int lineId, Guid who, bool failed = false)
    {
        if (!_lines.TryGetValue(lineId, out var line)) return;
        line.Waiting.Remove(who);
        line.Failed |= failed;
    }

    /// <summary>The log was cut back past these lines (Reprompt): their ids come round again.</summary>
    public void Forget(int afterLineId)
    {
        foreach (var id in _lines.Keys.Where(id => id > afterLineId).ToList()) _lines.Remove(id);
    }

    /// <summary>Someone has it in hand without a turn here, as Reflex's quick line.</summary>
    public void Answered(int lineId) => _lines.Remove(lineId);

    /// <summary>A line landed: an older one is answered if its human heard this, or said it.</summary>
    public void Landed(int lineId, Guid sender, IReadOnlyCollection<Guid>? heardBy)
    {
        foreach (var (id, line) in _lines.Where(kv => kv.Key < lineId).ToList())
            if (sender == line.From || heardBy is null || heardBy.Contains(line.From))
                _lines.Remove(id);
    }

    /// <summary>Lines every listener let pass, each with who answers it; handed out once, then no longer owed.</summary>
    /// <param name="onItsWay">A turn is still open after the line: wait for it.</param>
    /// <param name="present">Who could still answer, as the Room has them now.</param>
    public List<(int LineId, Guid Who)> Due(Func<int, bool> onItsWay, IReadOnlyCollection<SelfView> present)
    {
        var due = new List<(int, Guid)>();
        foreach (var (id, line) in _lines.Where(kv => kv.Value.Waiting.Count == 0).ToList())
        {
            if (!line.Failed && onItsWay(id)) continue;
            _lines.Remove(id);
            if (line.Failed) continue;
            var keenest = present
                .Where(p => line.Listeners.Contains(p.Id))
                .OrderByDescending(p => line.Urges.GetValueOrDefault(p.Id))
                .ThenByDescending(p => p.Chattiness)
                .Select(p => (Guid?)p.Id)
                .FirstOrDefault();
            if (keenest is { } who) due.Add((id, who));
        }
        return due;
    }
}
