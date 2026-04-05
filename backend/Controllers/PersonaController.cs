using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
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

        // Read the initial request message
        var buffer = new byte[64 * 1024];
        WebSocketReceiveResult receiveResult;
        try
        {
            receiveResult = await socket.ReceiveAsync(buffer, ct);
        }
        catch (OperationCanceledException) { return; }

        if (receiveResult.MessageType == WebSocketMessageType.Close)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
            return;
        }

        using var requestDoc = JsonDocument.Parse(buffer.AsMemory(0, receiveResult.Count));
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
        var endpoint = LlmEndpointGrainFactory.GetGrain(grains, model);

        try
        {
            switch (requestType)
            {
                case "generate-bio":
                {
                    var systemPrompt = requestDoc.RootElement.GetProperty("systemPrompt").GetString() ?? string.Empty;
                    var accumulated = new StringBuilder();

                    await foreach (var evt in endpoint.GenerateAsync(new LlmGenerationParams
                    {
                        Model = model.Name,
                        Temperature = 0.7,
                        Messages = new LlmChatMessage[]
                        {
                            new() { Role = "system", Content = BioGenerationSystemPrompt },
                            new() { Role = "user", Content = systemPrompt }
                        }
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
                    var prompt = requestDoc.RootElement.GetProperty("prompt").GetString() ?? string.Empty;
                    var generatorPrompt = await cache.GetOrCreateAsync("persona-generator-prompt", async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                        var path = Path.Combine(env.ContentRootPath,
                            "Services", "Generation", "Prompts", "PersonaGenerator.md");
                        return await System.IO.File.ReadAllTextAsync(path);
                    });

                    var accumulated = new StringBuilder();

                    await foreach (var evt in endpoint.GenerateAsync(new LlmGenerationParams
                    {
                        Model = model.Name,
                        Temperature = 0.85,
                        Messages = new LlmChatMessage[]
                        {
                            new() { Role = "system", Content = generatorPrompt ?? string.Empty },
                            new() { Role = "user", Content = prompt }
                        }
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
}
