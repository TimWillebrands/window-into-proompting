using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PartyTown.Grains;
using PartyTown.Model;

namespace PartyTown.Services.Papertrail;

/// <summary>
/// Builds an agent-readable causal trail of a chat group's persona turns.
/// Reconstructs the "who reacted to what" tree from durable event-sourced state
/// (messages + skipped-turn records) so the output is reproducible across silo
/// restarts and OTel trace expiry.
/// </summary>
public static class PapertrailRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static PapertrailDocument Build(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<SkippedTurn> skippedTurns,
        IReadOnlyList<ParticipantView> participants,
        int? sinceMessageId = null)
    {
        var nameById = participants.ToDictionary(p => p.Id, p => p.Name);
        var ordered = messages.OrderBy(m => m.MessageId).ToList();

        // Bucket reactions under their triggering message id. Reactions are messages with
        // a non-null TriggeredByMessageId plus any skip records keyed to the same trigger.
        var reactionMessages = ordered
            .Where(m => m.TriggeredByMessageId is not null)
            .GroupBy(m => m.TriggeredByMessageId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var reactionSkips = skippedTurns
            .GroupBy(s => s.TriggeredByMessageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var roots = ordered
            .Where(m => m.TriggeredByMessageId is null)
            .Where(m => !(m.SenderId == Guid.Empty && string.IsNullOrEmpty(m.Content)))
            .Where(m => sinceMessageId is null || m.MessageId >= sinceMessageId)
            .Select(m => BuildEntry(m, nameById, reactionMessages, reactionSkips))
            .ToList();

        return new PapertrailDocument
        {
            ChatGroupId = ordered.FirstOrDefault()?.ChatGroupId ?? Guid.Empty,
            Roots = roots
        };
    }

    private static PapertrailEntry BuildEntry(
        ChatMessage message,
        Dictionary<Guid, string> nameById,
        Dictionary<int, List<ChatMessage>> reactionMessages,
        Dictionary<int, List<SkippedTurn>> reactionSkips)
    {
        var reactions = new List<PapertrailEntry>();

        if (reactionMessages.TryGetValue(message.MessageId, out var children))
        {
            // Legacy decline events (pre-slot-tracking) carry SenderId = Guid.Empty + empty
            // content. They have no recoverable persona attribution so they read as noise.
            // Suppress them here rather than letting the renderer print "00000000-… declined".
            foreach (var child in children)
            {
                if (child.SenderId == Guid.Empty && string.IsNullOrEmpty(child.Content))
                    continue;
                reactions.Add(BuildEntry(child, nameById, reactionMessages, reactionSkips));
            }
        }

        if (reactionSkips.TryGetValue(message.MessageId, out var skips))
        {
            foreach (var skip in skips)
                reactions.Add(SkipEntry(skip));
        }

        // Order siblings chronologically so the tree reads top-to-bottom in clock time.
        reactions.Sort((a, b) => Nullable.Compare(a.SendAt, b.SendAt));

        var senderName = nameById.GetValueOrDefault(message.SenderId, message.SenderId.ToString());
        return new PapertrailEntry
        {
            Kind = message.SenderType == "user" ? "user" : "assistant",
            MessageId = message.MessageId,
            SenderId = message.SenderId,
            SenderName = senderName,
            Content = message.Content,
            Reasoning = message.Reasoning,
            Appraisal = TryParseAppraisal(message.Appraisal),
            Provider = message.Metadata?.Provider,
            Model = message.Metadata?.ModelName,
            Error = message.Error,
            SendAt = message.SendAt,
            TriggeredByMessageId = message.TriggeredByMessageId,
            Reactions = reactions
        };
    }

    private static PapertrailEntry SkipEntry(SkippedTurn skip) => new()
    {
        Kind = "skip",
        SenderId = skip.PersonaId,
        SenderName = skip.PersonaName,
        Reason = skip.Reason,
        UrgeTotal = skip.UrgeTotal,
        SendAt = skip.SendAt,
        TriggeredByMessageId = skip.TriggeredByMessageId,
        Reactions = []
    };

    private static AppraisalSummary? TryParseAppraisal(string? appraisalJson)
    {
        if (string.IsNullOrWhiteSpace(appraisalJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(appraisalJson);
            var root = doc.RootElement;
            return new AppraisalSummary
            {
                Instruction = root.TryGetProperty("instruction", out var instr) ? instr.GetString() : null,
                Reason = root.TryGetProperty("reason", out var reason) ? reason.GetString() : null,
                Recall = root.TryGetProperty("recall", out var recall) && recall.ValueKind == JsonValueKind.Object
                    ? recall.Deserialize<RecallSummary>(JsonOptions)
                    : null
            };
        }
        catch
        {
            return null;
        }
    }

    public static string ToText(PapertrailDocument doc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Papertrail for chat group {doc.ChatGroupId}");
        sb.AppendLine();
        long? anchor = null;
        foreach (var root in doc.Roots)
        {
            anchor ??= root.SendAt;
            RenderEntry(sb, root, depth: 0, anchor);
        }
        return sb.ToString();
    }

    private static void RenderEntry(StringBuilder sb, PapertrailEntry entry, int depth, long? anchor)
    {
        var indent = new string(' ', depth * 2);
        var time = FormatRelative(entry.SendAt, anchor);

        switch (entry.Kind)
        {
            case "skip":
                sb.AppendLine($"{indent}└─ [skip {time}] {entry.SenderName} (urge={entry.UrgeTotal:F2}, {entry.Reason})");
                break;
            case "user":
                sb.AppendLine($"{indent}[#{entry.MessageId} {time}] {entry.SenderName} (user): {Truncate(entry.Content, 240)}");
                break;
            default:
                var attribution = (entry.Provider, entry.Model) switch
                {
                    (not null, not null) => $"  [{entry.Provider}/{entry.Model}]",
                    _ => string.Empty
                };
                var gut = entry.Appraisal?.Reason is { Length: > 0 } r ? $"\n{indent}    gut: {Truncate(r, 200)}" : "";
                // ADR 0015 observability: candidate set + pick per recall, so memory misses
                // are diagnosable from the trail alone (the embeddings-deferral trigger).
                var recall = entry.Appraisal?.Recall is { Candidates.Count: > 0 } rc
                    ? $"\n{indent}    recall: [{string.Join(", ", rc.Candidates.Select(RenderRecallCandidate))}] picked={(rc.Picked is int p ? $"#{p}" : "none")}"
                    : "";
                if (entry.Content is null or "" && entry.Error is null)
                {
                    sb.AppendLine($"{indent}└─ [#{entry.MessageId} {time}] {entry.SenderName} declined.{gut}{recall}");
                }
                else if (entry.Error is not null)
                {
                    sb.AppendLine($"{indent}└─ [#{entry.MessageId} {time}] {entry.SenderName} ERROR: {Truncate(entry.Error, 200)}");
                }
                else
                {
                    sb.AppendLine($"{indent}└─ [#{entry.MessageId} {time}] {entry.SenderName}: {Truncate(entry.Content, 240)}{attribution}{gut}{recall}");
                }
                break;
        }

        foreach (var child in entry.Reactions)
            RenderEntry(sb, child, depth + 1, anchor);
    }

    public static string ToJson(PapertrailDocument doc)
        => JsonSerializer.Serialize(doc, JsonOptions);

    // Arm attribution (ADR 0015 two-arm union) abbreviated to keep the line scannable:
    // `(rel)` graph-walk arm, `(rec)` recency arm; pre-arm entries carry no suffix.
    private static string RenderRecallCandidate(RecallCandidateSummary c)
        => $"#{c.Index}={c.Score:F2}{(string.IsNullOrEmpty(c.Arm) ? "" : $"({c.Arm[..Math.Min(3, c.Arm.Length)]})")}";

    private static string FormatRelative(long? sendAt, long? anchor)
    {
        if (sendAt is null) return "";
        if (anchor is null || anchor == sendAt) return "t+0.0s";
        var delta = (sendAt.Value - anchor.Value) / 1000.0;
        return delta >= 0 ? $"t+{delta:F1}s" : $"t{delta:F1}s";
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var collapsed = s.ReplaceLineEndings(" ").Trim();
        return collapsed.Length <= max ? collapsed : collapsed[..max] + "…";
    }
}

public sealed class PapertrailDocument
{
    public Guid ChatGroupId { get; init; }
    public List<PapertrailEntry> Roots { get; init; } = [];
}

public sealed class PapertrailEntry
{
    public string Kind { get; init; } = "";
    public int? MessageId { get; init; }
    public Guid SenderId { get; init; }
    public string SenderName { get; init; } = "";
    public string? Content { get; init; }
    public string? Reasoning { get; init; }
    public AppraisalSummary? Appraisal { get; init; }
    public string? Provider { get; init; }
    public string? Model { get; init; }
    public string? Error { get; init; }
    public string? Reason { get; init; }
    public double? UrgeTotal { get; init; }
    public long? SendAt { get; init; }
    public int? TriggeredByMessageId { get; init; }
    public List<PapertrailEntry> Reactions { get; init; } = [];
}

public sealed class AppraisalSummary
{
    public string? Instruction { get; init; }
    public string? Reason { get; init; }
    public RecallSummary? Recall { get; init; }
}

/// <summary>
/// The turn's recall snapshot as the response pipeline logged it into the appraisal JSON
/// (ADR 0015 observability): every surfaced candidate with its salience score, plus the
/// Decision phase's pick (1-based index into the surfaced list, null = nothing fit).
/// </summary>
public sealed class RecallSummary
{
    public List<RecallCandidateSummary> Candidates { get; init; } = [];
    public int? Picked { get; init; }
    public Guid? PickedId { get; init; }
}

public sealed class RecallCandidateSummary
{
    public int Index { get; init; }
    public Guid Id { get; init; }
    public double Score { get; init; }
    public double Weight { get; init; }
    public int RecallCount { get; init; }

    /// <summary>
    /// Which arm's slot this candidate consumed: <c>"relevant"</c> (anchor graph-walk) or
    /// <c>"recent"</c> (recency pool). Null on entries written before the two-arm union.
    /// </summary>
    public string? Arm { get; init; }
}
