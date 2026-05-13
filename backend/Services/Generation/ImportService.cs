using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using JsonRepairSharp;
using PartyTown.Grains.Generation;

namespace PartyTown.Services.Generation;

/// <summary>
/// Two-stage LLM driver for the chat-import flow:
///   • <see cref="ExtractPersonasAsync"/> — read the <c>systemInstruction</c> from a
///     Gemini AI Studio export, propose persona stubs.
///   • <see cref="ClassifyChunkAsync"/> — split one transcript chunk into
///     per-character segments tagged by kind.
///
/// Mirrors the <see cref="PersonaDecisionService"/> idiom: route via
/// <see cref="ILlmRouterGrain"/>, consume the streaming generator to completion, parse
/// with the standard cleanup pipeline (<see cref="LlmJsonParsing.ExtractJsonPayload"/>
/// then <see cref="JsonRepair"/>), and fall back to a safe default on parse failure
/// rather than throwing.
/// </summary>
public sealed class ImportService(IGrainFactory grains, ILogger<ImportService> logger)
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private ILlmRouterGrain Router => grains.GetGrain<ILlmRouterGrain>(0);

    public async Task<IReadOnlyList<ExtractedPersona>> ExtractPersonasAsync(
        string systemInstructionText,
        IReadOnlyList<ImportSampleChunk>? sampleChunks,
        CancellationToken cancellationToken)
    {
        // Build a token-budgeted transcript sample from the caller-selected chunks. We
        // cap total characters (≈10 KB) so the persona-extraction prompt stays well
        // within context, then format chunks as `[role] text` blocks separated by
        // blank lines. If we run out of budget mid-chunk we hard-truncate and add an
        // ellipsis — partial chunks still carry voice signal for name/personality
        // grounding.
        const int sampleBudgetChars = 10_000;
        var transcriptSample = BuildTranscriptSample(sampleChunks, sampleBudgetChars);

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.PersonaExtractionSystem },
            new() { Role = "user", Content = ImportPrompts.PersonaExtractionUser(systemInstructionText, transcriptSample) }
        };

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "extracted_personas",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["personas"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["name"] = new JsonObject { ["type"] = "string" },
                                    ["archetype"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["system_prompt"] = new JsonObject { ["type"] = "string" },
                                    ["bio"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                                },
                                ["required"] = new JsonArray("name", "archetype", "system_prompt", "bio")
                            }
                        }
                    },
                    ["required"] = new JsonArray("personas")
                }
            }
        };

        var raw = await RunStructuredAsync(messages, JobComplexity.General, responseFormat, cancellationToken);
        var parsed = TryParse<ExtractedPersonasPayload>(raw, "ExtractPersonas");

        if (parsed?.Personas is null || parsed.Personas.Count == 0)
        {
            logger.LogWarning(
                "Persona extraction returned empty (parsed null? {ParsedNull}). Raw output (truncated): {Raw}. Falling back to single Narrator stub.",
                parsed is null,
                raw.Length > 600 ? raw[..600] : raw);
            return [new ExtractedPersona { Name = "Narrator", Archetype = null, SystemPrompt = "A neutral narrator who describes the scene and events.", Bio = null }];
        }

        return parsed.Personas;
    }

    private static string BuildTranscriptSample(IReadOnlyList<ImportSampleChunk>? chunks, int maxChars)
    {
        if (chunks is null || chunks.Count == 0) return "(no transcript sample provided)";
        var sb = new StringBuilder();
        var remaining = maxChars;
        foreach (var chunk in chunks)
        {
            if (remaining <= 200) break;
            var header = $"[{chunk.Role}]\n";
            var text = chunk.Text ?? string.Empty;
            var blockBudget = remaining - header.Length - 2;
            if (blockBudget <= 0) break;
            var body = text.Length > blockBudget ? text[..blockBudget] + "…" : text;
            sb.Append(header).Append(body).Append("\n\n");
            remaining -= header.Length + body.Length + 2;
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no transcript sample provided)";
    }

    public async Task<ChunkClassification> ClassifyChunkAsync(
        string chunkText,
        string chunkRole,
        IReadOnlyList<PersonaRosterEntry> roster,
        CancellationToken cancellationToken)
    {
        var rosterJson = JsonSerializer.Serialize(roster, WebOptions);

        var messages = new List<LlmChatMessage>
        {
            new() { Role = "system", Content = ImportPrompts.ChunkClassifySystem },
            new() { Role = "user", Content = ImportPrompts.ChunkClassifyUser(rosterJson, chunkRole, chunkText) }
        };

        var responseFormat = new JsonObject
        {
            ["type"] = "json_schema",
            ["json_schema"] = new JsonObject
            {
                ["name"] = "chunk_classification",
                ["strict"] = true,
                ["schema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["segments"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["persona_id"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["new_persona_name"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                                    ["text"] = new JsonObject { ["type"] = "string" },
                                    ["kind"] = new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["enum"] = new JsonArray("dialogue", "action", "thought", "narration", "ooc")
                                    }
                                },
                                ["required"] = new JsonArray("persona_id", "new_persona_name", "text", "kind")
                            }
                        }
                    },
                    ["required"] = new JsonArray("segments")
                }
            }
        };

        try
        {
            var raw = await RunStructuredAsync(messages, JobComplexity.CharacterThoughts, responseFormat, cancellationToken);
            var parsed = TryParse<ChunkClassification>(raw, "ClassifyChunk");

            if (parsed?.Segments is null || parsed.Segments.Count == 0)
            {
                return UnattributedFallback(chunkText);
            }
            return parsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Chunk classification failed; using unattributed fallback.");
            return UnattributedFallback(chunkText);
        }
    }

    private async Task<string> RunStructuredAsync(
        List<LlmChatMessage> messages,
        JobComplexity complexity,
        JsonObject responseFormat,
        CancellationToken cancellationToken)
    {
        var job = new LlmGenerationJob
        {
            Messages = messages,
            JobComplexity = complexity,
            ResponseFormat = responseFormat.ToJsonString()
        };

        var endpoint = await Router.RouteAsync(complexity, cancellationToken);
        var buffer = new StringBuilder();
        await foreach (var chunk in endpoint.GenerateAsync(job, cancellationToken))
        {
            if (chunk.Type == "message")
                buffer.Append(chunk.Data);
        }
        return buffer.ToString().Trim();
    }

    private T? TryParse<T>(string raw, string label) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(raw, WebOptions); }
        catch (Exception ex) { logger.LogDebug("{Label} direct parse failed: {Error}", label, ex.Message); }

        try
        {
            var cleaned = LlmJsonParsing.ExtractJsonPayload(raw);
            try { return JsonSerializer.Deserialize<T>(cleaned, WebOptions); }
            catch
            {
                var repaired = JsonRepair.RepairJson(cleaned, JsonRepair.InputType.LLM);
                return JsonSerializer.Deserialize<T>(repaired, WebOptions);
            }
        }
        catch (Exception ex)
        {
            logger.LogError("{Label} JSON repair failed: {Error}. Raw (truncated): {Raw}",
                label, ex.Message, raw.Length > 400 ? raw[..400] : raw);
            return null;
        }
    }

    private static ChunkClassification UnattributedFallback(string chunkText)
        => new()
        {
            Segments = [new ClassifiedSegment
            {
                PersonaId = null,
                NewPersonaName = "Unattributed",
                Text = chunkText,
                Kind = "narration"
            }]
        };
}

