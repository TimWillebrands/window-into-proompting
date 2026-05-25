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
///      systemInstruction text + a transcript sample. Map-reduce: parallel "who appears
///      here?" mention extraction across transcript windows, then one synthesis call.
///      ~15-30s for a typical export.
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
    /// Collapse 2+ persona stubs (the user multi-selects them in the review-personas
    /// UI) into one canonical persona via a single LLM synthesis call.
    /// </summary>
    [HttpPost("merge-personas")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<MergePersonasResponse>> MergePersonas(
        [FromBody] MergePersonasRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Personas is null || request.Personas.Count < 2)
        {
            return BadRequest("merge requires at least 2 personas.");
        }

        var merged = await importService.MergePersonasAsync(request.Personas, cancellationToken);
        return Ok(new MergePersonasResponse(merged));
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

    /// <summary>
    /// WebSocket endpoint for the per-msg identify pipeline.
    /// Client sends one <see cref="IdentifyRequest"/>; server streams envelopes:
    ///   • import.identify.map_progress   — per msg, as each map call completes
    ///   • import.identify.roster_sync    — once, after all maps; canonical roster + links
    ///   • import.identify.reduce_progress — per char, as each detail synth completes
    ///   • import.identify.completed      — terminal
    ///   • import.identify.error          — terminal on failure
    ///
    /// Phases are serialized server-side (all maps → roster_sync → all reduces) so the
    /// client never sees a reduce_progress for a char that hasn't appeared in roster_sync.
    /// </summary>
    [HttpGet("identify-ws")]
    public async Task IdentifyWs()
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        var ct = HttpContext.RequestAborted;

        IdentifyRequest? request;
        try
        {
            request = await ReadJsonRequestAsync<IdentifyRequest>(socket, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read identify request from socket");
            await SendEnvelopeAsync(socket, "import.identify.error",
                new { error = "Bad request payload" }, ct);
            await CloseAsync(socket, ct);
            return;
        }

        if (request is null || request.Msgs is null || request.Msgs.Count == 0)
        {
            await SendEnvelopeAsync(socket, "import.identify.error",
                new { error = "msgs is required" }, ct);
            await CloseAsync(socket, ct);
            return;
        }

        var sysInstruction = request.SystemInstructionText ?? string.Empty;
        var total = request.Msgs.Count;
        var completed = 0;
        var mentionsByMsg = new System.Collections.Concurrent.ConcurrentDictionary<string, List<MentionWithIdDto>>();

        // Phase 1: parallel per-msg map.
        using var mapSem = new SemaphoreSlim(MaxConcurrentClassifications);
        var mapTasks = request.Msgs.Select(async msg =>
        {
            await mapSem.WaitAsync(ct);
            try
            {
                var result = await importService.IdentifyMentionsForMsgAsync(msg.Text, msg.Role, ct);
                var raw = result?.Mentions ?? [];
                var indexed = raw
                    .Select((m, i) => new MentionWithIdDto
                    {
                        MentionId = $"{msg.Id}:{i}",
                        Name = m.Name,
                        Evidence = m.Evidence,
                        RoleHint = m.RoleHint,
                    })
                    .ToList();
                mentionsByMsg[msg.Id] = indexed;

                var done = Interlocked.Increment(ref completed);
                await SendEnvelopeAsync(socket, "import.identify.map_progress", new
                {
                    msgId = msg.Id,
                    completed = done,
                    total,
                    mentions = indexed,
                }, ct);
            }
            finally
            {
                mapSem.Release();
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(mapTasks);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Identify map phase failed");
            if (socket.State == WebSocketState.Open)
            {
                await SendEnvelopeAsync(socket, "import.identify.error",
                    new { error = ex.Message }, ct);
            }
            await CloseAsync(socket, ct);
            return;
        }

        // Phase 2: roster merge (single LLM call).
        var groups = mentionsByMsg
            .SelectMany(kv => kv.Value.Select(m => new { MsgId = kv.Key, Mention = m }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Mention.Name))
            .GroupBy(x => x.Mention.Name.Trim().ToLowerInvariant())
            .Select(g => new MentionGroupDto
            {
                DisplayName = g.First().Mention.Name.Trim(),
                Count = g.Count(),
                Refs = g
                    .Select(x => new MentionRefDto
                    {
                        MsgId = x.MsgId,
                        MentionId = x.Mention.MentionId,
                        Evidence = x.Mention.Evidence,
                        RoleHint = x.Mention.RoleHint,
                    })
                    .ToList(),
                RoleHints = g
                    .Select(x => x.Mention.RoleHint)
                    .Where(h => !string.IsNullOrWhiteSpace(h))
                    .Select(h => h!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList(),
            })
            .OrderByDescending(g => g.Count)
            .ToList();

        if (groups.Count == 0)
        {
            await SendEnvelopeAsync(socket, "import.identify.roster_sync",
                new { characters = Array.Empty<object>(), links = Array.Empty<object>() }, ct);
            await SendEnvelopeAsync(socket, "import.identify.completed", new { }, ct);
            await CloseAsync(socket, ct);
            return;
        }

        RosterMergeResult? merged;
        try
        {
            merged = await importService.MergeRosterAsync(groups, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Roster merge failed");
            if (socket.State == WebSocketState.Open)
            {
                await SendEnvelopeAsync(socket, "import.identify.error",
                    new { error = ex.Message }, ct);
            }
            await CloseAsync(socket, ct);
            return;
        }

        // Fallback: each unique observed name → its own char so the UI doesn't stall.
        if (merged?.Characters is null || merged.Characters.Count == 0)
        {
            merged = new RosterMergeResult
            {
                Characters = groups
                    .Select((g, i) => new RosterCharacter
                    {
                        Id = $"char-{i + 1}",
                        PrimaryName = g.DisplayName,
                        Names = new List<string> { g.DisplayName },
                        Archetype = g.RoleHints.FirstOrDefault(),
                    })
                    .ToList(),
            };
        }

        // Build mention → char-id link map by case-insensitive name match.
        var nameToChar = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in merged.Characters)
        {
            foreach (var n in c.Names ?? [])
            {
                if (!string.IsNullOrWhiteSpace(n))
                {
                    nameToChar[n.Trim()] = c.Id;
                }
            }
            // Always include primary as a key, even if not in names[].
            if (!string.IsNullOrWhiteSpace(c.PrimaryName))
            {
                nameToChar.TryAdd(c.PrimaryName.Trim(), c.Id);
            }
        }
        // Fallback char for unmatched mentions (shouldn't happen if LLM follows rules).
        var fallbackCharId = merged.Characters[0].Id;

        var links = new List<object>();
        foreach (var g in groups)
        {
            var charId = nameToChar.GetValueOrDefault(g.DisplayName) ?? fallbackCharId;
            foreach (var r in g.Refs)
            {
                links.Add(new
                {
                    msgId = r.MsgId,
                    mentionId = r.MentionId,
                    charId,
                });
            }
        }

        await SendEnvelopeAsync(socket, "import.identify.roster_sync", new
        {
            characters = merged.Characters,
            links,
        }, ct);

        // Phase 3: per-char detail synth in parallel.
        // Collect evidence per char by walking groups whose displayName lives in char.names.
        var evidenceByChar = new Dictionary<string, List<string>>();
        foreach (var c in merged.Characters)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in c.Names ?? [])
            {
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n.Trim());
            }
            if (!string.IsNullOrWhiteSpace(c.PrimaryName)) names.Add(c.PrimaryName.Trim());

            var ev = groups
                .Where(g => names.Contains(g.DisplayName))
                .SelectMany(g => g.Refs.Select(r => r.Evidence))
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Distinct()
                .Take(8)
                .ToList();
            evidenceByChar[c.Id] = ev;
        }

        using var reduceSem = new SemaphoreSlim(MaxConcurrentClassifications);
        var reduceTasks = merged.Characters.Select(async c =>
        {
            await reduceSem.WaitAsync(ct);
            try
            {
                var detail = await importService.SynthesizeCharDetailAsync(
                    c.PrimaryName,
                    c.Names ?? new List<string> { c.PrimaryName },
                    evidenceByChar[c.Id],
                    c.Archetype,
                    sysInstruction,
                    "both",
                    ct);
                await SendEnvelopeAsync(socket, "import.identify.reduce_progress", new
                {
                    characterId = c.Id,
                    prompt = detail?.Prompt ?? string.Empty,
                    bio = detail?.Bio,
                }, ct);
            }
            finally
            {
                reduceSem.Release();
            }
        }).ToArray();

        try
        {
            await Task.WhenAll(reduceTasks);
            await SendEnvelopeAsync(socket, "import.identify.completed", new { }, ct);
        }
        catch (OperationCanceledException) { /* client disconnected */ }
        catch (Exception ex)
        {
            logger.LogError(ex, "Identify reduce phase failed");
            if (socket.State == WebSocketState.Open)
            {
                await SendEnvelopeAsync(socket, "import.identify.error",
                    new { error = ex.Message }, ct);
            }
        }
        finally
        {
            await CloseAsync(socket, ct);
        }
    }

    /// <summary>
    /// Single-shot regeneration of one character's prompt and/or bio. Used by the
    /// CharDetailWindow's "Regen prompt" / "Regen bio" buttons.
    /// </summary>
    [HttpPost("regenerate-char-detail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegenerateCharDetailResponse>> RegenerateCharDetail(
        [FromBody] RegenerateCharDetailRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PrimaryName))
            return BadRequest("primaryName is required.");
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? "both" : request.Mode;
        if (mode is not "prompt" and not "bio" and not "both")
            return BadRequest("mode must be 'prompt', 'bio', or 'both'.");

        var detail = await importService.SynthesizeCharDetailAsync(
            request.PrimaryName,
            request.Names ?? [],
            request.Evidence ?? [],
            request.Archetype,
            request.SystemInstructionText ?? string.Empty,
            mode,
            cancellationToken);

        return Ok(new RegenerateCharDetailResponse(detail?.Prompt, detail?.Bio));
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

    private static Task<ClassifyRequest?> ReadRequestAsync(WebSocket socket, CancellationToken ct)
        => ReadJsonRequestAsync<ClassifyRequest>(socket, ct);

    private static async Task<T?> ReadJsonRequestAsync<T>(WebSocket socket, CancellationToken ct)
    {
        using var messageStream = new MemoryStream();
        var frameBuffer = new byte[64 * 1024];
        WebSocketReceiveResult receiveResult;
        do
        {
            receiveResult = await socket.ReceiveAsync(frameBuffer, ct);
            if (receiveResult.MessageType == WebSocketMessageType.Close) return default;
            messageStream.Write(frameBuffer.AsSpan(0, receiveResult.Count));
        } while (!receiveResult.EndOfMessage);

        return JsonSerializer.Deserialize<T>(messageStream.ToArray(), WsJsonOptions);
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

public sealed record class MergePersonasRequest
{
    [JsonPropertyName("personas")]
    public List<ExtractedPersona>? Personas { get; init; }
}

public sealed record class MergePersonasResponse(
    [property: JsonPropertyName("persona")] ExtractedPersona Persona);

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

public sealed record class IdentifyRequest
{
    [JsonPropertyName("systemInstructionText")]
    public string SystemInstructionText { get; init; } = string.Empty;

    [JsonPropertyName("msgs")]
    public List<IdentifyMsgInput> Msgs { get; init; } = [];
}

public sealed record class IdentifyMsgInput
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

/// <summary>One mention with its server-assigned stable id, sent on each
/// import.identify.map_progress envelope.</summary>
public sealed record class MentionWithIdDto
{
    [JsonPropertyName("mentionId")]
    public string MentionId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("evidence")]
    public string Evidence { get; init; } = string.Empty;

    [JsonPropertyName("roleHint")]
    public string? RoleHint { get; init; }
}

public sealed record class RegenerateCharDetailRequest
{
    [JsonPropertyName("primaryName")]
    public string PrimaryName { get; init; } = string.Empty;

    [JsonPropertyName("names")]
    public List<string>? Names { get; init; }

    [JsonPropertyName("evidence")]
    public List<string>? Evidence { get; init; }

    [JsonPropertyName("archetype")]
    public string? Archetype { get; init; }

    [JsonPropertyName("systemInstructionText")]
    public string? SystemInstructionText { get; init; }

    /// <summary>"prompt" | "bio" | "both". Default "both".</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = "both";
}

public sealed record class RegenerateCharDetailResponse(
    [property: JsonPropertyName("prompt")] string? Prompt,
    [property: JsonPropertyName("bio")] string? Bio);
