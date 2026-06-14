using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.Realtime;

namespace PartyTown.Controllers;

public sealed record DefaultPersonaTemplate
{
    public required string Name { get; init; }
    public required string SystemPrompt { get; init; }
    public required string Bio { get; init; }
}

public sealed record GenerateBioRequest
{
    public required string SystemPrompt { get; init; }
}

public sealed record GenerateBioResponse
{
    public required string Bio { get; init; }
}

public sealed record GeneratePersonaRequest
{
    public required string Prompt { get; init; }
}

public sealed record GeneratePersonaResponse
{
    public required string Name { get; init; }
    public required string SystemPrompt { get; init; }
    public required string Bio { get; init; }
}

[ApiController]
[Route("[controller]")]
/// <summary>
/// HTTP API for creating, reading, updating, and deleting personas.
/// </summary>
public class PersonaController(
    IGrainFactory grains,
    IMemoryCache cache,
    IWebHostEnvironment env,
    IMemoryRepository memoryRepository,
    ILogger<PersonaController> logger) : ControllerBase
{
    private const string BioGenerationSystemPrompt =
        "You are a concise biography writer for AI chat personas. Given a character's full system prompt, " +
        "write a compelling 2-3 sentence bio that captures their personality, role, and key defining traits. " +
        "The bio should read like a character description card — vivid but brief. " +
        "Output ONLY the bio text with no formatting, headers, or extra commentary.";

    /// <summary>
    /// Returns all personas currently registered.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<Persona[]>> GetAll()
    {
        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var personas = await root.GetAll();
        return Ok(personas);
    }

    /// <summary>
    /// Returns a single persona by id.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Persona>> GetById(Guid id)
    {
        if (id == Guid.Empty)
        {
            return BadRequest("Persona id cannot be empty.");
        }

        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var all = await root.GetAll();

        var persona = all.FirstOrDefault(p => p.Id == id);
        if (persona is null)
        {
            return NotFound();
        }

        return Ok(persona);
    }

    /// <summary>
    /// Creates or updates a persona based on the payload and optional route id.
    /// </summary>
    [HttpPut("{id:guid?}")]
    public async Task<ActionResult<Persona>> Upsert(Guid? id, [FromBody] Persona persona)
    {
        if (persona is null)
        {
            return BadRequest("Persona payload is required.");
        }

        if (id.HasValue && id.Value == Guid.Empty)
        {
            return BadRequest("Persona id cannot be empty.");
        }

        if (id.HasValue && persona.Id != Guid.Empty && id.Value != persona.Id)
        {
            return BadRequest("Route id does not match payload id.");
        }

        if (string.IsNullOrWhiteSpace(persona.Name))
        {
            return BadRequest("Persona name is required.");
        }

        if (string.IsNullOrWhiteSpace(persona.SystemPrompt))
        {
            return BadRequest("Persona system prompt is required.");
        }

        var personaId = id ?? (persona.Id == Guid.Empty ? Guid.NewGuid() : persona.Id);

        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var existing = await root.GetAllMetadata();
        var isNew = existing.All(item => item.Id != personaId);

        if (isNew)
        {
            logger.LogInformation("Creating persona: {PersonaName}", persona.Name);
        }
        else
        {
            logger.LogInformation("Updating persona: {PersonaId}", personaId);
        }

        await root.AddPersona(personaId, persona.Name, persona.SystemPrompt, persona.Bio);

        var personaGrain = grains.GetGrain<IPersonaGrain>(personaId);
        var updated = await personaGrain.GetPersona();

        if (isNew)
        {
            logger.LogInformation("Persona created: {PersonaId}", personaId);
        }

        return isNew
            ? CreatedAtAction(nameof(GetById), new { id = updated.Id }, updated)
            : AcceptedAtAction(nameof(GetById), new { id = updated.Id }, updated);
    }

    /// <summary>
    /// Removes a persona by id.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (id == Guid.Empty)
        {
            return BadRequest("Persona id cannot be empty.");
        }

        var root = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var exists = await root.HasPersonaId(id);
        if (!exists)
        {
            return NotFound();
        }

        logger.LogInformation("Deleting persona: {PersonaId}", id);
        await root.RemovePersona(id);
        return NoContent();
    }

    /// <summary>
    /// Returns the built-in default persona templates.
    /// </summary>
    [HttpGet("defaults")]
    public async Task<ActionResult<DefaultPersonaTemplate[]>> GetDefaults()
    {
        var templates = await cache.GetOrCreateAsync("persona-defaults", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var dir = Path.Combine(env.ContentRootPath,
                "Services", "Generation", "Prompts", "DefaultCharacters");

            if (!Directory.Exists(dir))
            {
                logger.LogWarning("Default characters directory not found: {Dir}", dir);
                return [];
            }

            var files = Directory.GetFiles(dir, "*.md");
            var results = new List<DefaultPersonaTemplate>();
            foreach (var file in files)
            {
                var content = await System.IO.File.ReadAllTextAsync(file);
                var name = ExtractNameFromMarkdown(content);
                var bio = ExtractBioFromMarkdown(content);
                results.Add(new DefaultPersonaTemplate
                {
                    Name = name,
                    SystemPrompt = content,
                    Bio = bio
                });
            }
            return results.ToArray();
        });

        return Ok(templates ?? []);
    }

    /// <summary>
    /// WebSocket endpoint for streaming persona generation.
    /// Client sends one JSON request, server streams realtime envelope events back.
    /// Request shapes:
    ///   { "type": "generate-bio", "systemPrompt": "..." }
    ///   { "type": "generate", "prompt": "..." }
    /// </summary>
    [HttpGet("ws")]
    public async Task RealtimeGeneration()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var ct = HttpContext.RequestAborted;

        // Read the initial request message (accumulate all frames)
        using var messageStream = new MemoryStream();
        var frameBuffer = new byte[64 * 1024];
        WebSocketReceiveResult receiveResult;
        try
        {
            do
            {
                receiveResult = await socket.ReceiveAsync(frameBuffer, ct);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                    return;
                }
                messageStream.Write(frameBuffer.AsSpan(0, receiveResult.Count));
            } while (!receiveResult.EndOfMessage);
        }
        catch (OperationCanceledException) { return; }

        using var requestDoc = JsonDocument.Parse(messageStream.ToArray());
        var requestType = requestDoc.RootElement.TryGetProperty("type", out var typeProp)
            ? typeProp.GetString()
            : null;

        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var models = await router.GetModelsAsync(ct);
        if (models.Count == 0)
        {
            await SendWsEnvelopeAsync(socket, "persona.generation.error",
                new PersonaGenerationErrorData("No LLM models available."), ct);
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
            return;
        }

        var model = models[0];
        ILlmEndpointGrain endpoint = model.ProviderType switch
        {
            "openrouter" => grains.GetGrain<IOpenRouterEndpointGrain>(model.EndpointProviderGrainId),
            "ollama" => grains.GetGrain<IOllamaEndpointGrain>(model.EndpointProviderGrainId),
            _ => throw new InvalidOperationException($"Unknown provider type: {model.ProviderType}")
        };

        try
        {
            switch (requestType)
            {
                case "generate-bio":
                {
                    if (!requestDoc.RootElement.TryGetProperty("systemPrompt", out var spProp) || spProp.ValueKind != JsonValueKind.String)
                    {
                        await SendWsEnvelopeAsync(socket, "persona.generation.error", new PersonaGenerationErrorData("Missing required field: systemPrompt"), ct);
                        break;
                    }
                    var systemPrompt = spProp.GetString() ?? string.Empty;
                    var accumulated = new StringBuilder();

                    await foreach (var evt in endpoint.GenerateAsync(new LlmGenerationJob
                    {
                        Messages = new List<LlmChatMessage>
                        {
                            new() { Role = "system", Content = BioGenerationSystemPrompt },
                            new() { Role = "user", Content = systemPrompt }
                        },
                        ModelParameters = new LlmModelParameters { Temperature = 0.7 }
                    }, ct))
                    {
                        if (evt.Type != "message") continue;
                        accumulated.Append(evt.Data);
                        await SendWsEnvelopeAsync(socket, "persona.generation.delta",
                            new PersonaGenerationDeltaData(evt.Data), ct);
                    }

                    await SendWsEnvelopeAsync(socket, "persona.generation.completed",
                        new PersonaGenerationCompletedData(null, null, accumulated.ToString().Trim()), ct);
                    break;
                }

                case "generate":
                {
                    if (!requestDoc.RootElement.TryGetProperty("prompt", out var promptProp) || promptProp.ValueKind != JsonValueKind.String)
                    {
                        await SendWsEnvelopeAsync(socket, "persona.generation.error", new PersonaGenerationErrorData("Missing required field: prompt"), ct);
                        break;
                    }
                    var prompt = promptProp.GetString() ?? string.Empty;
                    var generatorPrompt = await cache.GetOrCreateAsync("persona-generator-prompt", async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                        var path = Path.Combine(env.ContentRootPath,
                            "Services", "Generation", "Prompts", "PersonaGenerator.md");
                        if (!System.IO.File.Exists(path))
                        {
                            logger.LogError("PersonaGenerator.md not found at {Path}", path);
                            return null;
                        }
                        return await System.IO.File.ReadAllTextAsync(path, ct);
                    });

                    if (string.IsNullOrWhiteSpace(generatorPrompt))
                    {
                        await SendWsEnvelopeAsync(socket, "persona.generation.error", new PersonaGenerationErrorData("Persona generator unavailable."), ct);
                        break;
                    }

                    var accumulated = new StringBuilder();

                    await foreach (var evt in endpoint.GenerateAsync(new LlmGenerationJob
                    {
                        Messages = new List<LlmChatMessage>
                        {
                            new() { Role = "system", Content = generatorPrompt },
                            new() { Role = "user", Content = prompt }
                        },
                        ModelParameters = new LlmModelParameters { Temperature = 0.85 }
                    }, ct))
                    {
                        if (evt.Type != "message") continue;
                        accumulated.Append(evt.Data);
                        await SendWsEnvelopeAsync(socket, "persona.generation.delta",
                            new PersonaGenerationDeltaData(evt.Data), ct);
                    }

                    var (name, systemPrompt, bio) = ParseGeneratedPersona(accumulated.ToString());
                    await SendWsEnvelopeAsync(socket, "persona.generation.completed",
                        new PersonaGenerationCompletedData(name, systemPrompt, bio), ct);
                    break;
                }

                default:
                    await SendWsEnvelopeAsync(socket, "persona.generation.error",
                        new PersonaGenerationErrorData($"Unknown request type: {requestType}"), ct);
                    break;
            }
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Persona generation WebSocket stream failed");
            if (!ct.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                await SendWsEnvelopeAsync(socket, "persona.generation.error",
                    new PersonaGenerationErrorData("Generation failed."), ct);
            }
        }
        finally
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
        }
    }

    private static readonly JsonSerializerOptions WsJsonOptions = new(JsonSerializerDefaults.Web);
    private long wsSequence;

    private async Task SendWsEnvelopeAsync(WebSocket socket, string type, IPartyRealtimeData data, CancellationToken ct)
    {
        var envelope = new PartyRealtimeEnvelope
        {
            Type = type,
            Sequence = Interlocked.Increment(ref wsSequence),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Data = data,
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, WsJsonOptions);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct);
    }

    private static string ExtractNameFromMarkdown(string content)
    {
        // Match "# Name: Epithet" or "# Name (Epithet)" or just "# Name"
        var match = Regex.Match(content, @"^#\s+([^:\(\n]+?)(?:\s*[:\(]|$)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value.Trim() : "Default Persona";
    }

    private static string ExtractBioFromMarkdown(string content)
    {
        // Try to get the Persona Definition section
        var sectionMatch = Regex.Match(content,
            @"##\s*1\.\s*Persona Definition\s*\n(.*?)(?=\n##\s*2\.)",
            RegexOptions.Singleline);

        string text;
        if (sectionMatch.Success)
        {
            text = sectionMatch.Groups[1].Value.Trim();
            // Strip markdown italic markers
            text = Regex.Replace(text, @"\*([^*]+)\*", "$1");
        }
        else
        {
            // Fall back to content after the H1 heading
            var afterH1 = Regex.Match(content, @"^#[^\n]+\n+(.*?)(?=\n##|\z)", RegexOptions.Singleline);
            text = afterH1.Success ? afterH1.Groups[1].Value.Trim() : content;
        }

        // Take up to 3 sentences
        var sentences = Regex.Matches(text, @"[^.!?]*[.!?]");
        if (sentences.Count > 0)
            return string.Join(" ", sentences.Cast<Match>().Take(3).Select(m => m.Value.Trim()));

        // Fallback: first 300 chars
        return text.Length > 300 ? text[..300].TrimEnd() + "..." : text;
    }

    private static (string Name, string SystemPrompt, string Bio) ParseGeneratedPersona(string raw)
    {
        var systemPrompt = raw.Trim();

        // Extract name from "# System Prompt: Name (The Epithet)"
        var nameMatch = Regex.Match(raw, @"^#\s+System Prompt:\s*(.+?)\s*\(", RegexOptions.Multiline);
        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "Generated Persona";

        var bio = ExtractBioFromMarkdown(raw);
        if (string.IsNullOrWhiteSpace(bio))
            bio = $"A unique AI persona named {name}.";

        return (name, systemPrompt, bio);
    }

    // ─── Intrinsic Stances (ADR 0016, issue #91) ──────────────────────────────────────────

    private const int MaxReasoningLength = 600;

    /// <summary>
    /// Every Intrinsic Stance authored at Persona (library) scope, newest first. The latest
    /// edge per target is flagged <see cref="StanceRecord.IsCurrent"/>; superseded edges
    /// remain for history.
    /// </summary>
    [HttpGet("{personaId:guid}/stances")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<StanceRecord>>> ListIntrinsicStances(
        Guid personaId, CancellationToken ct)
    {
        var stances = await memoryRepository.ListIntrinsicStancesAsync(personaId, ct);
        return Ok(stances);
    }

    /// <summary>
    /// Append an Intrinsic Stance at Persona (library) scope. The edge travels into every
    /// Party the Persona joins. Append-only — prior edges are not mutated.
    /// </summary>
    [HttpPost("{personaId:guid}/stances")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AppendStanceResponse>> AppendIntrinsicStance(
        Guid personaId, [FromBody] AppendStanceRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest("Missing request body.");
        }

        if (double.IsNaN(request.Valence) || request.Valence < -1.0 || request.Valence > 1.0)
        {
            return BadRequest("Valence must be a number in the range -1..1.");
        }

        var reasoning = request.Reasoning?.Trim();
        if (string.IsNullOrEmpty(reasoning))
        {
            return BadRequest("Reasoning is required.");
        }
        if (reasoning.Length > MaxReasoningLength)
        {
            return BadRequest($"Reasoning must be at most {MaxReasoningLength} characters.");
        }

        StanceTargetSpec target;
        switch (request.TargetKind)
        {
            case StanceTargetKind.Participant:
                if (request.TargetPersonaId is not Guid targetPersonaId || targetPersonaId == Guid.Empty)
                {
                    return BadRequest("A Participant Stance requires targetPersonaId (the target Persona's id).");
                }
                if (targetPersonaId == personaId)
                {
                    return BadRequest("Use TargetKind.Self to record a stance toward oneself.");
                }
                target = new StanceTargetSpec(StanceTargetKind.Participant, targetPersonaId, null, null);
                break;

            case StanceTargetKind.Concept:
                var display = request.TargetConceptName?.Trim();
                if (string.IsNullOrEmpty(display))
                {
                    return BadRequest("A Concept Stance requires targetConceptName.");
                }
                if (display.Length > MemoryExtractor.MaxConceptNameChars)
                {
                    return BadRequest(
                        $"Concept name must be at most {MemoryExtractor.MaxConceptNameChars} characters.");
                }
                target = new StanceTargetSpec(
                    StanceTargetKind.Concept, null,
                    MemoryExtractor.NormaliseConceptName(display), display);
                break;

            case StanceTargetKind.Self:
                target = new StanceTargetSpec(StanceTargetKind.Self, null, null, null);
                break;

            default:
                return BadRequest("Unknown stance target kind.");
        }

        var id = await memoryRepository.AppendIntrinsicStanceAsync(
            personaId, target, request.Valence, reasoning, attribution: null, ct);
        return CreatedAtAction(
            nameof(ListIntrinsicStances), new { personaId }, new AppendStanceResponse(id));
    }
}
