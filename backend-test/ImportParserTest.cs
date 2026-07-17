using PartyTown.Services.Import;

namespace BackendTest;

/// <summary>
/// Deterministic IR parser + section splitter (ADR 0017). The parser is the
/// source-format seam: categorisation must come from structured fields + one regex,
/// never from content heuristics that could drift.
/// </summary>
public class ImportParserTest
{
    private static string Chunk(string role, string text, string extra = "")
        => $"{{ \"role\": \"{role}\", \"text\": {System.Text.Json.JsonSerializer.Serialize(text)}{extra} }}";

    private static string Export(params string[] chunks) => $$"""
        {
          "systemInstruction": { "text": "You are Dr Lena Brandt.\n\n# Appearance\nTall.\n" },
          "chunkedPrompt": { "chunks": [ {{string.Join(",", chunks)}} ] }
        }
        """;

    [Fact]
    public void Categorises_chunks_by_structured_fields_and_recap_regex()
    {
        var source = AiStudioImportParser.Parse(Export(
            Chunk("model", "some reasoning", ", \"isThought\": true"),
            Chunk("user", "", ", \"driveImage\": { \"id\": \"x\" }"),
            Chunk("user", ""),
            Chunk("user", "# History checkpoint one\nStuff happened."),
            Chunk("user", "## Checkpoint 2\nMore stuff."),
            Chunk("model", "Hello there."),
            Chunk("user", "Hi Lena.")), "test.json");

        Assert.Equal(7, source.Chunks.Count);
        Assert.Equal(
            new[] { "thought", "media", "empty", "recap", "recap", "message", "message" },
            source.Chunks.Select(c => c.Category).ToArray());
        Assert.Equal(Enumerable.Range(0, 7), source.Chunks.Select(c => c.Index));
        Assert.StartsWith("You are Dr Lena Brandt.", source.SystemInstruction);
    }

    [Fact]
    public void Thought_flag_wins_over_recap_header()
    {
        var source = AiStudioImportParser.Parse(Export(
            Chunk("model", "# History of my reasoning", ", \"isThought\": true")), null);
        Assert.Equal(ImportChunkCategories.Thought, source.Chunks[0].Category);
    }

    [Fact]
    public void Media_with_text_is_a_message_not_media()
    {
        // media category only applies when the chunk carries no text of its own
        var source = AiStudioImportParser.Parse(Export(
            Chunk("user", "look at this", ", \"driveImage\": { \"id\": \"x\" }")), null);
        Assert.Equal(ImportChunkCategories.Message, source.Chunks[0].Category);
    }

    [Fact]
    public void Rejects_non_export_json()
    {
        Assert.Throws<FormatException>(() => AiStudioImportParser.Parse("{ \"foo\": 1 }", null));
        Assert.Throws<FormatException>(() => AiStudioImportParser.Parse("not json at all", null));
    }

    [Fact]
    public void Technokangs_reference_export_categorises_as_measured()
    {
        // Acceptance ground truth from the one-off jq analysis of the reference export:
        // 316 chunks — 93 thought / 5 media / 6 recap / 212 message.
        var path = FindRepoFile("docs/Copy of Technokangs.json");
        if (path is null) return; // export not present in this checkout — nothing to assert

        var source = AiStudioImportParser.Parse(File.ReadAllText(path), "Copy of Technokangs.json");
        var counts = source.Chunks.GroupBy(c => c.Category).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(316, source.Chunks.Count);
        Assert.Equal(93, counts[ImportChunkCategories.Thought]);
        Assert.Equal(5, counts[ImportChunkCategories.Media]);
        Assert.Equal(6, counts[ImportChunkCategories.Recap]);
        Assert.Equal(212, counts[ImportChunkCategories.Message]);
    }

    [Fact]
    public void Section_splitter_splits_on_headings_and_dividers_and_merges_slivers()
    {
        var text = string.Join("\n",
            "preamble line that is long enough to matter " + new string('x', 200),
            "# First",
            new string('a', 300),
            "***",
            new string('b', 300),
            "## Tiny",
            "short");

        var sections = CanonSectionSplitter.Split("systemInstruction", "dossier", text);

        Assert.Equal(3, sections.Count);
        Assert.Equal("(preamble)", sections[0].Heading);
        Assert.Equal("First", sections[1].Heading);
        Assert.Equal("(divider)", sections[2].Heading);
        // the sub-200-char "## Tiny" fragment merged backward into the divider section
        Assert.Contains("short", sections[2].Text);
        Assert.Equal(new[] { "systemInstruction#0", "systemInstruction#1", "systemInstruction#2" },
            sections.Select(s => s.Id).ToArray());
    }

    [Fact]
    public void Section_splitter_truncates_oversized_sections()
    {
        var text = "# Big\n" + new string('z', CanonSectionSplitter.MaxSectionChars + 500);
        var sections = CanonSectionSplitter.Split("chunk[3]", "recap", text, new List<int> { 3 });

        var section = Assert.Single(sections);
        Assert.True(section.Truncated);
        Assert.EndsWith("[…truncated]", section.Text);
        Assert.Equal(new List<int> { 3 }, section.SourceChunks);
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
