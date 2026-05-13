using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Grains;
using PartyTown.Model;
using PartyTown.Services.Generation;
using PartyTown.Services.Realtime;

namespace PartyTown.Controllers;

[ApiController]
[Route("[controller]")]
/// <summary>
/// HTTP + WebSocket API for importing Gemini AI Studio chat exports as chatrooms.
///
/// Flow (driven by the frontend Import app):
///   1. POST /api/Import/extract-personas — extract persona stubs from the export's
///      systemInstruction text. One LLM call, ~5-10s.
///   2. GET  /api/Import/classify-ws       — open WebSocket, send chunks + roster,
///      stream back per-chunk segmentation results. Concurrency capped at 5
///      in-flight LLM calls.
///   3. POST /api/Import/commit            — atomic: create new personas, create
///      ChatGroup, set participants, bulk-import all messages in a single grain
///      event.
/// </summary>
public sealed class ImportController(
    IGrainFactory grains,
    ImportService importService,
    ILogger<ImportController> logger) : ControllerBase
{
    private const int MaxConcurrentClassifications = 5;

    [HttpPost("extract-personas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExtractPersonasResponse>> ExtractPersonas(
        [FromBody] ExtractPersonasRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SystemInstruction))
        {
            return BadRequest("systemInstruction is required.");
        }

        var personas = await importService.ExtractPersonasAsync(
            request.SystemInstruction,
            request.SampleChunks ?? [],
            cancellationToken);
        return Ok(new ExtractPersonasResponse(personas.ToList()));
    }

    /// <summary>
    /// WebSocket endpoint. Client opens connection, sends a single JSON message of
    /// shape <see cref="ClassifyRequest"/>, then reads streamed
    /// <see cref="ClassifyEnvelope"/> messages (one progress per completed chunk,
    /// then one completed). Closes after completion or on cancellation.
    /// </summary>
    [HttpGet("classify-ws")]
    public async Task ClassifyWs()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var ct = HttpContext.RequestAborted;

        ClassifyRequest? request;
        try
        {
            request = await ReadRequestAsync(socket, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read classify request from socket");
            await SendEnvelopeAsync(socket, "import.classify.error",
                new { error = "Bad request payload" }, ct);
            await CloseAsync(socket, ct);
            return;
        }

        if (request is null || request.Chunks is null || request.Chunks.Count == 0)
        {
            await SendEnvelopeAsync(socket, "import.classify.error",
                new { error = "chunks is required" }, ct);
            await CloseAsync(socket, ct);
            return;
        }

        var roster = request.Roster ?? [];
        var total = request.Chunks.Count;
        var completed = 0;

        using var sem = new SemaphoreSlim(MaxConcurrentClassifications);
        var tasks = request.Chunks.Select(async chunk =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var classification = await importService.ClassifyChunkAsync(
                    chunk.Text, chunk.Role, roster, ct);

                var done = Interlocked.Increment(ref completed);
                await SendEnvelopeAsync(socket, "import.classify.progress", new
                {
                    chunkId = chunk.Id,
                    completed = done,
                    total,
                    segments = classification.Segments
                }, ct);
            }
            finally
            {
                sem.Release();
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(tasks);
            await SendEnvelopeAsync(socket, "import.classify.completed",
                new { total }, ct);
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Classify stream failed");
            if (socket.State == WebSocketState.Open)
            {
                await SendEnvelopeAsync(socket, "import.classify.error",
                    new { error = ex.Message }, ct);
            }
        }
        finally
        {
            await CloseAsync(socket, ct);
        }
    }

    [HttpPost("commit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CommitImportResponse>> Commit(
        [FromBody] CommitImportRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PartyId == Guid.Empty)
            return BadRequest("partyId is required.");
        if (string.IsNullOrWhiteSpace(request.ChatGroupName))
            return BadRequest("chatGroupName is required.");
        if (request.Messages is null || request.Messages.Count == 0)
            return BadRequest("messages must contain at least one entry.");

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        if (!await root.HasPartyId(request.PartyId))
            return NotFound($"Party {request.PartyId} not found.");

        // Step 1: mint new personas (only those marked IsNew=true with no PreExistingId).
        // Map placeholder ids → minted GUIDs so the messages can be rewritten in step 4.
        var personaRoot = grains.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var idRemap = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var mintedIds = new List<Guid>();
        foreach (var stub in request.Personas ?? [])
        {
            if (!stub.IsNew && stub.PreExistingId is { } existing && existing != Guid.Empty)
            {
                if (!string.IsNullOrEmpty(stub.PlaceholderId))
                    idRemap[stub.PlaceholderId] = existing;
                continue;
            }
            var personaId = Guid.NewGuid();
            await personaRoot.AddPersona(personaId, stub.Name, stub.SystemPrompt, stub.Bio);
            mintedIds.Add(personaId);
            if (!string.IsNullOrEmpty(stub.PlaceholderId))
                idRemap[stub.PlaceholderId] = personaId;
        }

        // Step 2: create the chat group inside the party.
        var partyGrain = grains.GetGrain<IPartyGrain>(request.PartyId);
        var chatGroup = await partyGrain.CreateChatGroup(request.ChatGroupName.Trim(), request.Scenario);
        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(chatGroup.Id);

        // Step 3: replace per-room participants with imported personas + (optionally) user.
        // We deliberately scope participants to this chat group instead of polluting the
        // party-wide participant list — different rooms can host different casts.
        var participants = new List<PartyParticipant>();
        foreach (var stub in request.Personas ?? [])
        {
            if (!string.IsNullOrEmpty(stub.PlaceholderId) && idRemap.TryGetValue(stub.PlaceholderId, out var pid))
            {
                participants.Add(new PartyParticipant
                {
                    Id = pid,
                    Name = stub.Name,
                    IsUser = false
                });
            }
        }
        if (request.UserParticipant is { } userP && userP.Id != Guid.Empty)
        {
            participants.Add(new PartyParticipant
            {
                Id = userP.Id,
                Name = string.IsNullOrWhiteSpace(userP.Name) ? "You" : userP.Name,
                IsUser = true
            });
        }
        await chatGroupGrain.SetParticipantsAsync(participants);

        // Step 4: bulk-import messages. Rewrite each message's senderId from
        // placeholder → minted GUID via idRemap. Drop messages whose placeholder didn't
        // resolve (caller bug; safer to skip than to attribute to Guid.Empty).
        var imported = new List<ImportedMessage>(request.Messages.Count);
        foreach (var m in request.Messages)
        {
            if (!idRemap.TryGetValue(m.SenderPlaceholderId ?? string.Empty, out var senderId))
            {
                logger.LogWarning(
                    "Import: dropping message with unresolved sender placeholder '{Placeholder}'",
                    m.SenderPlaceholderId);
                continue;
            }
            imported.Add(new ImportedMessage
            {
                SenderId = senderId,
                SenderType = string.IsNullOrWhiteSpace(m.SenderType) ? "assistant" : m.SenderType,
                Content = m.Content ?? string.Empty,
                SendAt = m.SendAt,
                Kind = m.Kind,
                ChatGroupId = chatGroup.Id,
                Metadata = null
            });
        }
        var written = await chatGroupGrain.ImportMessagesAsync(imported, cancellationToken);

        logger.LogInformation(
            "Import committed: party={PartyId} chatGroup={ChatGroupId} personas={NewPersonas} messages={WrittenMessages}",
            request.PartyId, chatGroup.Id, mintedIds.Count, written);

        return Ok(new CommitImportResponse(chatGroup.Id, mintedIds, written));
    }

    private static async Task<ClassifyRequest?> ReadRequestAsync(WebSocket socket, CancellationToken ct)
    {
        using var messageStream = new MemoryStream();
        var frameBuffer = new byte[64 * 1024];
        WebSocketReceiveResult receiveResult;
        do
        {
            receiveResult = await socket.ReceiveAsync(frameBuffer, ct);
            if (receiveResult.MessageType == WebSocketMessageType.Close) return null;
            messageStream.Write(frameBuffer.AsSpan(0, receiveResult.Count));
        } while (!receiveResult.EndOfMessage);

        return JsonSerializer.Deserialize<ClassifyRequest>(messageStream.ToArray(), WsJsonOptions);
    }

    private async Task SendEnvelopeAsync(WebSocket socket, string type, object data, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open) return;
        var envelope = new
        {
            type,
            sequence = Interlocked.Increment(ref _wsSequence),
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            data
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, WsJsonOptions);
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct);
    }

    private static async Task CloseAsync(WebSocket socket, CancellationToken ct)
    {
        if (socket.State == WebSocketState.Open)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct); }
            catch { /* socket may already be closing */ }
        }
    }

    private static readonly JsonSerializerOptions WsJsonOptions = new(JsonSerializerDefaults.Web);
    private long _wsSequence;
}

