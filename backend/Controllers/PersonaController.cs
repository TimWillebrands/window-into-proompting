using Microsoft.AspNetCore.Authorization;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Auth;
using Microsoft.Extensions.Caching.Memory;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.Realtime;
using PartyTown.Services.ResponsePipeline;
using PartyTown.World.Places;

namespace PartyTown.Controllers;

/// <summary>What the generator wrote: the sheet, plus the Card's fields when it filled them in.</summary>
internal sealed record GeneratedPersona(string Name, string SystemPrompt, string Bio, double? Chattiness, double? Impulsivity);

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
        "You write a person's bio from their full description. Other systems read it as plain fact, and their figure is drawn from it alone. " +
        "Third person, plain, 2-3 sentences on one line. " +
        "The first sentence gives first name, age, what they do and where they're from; others often see only its first 140 characters. " +
        "Then how they look in one clause (hair, build, what they wear), always: if the description doesn't say, choose what fits. " +
        "Then how they come across, from what they do. " +
        "Apart from looks, invent nothing: a missing age or origin stays out. " +
        "Only what stays true: nothing about today, lately or what's going on for them. " +
        "No epithets, archetype labels, themes or metaphors, even if the description has them. " +
        "Output ONLY the bio: one line, no formatting, headings or commentary.";

    /// <summary>
    /// Returns all personas currently registered.
    /// </summary>
    [AllowAnonymous]
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
    [AllowAnonymous]
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
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
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

        await root.AddPersona(persona with { Id = personaId });

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
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
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
        await grains.GetGrain<ILlmProviderConfigGrain>(0).SetPersonaRoutesAsync(id, new List<PersonaRoute>());
        return NoContent();
    }

    /// <summary>
    /// Returns the built-in default persona templates.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("defaults")]
    public async Task<ActionResult<DefaultPersonaTemplate[]>> GetDefaults()
    {
        var templates = await cache.GetOrCreateAsync("persona-defaults", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var dir = Path.Combine(env.ContentRootPath,
                "Services", "ResponsePipeline", "Prompts", "DefaultCharacters");

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
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
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

        var llm = new LlmCall(grains.GetGrain<ILlmRouterGrain>(0), logger);
        // Streams one routed call, forwarding each content chunk as a delta frame.
        async Task<string?> StreamToSocketAsync(string system, string user, double temperature)
        {
            try
            {
                var result = await llm.StreamAsync(new LlmGenerationJob
                {
                    Messages = new List<LlmChatMessage>
                    {
                        new() { Role = "system", Content = system },
                        new() { Role = "user", Content = user },
                    },
                    ModelParameters = new LlmModelParameters { Temperature = temperature },
                }, LlmTiers.General, ct, evt => evt.Type == LlmGenerationEvent.ContentChunk
                    ? SendWsEnvelopeAsync(socket, "persona.generation.delta", new PersonaGenerationDeltaData(evt.Data), ct)
                    : Task.CompletedTask);
                return result.Content;
            }
            catch (InvalidOperationException)
            {
                await SendWsEnvelopeAsync(socket, "persona.generation.error",
                    new PersonaGenerationErrorData("No LLM models available."), ct);
                return null;
            }
        }

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
                    var bio = await StreamToSocketAsync(BioGenerationSystemPrompt, spProp.GetString() ?? string.Empty, 0.7);
                    if (bio is null) break;
                    await SendWsEnvelopeAsync(socket, "persona.generation.completed",
                        new PersonaGenerationCompletedData(null, null, bio.Trim()), ct);
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
                        // Source tree in dev (live edits), build output when published.
                        var relative = Path.Combine("Services", "ResponsePipeline", "Prompts", "PersonaGenerator.md");
                        var path = new[] { env.ContentRootPath, AppContext.BaseDirectory }
                            .Select(root => Path.Combine(root, relative))
                            .FirstOrDefault(System.IO.File.Exists) ?? Path.Combine(AppContext.BaseDirectory, relative);
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

                    var generated = await StreamToSocketAsync(generatorPrompt, await BriefAsync(prompt), 0.85);
                    if (generated is null) break;
                    var persona = ParseGeneratedPersona(generated);
                    await SendWsEnvelopeAsync(socket, "persona.generation.completed",
                        new PersonaGenerationCompletedData(persona.Name, persona.SystemPrompt, persona.Bio, persona.Chattiness, persona.Impulsivity), ct);
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

    /// <summary>First sentences of the persona's self-description: "## Who you are" (lived format) or "## 1. Persona Definition" (legacy).</summary>
    internal static string ExtractBioFromMarkdown(string content)
    {
        var section = Regex.Match(content,
            @"^##\s*(?:1\.\s*Persona Definition|Who you are)\s*\n(.*?)(?=\n##\s|\z)",
            RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase);

        var text = section.Success
            ? section.Groups[1].Value
            : Regex.Match(content, @"^#[^\n]+\n+(.*?)(?=\n##|\z)", RegexOptions.Singleline) is { Success: true } afterH1
                ? afterH1.Groups[1].Value
                : content;
        text = Regex.Replace(text, @"^#+[^\n]*\n?", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"\*([^*]+)\*", "$1").Trim();

        var sentences = Regex.Matches(text, @"[^.!?]*[.!?]");
        if (sentences.Count > 0)
            return string.Join(" ", sentences.Cast<Match>().Take(3).Select(m => m.Value.Trim()));
        return text.Length > 300 ? text[..300].TrimEnd() + "..." : text;
    }

    /// <summary>The seed plus who and what the newcomer lands among, in the default Party.</summary>
    private async Task<string> BriefAsync(string seed)
    {
        var cast = await grains.GetGrain<IPartyGrain>(Guid.Empty).GetCastAsync();
        var places = (await grains.GetGrain<IPlacesGrain>(Guid.Empty).GetSnapshotAsync()).Places
            .Where(p => p.Kind == PlaceKinds.Public && !p.Private).Select(p => p.Name).ToList();
        var residents = cast.Where(c => c.Id != Narrator.PersonaId).Select(c => (c.Name, c.Bio)).ToList();
        return PersonaBrief.Compose(seed, Random.Shared, places, residents);
    }

    /// <summary>Name from "# Full Name" (lived format) or "# System Prompt: Name (The Epithet)" (legacy); "## Card" feeds the fields and stays out of the sheet.</summary>
    internal static GeneratedPersona ParseGeneratedPersona(string raw)
    {
        var card = Regex.Match(raw, @"^##\s*Card\s*$(.*)\z", RegexOptions.Singleline | RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var sheet = card.Success ? raw[..card.Index] : raw;
        string? Field(string key) => Regex.Match(card.Groups[1].Value, $@"^\s*{key}\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;
        double? Dial(string key) => double.TryParse(Field(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 1) : null;

        var heading = Regex.Match(sheet, @"^#(?!#)\s*(.+?)\s*$", RegexOptions.Multiline);
        var name = heading.Success
            ? Regex.Replace(Regex.Replace(heading.Groups[1].Value, @"^System Prompt:\s*", "", RegexOptions.IgnoreCase), @"\s*\(.*\)\s*$", "").Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(name)) name = "Generated Persona";

        var bio = Field("bio") ?? ExtractBioFromMarkdown(sheet);
        if (string.IsNullOrWhiteSpace(bio))
            bio = $"A unique AI persona named {name}.";

        return new GeneratedPersona(name, sheet.Trim(), bio, Dial("chattiness"), Dial("impulsivity"));
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
    [Authorize(Policy = ClerkAuth.AdminPolicy)]
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