/// <summary>One transcript chunk passed to <see cref="ImportService.ExtractPersonasAsync"/>
/// as additional grounding context. Used to expose actual dialogue and action prose
/// to the extractor — the <c>systemInstruction.text</c> on its own is often a thin
/// "you are a GM" frame that doesn't name the cast.</summary>
public sealed record class ImportSampleChunk
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed record class ExtractedPersonasPayload
{
    [JsonPropertyName("personas")]
    public List<ExtractedPersona> Personas { get; init; } = [];
}

public sealed record class ExtractedPersona
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("archetype")]
    public string? Archetype { get; init; }

    [JsonPropertyName("system_prompt")]
    public string SystemPrompt { get; init; } = string.Empty;

    [JsonPropertyName("bio")]
    public string? Bio { get; init; }
}

public sealed record class PersonaRosterEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("archetype")]
    public string? Archetype { get; init; }

    /// <summary>First ~200 chars of system_prompt — keeps the per-chunk prompt token
    /// budget bounded across hundreds of classification calls.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;
}

public sealed record class ChunkClassification
{
    [JsonPropertyName("segments")]
    public List<ClassifiedSegment> Segments { get; init; } = [];
}

public sealed record class ClassifiedSegment
{
    [JsonPropertyName("persona_id")]
    public string? PersonaId { get; init; }

    [JsonPropertyName("new_persona_name")]
    public string? NewPersonaName { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "narration";
}
