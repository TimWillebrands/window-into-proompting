using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Papertrail;
using PartyTown.Services.Realtime;

namespace PartyTown.Controllers;

/// <summary>
/// Provides endpoints for creating, querying, and interacting with party conversations.
/// </summary>
/// <remarks>
/// This controller coordinates Orleans grains for party state and exposes both request/response
/// endpoints for live updates and interaction.
/// </remarks>
[ApiController]
[Route("[controller]")]
public sealed class PartyController(
    IGrainFactory grains,
    IPartyRealtimeHub realtimeHub,
    IMemoryCache cache,
    ILogger<PartyController> logger) : ControllerBase
{
    /// <summary>
    /// Retrieves all known parties.
    /// </summary>
    /// <returns>
    /// A <see cref="PartyInfo"/> array wrapped in <c>200 OK</c>.
    /// </returns>
    [HttpGet]
    public async Task<ActionResult<PartyInfo[]>> GetAll()
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        var parties = await root.GetAll();
        return Ok(parties);
    }

    /// <summary>
    /// Creates a new party and initializes its participant list.
    /// </summary>
    /// <param name="request">
    /// The party creation payload containing the party name and optional user participant information.
    /// </param>
    /// <returns>
    /// <c>201 Created</c> with the newly created <see cref="PartyInfoFull"/> when successful;
    /// otherwise <c>400 Bad Request</c> if the party name is missing or whitespace.
    /// </returns>
    [HttpPost("create")]
    public async Task<ActionResult<PartyInfo>> Create([FromBody] CreatePartyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PartyName))
        {
            return BadRequest("Invalid party name");
        }

        logger.LogInformation("Creating party: {PartyName}", request.PartyName.Trim());

        var partyId = Guid.NewGuid();
        var party = new PartyInfo
        {
            Id = partyId,
            Name = request.PartyName.Trim()
        };

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await root.AddParty(party);

        var participantList = await BuildDefaultParticipants(request.UserId, request.UserName);
        var partyGrain = grains.GetGrain<IPartyGrain>(partyId);
        await partyGrain.SetParticipants(participantList);

        logger.LogInformation("Party created: {PartyId}", partyId);

        return CreatedAtAction(nameof(GetById), new { id = partyId }, new PartyInfo
        {
            Id = partyId,
            Name = party.Name,
            Participants = participantList
        });
    }

    /// <summary>
    /// Retrieves the full party payload for the specified party identifier.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <returns>
    /// <c>200 OK</c> containing the party, available models, and persona metadata;
    /// <c>400 Bad Request</c> if <paramref name="id"/> is empty; or <c>404 Not Found</c>
    /// when the party does not exist.
    /// </returns>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PartyDetails>> GetById(Guid id)
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var party = await grains.GetGrain<IPartyGrain>(id).GetParty();
        var personas = await grains.GetGrain<IPersonaRootGrain>(Guid.Empty).GetAllMetadata();

        return Ok(new PartyDetails(party, personas));
    }

    /// <summary>
    /// Ensures the specified party has a default participant set.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <returns>
    /// <c>200 OK</c> with the current party state. If the party has no participants,
    /// default persona participants are created first.
    /// Returns <c>400 Bad Request</c> when <paramref name="id"/> is empty, or
    /// <c>404 Not Found</c> when the party does not exist.
    /// </returns>
    [HttpPost("{id:guid}/reset-participants")]
    public async Task<ActionResult<PartyInfo>> ResetParticipants(Guid id)
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var grain = grains.GetGrain<IPartyGrain>(id);
        var current = await grain.GetParty();
        if (current.Participants.Count == 0)
        {
            current.Participants = await BuildDefaultParticipants(null, null);
            await grain.SetParticipants(current.Participants);
        }

        return Ok(current);
    }

    [HttpPut("{id:guid}/participants")]
    public async Task<ActionResult<PartyInfo>> SetParticipants(Guid id, [FromBody] UpdatePartyParticipantsRequest request)
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var participants = (request.Participants ?? [])
            .Where(participant => participant.Id != Guid.Empty)
            .GroupBy(participant => participant.Id)
            .Select(group =>
            {
                var participant = group.First();
                var fallbackName = participant.Id.ToString();
                return new PartyParticipant
                {
                    Id = participant.Id,
                    Name = string.IsNullOrWhiteSpace(participant.Name)
                        ? fallbackName
                        : participant.Name.Trim(),
                    IsUser = participant.IsUser
                };
            })
            .ToList();

        var grain = grains.GetGrain<IPartyGrain>(id);
        await grain.SetParticipants(participants);
        var updated = await grain.GetParty();
        return Ok(updated);
    }

    /// <summary>
    /// Returns the participant list for a single chat group (the per-room cast,
    /// including the user-persona marked with <see cref="PartyParticipant.IsUser"/>).
    /// </summary>
    [HttpGet("{id:guid}/chat-groups/{chatGroupId:guid}/participants")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<PartyParticipant>>> GetChatGroupParticipants(Guid id, Guid chatGroupId)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var partyGrain = grains.GetGrain<IPartyGrain>(id);
        var chatGroups = await partyGrain.GetChatGroups();
        if (!chatGroups.Any(g => g.Id == chatGroupId))
        {
            return NotFound();
        }

        var participants = await grains.GetGrain<IChatGroupGrain>(chatGroupId).GetParticipantsAsync();
        return Ok(participants);
    }

    /// <summary>
    /// Replaces the participant list for a single chat group. Other chat groups in the
    /// same party are not affected. The party-level participant list (used to seed new
    /// chat groups) is also untouched.
    /// </summary>
    [HttpPut("{id:guid}/chat-groups/{chatGroupId:guid}/participants")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<PartyParticipant>>> SetChatGroupParticipants(
        Guid id,
        Guid chatGroupId,
        [FromBody] UpdatePartyParticipantsRequest request)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var partyGrain = grains.GetGrain<IPartyGrain>(id);
        var chatGroups = await partyGrain.GetChatGroups();
        if (!chatGroups.Any(g => g.Id == chatGroupId))
        {
            return NotFound();
        }

        var participants = (request.Participants ?? [])
            .Where(participant => participant.Id != Guid.Empty)
            .GroupBy(participant => participant.Id)
            .Select(group =>
            {
                var participant = group.First();
                var fallbackName = participant.Id.ToString();
                return new PartyParticipant
                {
                    Id = participant.Id,
                    Name = string.IsNullOrWhiteSpace(participant.Name)
                        ? fallbackName
                        : participant.Name.Trim(),
                    IsUser = participant.IsUser
                };
            })
            .ToList();

        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(chatGroupId);
        await chatGroupGrain.SetParticipantsAsync(participants);
        var updated = await chatGroupGrain.GetParticipantsAsync();
        return Ok(updated);
    }

    /// <summary>
    /// Sends a user prompt into the party conversation.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <param name="request">
    /// The prompt request containing prompt text, sender metadata, and model/provider selection.
    /// </param>
    /// <returns>
    /// <c>202 Accepted</c> when prompt processing is queued; <c>400 Bad Request</c> when
    /// required fields (prompt, model, or provider) are missing or whitespace.
    /// </returns>
    [HttpPost("{id:guid}/prompt")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Prompt(Guid id, [FromBody] PromptRequest request)
    {
        if (request.ChatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return BadRequest("Invalid prompt");
        }

        logger.LogInformation("Sending prompt to party {PartyId}, message length: {Length}", id, request.Prompt.Length);

        var senderId = request.SenderId ?? Guid.NewGuid();

        // Route directly to ChatGroupGrain for parallel persona fan-out
        await grains.GetGrain<IChatGroupGrain>(request.ChatGroupId)
            .SendNewMessageAsync(senderId, request.Prompt);

        return Accepted();
    }

    /// <summary>
    /// Requests the party to generate the next response without adding a new user prompt.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <param name="request">
    /// The continuation request containing sender metadata and model/provider selection.
    /// </param>
    /// <returns>
    /// <c>202 Accepted</c> when continuation processing is queued; <c>400 Bad Request</c>
    /// when model or provider is missing or whitespace.
    /// </returns>
    [HttpPost("{id:guid}/proceed")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Proceed(Guid id, [FromBody] ProceedRequest request)
    {
        if (request.ChatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }


        // Create a synthetic trigger message to fan out to personas
        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(request.ChatGroupId);
        var messages = await chatGroupGrain.GetMessagesAsync();
        var lastMessage = messages.LastOrDefault();

        if (lastMessage is not null)
        {
            await chatGroupGrain.NotifyAllParticipantsAsync(lastMessage, HttpContext.RequestAborted);
        }

        return Accepted();
    }

    /// <summary>
    /// Cancels all active AI generations for the specified party.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <returns>
    /// <c>204 No Content</c> when cancellation is signalled; <c>400 Bad Request</c>
    /// when <paramref name="id"/> is empty.
    /// </returns>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> CancelGenerations(Guid id)
    {
        await grains.GetGrain<IPartyGrain>(id).CancelAllGenerations();
        return NoContent();
    }

    /// <summary>
    /// Deletes a single message from the party transcript.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <param name="messageId">The zero-based message identifier within the party timeline.</param>
    /// <returns>
    /// <c>204 No Content</c> when deletion succeeds, or <c>400 Bad Request</c>
    /// when <paramref name="messageId"/> is negative.
    /// </returns>
    [HttpDelete("{id:guid}/chat-groups/{chatGroupId:guid}/messages/{messageId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> DeleteMessage(Guid id, Guid chatGroupId, int messageId)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        if (messageId < 0)
        {
            return BadRequest("Invalid message id");
        }

        await grains.GetGrain<IChatGroupGrain>(chatGroupId).DeleteMessageAsync(messageId);
        return NoContent();
    }

    /// <summary>
    /// Deletes all messages strictly after the specified message identifier.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <param name="messageId">The anchor message identifier after which messages are removed.</param>
    /// <returns>
    /// <c>204 No Content</c> when deletion succeeds, or <c>400 Bad Request</c>
    /// when <paramref name="messageId"/> is negative.
    /// </returns>
    [HttpDelete("{id:guid}/chat-groups/{chatGroupId:guid}/messages-after/{messageId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> DeleteMessagesAfter(Guid id, Guid chatGroupId, int messageId)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        if (messageId < 0)
        {
            return BadRequest("Invalid message id");
        }

        await grains.GetGrain<IChatGroupGrain>(chatGroupId).DeleteMessagesAfterAsync(messageId);
        return NoContent();
    }

    /// <summary>
    /// Rewinds the conversation to a specific message and requests a new continuation.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <param name="messageId">The message identifier used as the rewind boundary.</param>
    /// <param name="request">
    /// The reprompt request with sender metadata and model/provider selection.
    /// </param>
    /// <returns>
    /// <c>202 Accepted</c> when reprompt processing is queued; <c>400 Bad Request</c>
    /// when model or provider is missing or whitespace.
    /// </returns>
    /// <remarks>
    /// This endpoint first removes all messages after <paramref name="messageId"/>, then calls
    /// the same continuation flow used by <see cref="Proceed(Guid, ProceedRequest)"/>.
    /// </remarks>
    [HttpPost("{id:guid}/reprompt/{messageId:int}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Reprompt(Guid id, int messageId, [FromBody] RepromptRequest request)
    {
        if (request.ChatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }


        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(request.ChatGroupId);
        await chatGroupGrain.DeleteMessagesAfterAsync(messageId);

        // Fan out to personas after truncation
        var messages = await chatGroupGrain.GetMessagesAsync();
        var lastMessage = messages.LastOrDefault();
        if (lastMessage is not null)
        {
            await chatGroupGrain.NotifyAllParticipantsAsync(lastMessage, HttpContext.RequestAborted);
        }

        return Accepted();
    }

    /// <summary>
    /// Opens a websocket connection for all realtime updates within the party context.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <returns>
    /// A task that completes when the websocket closes or the request is aborted.
    /// </returns>
    /// <remarks>
    /// This is the single realtime channel for a party. The server emits newline-independent JSON
    /// envelope messages containing a <c>type</c>, a per-session <c>sequence</c>, and a typed
    /// <c>data</c> payload. Clients should treat this endpoint as the authoritative realtime feed.
    /// </remarks>
    [HttpGet("{id:guid}/ws")]
    public async Task Realtime(Guid id)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Expected a websocket upgrade request.", HttpContext.RequestAborted);
            return;
        }

        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await realtimeHub.HandleConnectionAsync(id, socket, HttpContext.RequestAborted);
    }

    /// <summary>
    /// Retrieves all available LLM models from the provider router.
    /// </summary>
    /// <param name="id">The party identifier (for cache scoping).</param>
    /// <returns>
    /// <c>200 OK</c> containing a list of available LLM models cached for 5 minutes.
    /// </returns>
    [HttpGet("{id:guid}/models")]
    public async Task<ActionResult<IReadOnlyList<LlmModel>>> GetModels(Guid id)
    {
        var router = grains.GetGrain<ILlmRouterGrain>(0);
        var models = await cache.GetOrCreateAsync($"llm-models-{id}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return await router.GetModelsAsync(HttpContext.RequestAborted);
        });
        return Ok(models);
    }

    /// <summary>
    /// Retrieves all chat groups within the party.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <returns>
    /// <c>200 OK</c> containing all chat groups; <c>404 Not Found</c> if the party does not exist.
    /// </returns>
    [HttpGet("{id:guid}/chat-groups")]
    public async Task<ActionResult<List<ChatGroupInfo>>> GetChatGroups(Guid id)
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var chatGroups = await grains.GetGrain<IPartyGrain>(id).GetChatGroups();
        return Ok(chatGroups);
    }

    /// <summary>
    /// Creates a new chat group within the party.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <param name="request">The chat group creation payload containing the group name.</param>
    /// <returns>
    /// <c>201 Created</c> with the newly created <see cref="ChatGroupInfo"/> when successful;
    /// <c>400 Bad Request</c> if the group name is missing or whitespace;
    /// <c>404 Not Found</c> if the party does not exist.
    /// </returns>
    [HttpPost("{id:guid}/chat-groups")]
    public async Task<ActionResult<ChatGroupInfo>> CreateChatGroup(Guid id, [FromBody] CreateChatGroupRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("Chat group name is required.");
        }

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var chatGroup = await grains.GetGrain<IPartyGrain>(id).CreateChatGroup(request.Name.Trim(), request.Scenario);
        return CreatedAtAction(nameof(GetChatGroups), new { id }, chatGroup);
    }

    /// <summary>
    /// Updates the free-text scenario / setting for a chat group. Used to (re)establish the
    /// in-fiction context that personas see in their system prompt during generation.
    /// </summary>
    /// <param name="id">The party identifier.</param>
    /// <param name="chatGroupId">The chat group identifier.</param>
    /// <param name="request">Scenario update payload (null/whitespace clears the scenario).</param>
    /// <returns>
    /// <c>204 No Content</c> on success;
    /// <c>400 Bad Request</c> if the chat group id is empty;
    /// <c>404 Not Found</c> if the party does not exist.
    /// </returns>
    [HttpPut("{id:guid}/chat-groups/{chatGroupId:guid}/scenario")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateChatGroupScenario(
        Guid id,
        Guid chatGroupId,
        [FromBody] UpdateChatGroupScenarioRequest request)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        await grains.GetGrain<IPartyGrain>(id).UpdateChatGroupScenario(chatGroupId, request.Scenario);
        return NoContent();
    }

    /// <summary>
    /// Returns a causal papertrail of every persona turn in this chat group: which message
    /// triggered which thoughts, decisions, replies, and silent skips. Reconstructed from
    /// durable event-sourced state so it survives silo restarts.
    /// </summary>
    /// <remarks>
    /// Dev/debug endpoint — exposes raw persona "gut reactions" and skip reasons that are
    /// not user-safe content. Gate with auth/RBAC before opening to non-admin clients.
    /// Use <c>format=text</c> for an LLM-readable indented transcript (default), or
    /// <c>format=json</c> for a structured tree.
    /// </remarks>
    [HttpGet("{id:guid}/chat-groups/{chatGroupId:guid}/papertrail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetPapertrail(
        Guid id,
        Guid chatGroupId,
        [FromQuery] int? sinceMessageId = null,
        [FromQuery] string? format = "text")
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(chatGroupId);
        var messages = await chatGroupGrain.GetMessagesAsync();
        var skipped = await chatGroupGrain.GetSkippedTurnsAsync();
        var participants = await chatGroupGrain.GetParticipantsAsync();

        var doc = PapertrailRenderer.Build(messages, skipped, participants, sinceMessageId);

        return string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            ? Content(PapertrailRenderer.ToJson(doc), "application/json")
            : Content(PapertrailRenderer.ToText(doc), "text/plain");
    }

    /// <summary>
    /// Ensures the default party exists, creating it if necessary.
    /// </summary>
    /// <param name="root">The party root grain singleton.</param>
    /// <param name="id">The party identifier to check or create.</param>
    private static async Task EnsureDefaultParty(IPartyRootGrain root, Guid id)
    {
        if (id == Guid.Empty && !await root.HasPartyId(Guid.Empty))
        {
            await root.AddParty(new PartyInfo { Id = Guid.Empty, Name = "Default Party" });
        }
    }

    /// <summary>
    /// Builds the default participant list from persona metadata and an optional user participant.
    /// </summary>
    /// <param name="userId">Optional user identifier to append as a participant.</param>
    /// <param name="userName">Optional user display name; falls back to <paramref name="userId"/>'s string representation.</param>
    /// <returns>
    /// A participant list containing all available personas plus the optional user participant.
    /// </returns>
    private async Task<List<PartyParticipant>> BuildDefaultParticipants(Guid? userId, string? userName)
    {
        var personas = await grains.GetGrain<IPersonaRootGrain>(Guid.Empty).GetAllMetadata();
        var participants = personas.Select(persona => new PartyParticipant
        {
            Id = persona.Id,
            Name = persona.Name,
            IsUser = false
        }).ToList();

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            participants.Add(new PartyParticipant
            {
                Id = userId.Value,
                Name = string.IsNullOrWhiteSpace(userName) ? userId.Value.ToString() : userName,
                IsUser = true
            });
        }

        return participants;
    }

}

public record PartyDetails(PartyInfo Party, PersonaMetadata[] PersonaParticipants);