// ── Wire DTOs ──

public sealed record class ExtractPersonasRequest
{
    [JsonPropertyName("systemInstruction")]
    public string SystemInstruction { get; init; } = string.Empty;

    /// <summary>Optional transcript chunks (in order) that the extractor will use as
    /// grounding context alongside the systemInstruction. Caller is responsible for
    /// selecting an informative subset; the service applies its own char budget on
    /// top of whatever is passed in.</summary>
    [JsonPropertyName("sampleChunks")]
    public List<ImportSampleChunk>? SampleChunks { get; init; }
}

public sealed record class ExtractPersonasResponse(
    [property: JsonPropertyName("personas")] List<ExtractedPersona> Personas);

public sealed record class ClassifyRequest
{
    [JsonPropertyName("chunks")]
    public List<ClassifyChunk> Chunks { get; init; } = [];

    [JsonPropertyName("roster")]
    public List<PersonaRosterEntry> Roster { get; init; } = [];
}

public sealed record class ClassifyChunk
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "model";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>WebSocket envelope used to wrap progress/completed/error data frames.
/// Kept as an inline anonymous type at the call site rather than a record, since the
/// payload shape varies per type.</summary>
public sealed record class ClassifyEnvelope;

public sealed record class CommitImportRequest
{
    [JsonPropertyName("partyId")]
    public Guid PartyId { get; init; }

