using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PartyTown.Services.Import;

/// <summary>
/// Deterministic IR parser for Gemini AI Studio exports — the source-format seam
/// (ADR 0017). Every chunk is categorized by structured fields plus one regex, never by
/// LLM; everything downstream consumes only the IR. Ported from the spearhead probes.
/// </summary>
public static class AiStudioImportParser
{
    // Recognizes the author's own recap headers ("# History", "## Checkpoint …").
    private static readonly Regex RecapHeader = new(@"^\s*#+\s*(history|checkpoint)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Parse an AI Studio export into typed chunks + the systemInstruction text.
    /// Throws <see cref="FormatException"/> when the document is not an AI Studio export.</summary>
    public static ImportSource Parse(string json, string? fileName)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Upload is not valid JSON.", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("chunkedPrompt", out var chunkedPrompt) ||
                !chunkedPrompt.TryGetProperty("chunks", out var chunks) ||
                chunks.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Upload is not an AI Studio export: missing chunkedPrompt.chunks.");
            }

            var systemInstruction =
                doc.RootElement.TryGetProperty("systemInstruction", out var si) &&
                si.ValueKind == JsonValueKind.Object &&
                si.TryGetProperty("text", out var siText)
                    ? siText.GetString() ?? ""
                    : "";

            var ir = new List<ImportChunk>();
            var idx = 0;
            foreach (var chunk in chunks.EnumerateArray())
            {
                // `.text` is canonical — AI Studio in-app edits leave `parts` stale.
                var text = chunk.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                var role = chunk.TryGetProperty("role", out var r) ? r.GetString() ?? "?" : "?";
                var category =
                    chunk.TryGetProperty("isThought", out var th) && th.ValueKind == JsonValueKind.True ? ImportChunkCategories.Thought
                    : (chunk.TryGetProperty("driveImage", out _) || chunk.TryGetProperty("driveAudio", out _)) && text.Length == 0 ? ImportChunkCategories.Media
                    : text.Length == 0 ? ImportChunkCategories.Empty
                    : RecapHeader.IsMatch(text) ? ImportChunkCategories.Recap
                    : ImportChunkCategories.Message;
                ir.Add(new ImportChunk { Index = idx++, Role = role, Category = category, Text = text });
            }

            return new ImportSource
            {
                FileName = fileName,
                SystemInstruction = systemInstruction,
                Chunks = ir,
            };
        }
    }
}

/// <summary>One canon-path section: a slice of the dossier or a recap chunk, split on the
/// author's own markdown seams (headings, *** dividers).</summary>
public sealed record CanonSection
{
    public required string Id { get; init; }
    public required string SourceId { get; init; }

    /// <summary>"dossier" or "recap" — steers the extraction prompt's framing.</summary>
    public required string Kind { get; init; }

    public required string Heading { get; init; }
    public required string Text { get; init; }
    public bool Truncated { get; init; }

    /// <summary>Chunk indices this section came from; empty for dossier sections.</summary>
    public List<int> SourceChunks { get; init; } = new();
}

/// <summary>
/// Splits canon text (dossier / recaps) on markdown headings and *** dividers — the seams
/// the author already chose when compressing. Ported from the spearhead probe.
/// </summary>
public static class CanonSectionSplitter
{
    public const int MinSectionChars = 200;   // merge smaller sections into the previous one
    public const int MaxSectionChars = 8_000; // hard cap sent to the model (truncation observed)

    private static readonly Regex HeadingLine = new(@"^#{1,6}\s+\S", RegexOptions.Compiled);
    private static readonly Regex DividerLine = new(@"^\*{3,}\s*$", RegexOptions.Compiled);

    public static List<CanonSection> Split(string sourceId, string kind, string text, List<int>? sourceChunks = null)
    {
        var rawSections = new List<(string Heading, StringBuilder Body)>();
        var current = (Heading: "(preamble)", Body: new StringBuilder());
        foreach (var line in text.Split('\n'))
        {
            if (HeadingLine.IsMatch(line) || DividerLine.IsMatch(line))
            {
                if (current.Body.Length > 0) rawSections.Add(current);
                var heading = DividerLine.IsMatch(line) ? "(divider)" : line.TrimStart('#', ' ').Trim();
                current = (heading, new StringBuilder());
                if (!DividerLine.IsMatch(line)) current.Body.AppendLine(line);
                continue;
            }
            current.Body.AppendLine(line);
        }
        if (current.Body.Length > 0) rawSections.Add(current);

        // merge tiny fragments backward so the model never sees a 3-line sliver alone
        var merged = new List<(string Heading, string Body)>();
        foreach (var (heading, body) in rawSections)
        {
            var trimmed = body.ToString().Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Length < MinSectionChars && merged.Count > 0)
                merged[^1] = (merged[^1].Heading, merged[^1].Body + "\n\n" + trimmed);
            else
                merged.Add((heading, trimmed));
        }

        return merged.Select((s, i) =>
        {
            var truncated = s.Body.Length > MaxSectionChars;
            var body = truncated ? s.Body[..MaxSectionChars] + "\n[…truncated]" : s.Body;
            return new CanonSection
            {
                Id = $"{sourceId}#{i}",
                SourceId = sourceId,
                Kind = kind,
                Heading = Truncate(s.Heading, 80),
                Text = body,
                Truncated = truncated,
                SourceChunks = (sourceChunks ?? new List<int>()).ToList(),
            };
        }).ToList();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";
}