    [JsonPropertyName("chatGroupName")]
    public string ChatGroupName { get; init; } = string.Empty;

    [JsonPropertyName("scenario")]
    public string? Scenario { get; init; }

    [JsonPropertyName("personas")]
    public List<PersonaStubForCommit> Personas { get; init; } = [];

    [JsonPropertyName("messages")]
    public List<ImportedMessageForCommit> Messages { get; init; } = [];

    [JsonPropertyName("userParticipant")]
    public UserParticipantStub? UserParticipant { get; init; }
}

public sealed record class PersonaStubForCommit
{
    /// <summary>Client-assigned tag used to bind messages to personas before the
    /// real Guid is minted server-side. e.g. "p1", "narrator". Required.</summary>
    [JsonPropertyName("placeholderId")]
    public string PlaceholderId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("systemPrompt")]
    public string SystemPrompt { get; init; } = string.Empty;

    [JsonPropertyName("bio")]
    public string? Bio { get; init; }

    /// <summary>True if a new persona grain should be created. False (with PreExistingId)
    /// if the import re-uses an existing persona.</summary>
    [JsonPropertyName("isNew")]
    public bool IsNew { get; init; } = true;

    [JsonPropertyName("preExistingId")]
    public Guid? PreExistingId { get; init; }
}

public sealed record class ImportedMessageForCommit
{
    [JsonPropertyName("senderPlaceholderId")]
    public string? SenderPlaceholderId { get; init; }

    [JsonPropertyName("senderType")]
    public string SenderType { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("sendAt")]
    public long SendAt { get; init; }

    /// <summary>Optional segment kind: "dialogue" / "action" / "thought" / "narration" /
    /// "ooc" / "emote". Null = normal message.</summary>
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }
}

public sealed record class UserParticipantStub
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed record class CommitImportResponse(
    [property: JsonPropertyName("chatGroupId")] Guid ChatGroupId,
    [property: JsonPropertyName("createdPersonaIds")] List<Guid> CreatedPersonaIds,
    [property: JsonPropertyName("messagesWritten")] int MessagesWritten);
