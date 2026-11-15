using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PartyTown.Services.Analytics;
using PartyTown.Services.Auth;
using Microsoft.Extensions.Caching.Memory;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Memory;
using PartyTown.Services.Papertrail;
using PartyTown.Services.Realtime;
using PostHog;

namespace PartyTown.Controllers;

[ApiController]
[Route("[controller]")]
public sealed class PartyController(
    IGrainFactory grains,
    IPartyRealtimeHub realtimeHub,
    Actors actors,
    IMemoryCache cache,
    PartyTown.World.WorldMemory worldMemory,
    IPostHogClient posthog,
    ILogger<PartyController> logger) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet]
    public async Task<ActionResult<PartyInfo[]>> GetAll()
    {
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        var parties = await root.GetAll();
        return Ok(parties);
    }

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
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

    [AllowAnonymous]
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

    [Authorize(Policy = ClerkAuth.AdminPolicy)]
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

    [Authorize(Policy = ClerkAuth.AdminPolicy)]

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
                    Driver = participant.Driver
                };
            })
            .ToList();

        var grain = grains.GetGrain<IPartyGrain>(id);
        await grain.SetParticipants(participants);
        var updated = await grain.GetParty();
        return Ok(updated);
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/chat-groups/{chatGroupId:guid}/participants")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Guid>>> GetChatGroupParticipants(Guid id, Guid chatGroupId)
    {
        if (await ValidateChatGroupAsync(id, chatGroupId) is { } error) return error;

        var ids = await grains.GetGrain<IChatGroupGrain>(chatGroupId).GetParticipantIdsAsync();
        return Ok(ids.ToList());
    }

    [HttpPut("{id:guid}/chat-groups/{chatGroupId:guid}/participants")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<Guid>>> SetChatGroupParticipants(
        Guid id,
        Guid chatGroupId,
        [FromBody] UpdateChatGroupParticipantIdsRequest request)
    {
        if (await ValidateChatGroupAsync(id, chatGroupId) is { } error) return error;
        await actors.RequireRoomAsync(User, chatGroupId);

        var idsSet = new HashSet<Guid>((request.ParticipantIds ?? []).Where(g => g != Guid.Empty));

        var partyGrain = grains.GetGrain<IPartyGrain>(id);
        var party = await partyGrain.GetParty();
        var existingCastIds = party.Participants.Select(p => p.Id).ToHashSet();
        var missing = idsSet.Where(pid => !existingCastIds.Contains(pid)).ToList();
        if (missing.Count > 0)
        {
            var personaMetadata = await grains.GetGrain<IPersonaRootGrain>(Guid.Empty).GetAllMetadata();
            var byId = personaMetadata.ToDictionary(p => p.Id, p => p);

            var unknown = missing.Where(pid => !byId.ContainsKey(pid)).ToList();
            if (unknown.Count > 0)
            {
                return BadRequest($"Unknown persona id(s): {string.Join(", ", unknown)}");
            }

            var promoted = missing
                .Select(pid => new PartyParticipant
                {
                    Id = pid,
                    Name = byId[pid].Name,
                    Driver = DriverKind.LLM,
                })
                .ToList();
            await partyGrain.AddParticipantsAsync(promoted);
        }

        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(chatGroupId);
        await chatGroupGrain.SetParticipantIdsAsync(idsSet);
        var updated = await chatGroupGrain.GetParticipantIdsAsync();
        return Ok(updated.ToList());
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/chat-groups/{chatGroupId:guid}/driver-overrides")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<DriverOverrideEntry>>> GetChatGroupDriverOverrides(Guid id, Guid chatGroupId)
    {
        if (await ValidateChatGroupAsync(id, chatGroupId) is { } error) return error;

        var overrides = await grains.GetGrain<IChatGroupGrain>(chatGroupId).GetDriverOverridesAsync();
        return Ok(ToEntries(overrides));
    }

    [HttpPut("{id:guid}/chat-groups/{chatGroupId:guid}/driver-overrides")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<DriverOverrideEntry>>> SetChatGroupDriverOverrides(
        Guid id,
        Guid chatGroupId,
        [FromBody] UpdateChatGroupDriverOverridesRequest request)
    {
        if (await ValidateChatGroupAsync(id, chatGroupId) is { } error) return error;
        await actors.RequireRoomAsync(User, chatGroupId);

        var deduped = (request.Overrides ?? [])
            .Where(o => o.PersonaId != Guid.Empty)
            .GroupBy(o => o.PersonaId)
            .ToDictionary(g => g.Key, g => g.First().Kind);

        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(chatGroupId);
        await chatGroupGrain.SetDriverOverridesAsync(deduped);
        var updated = await chatGroupGrain.GetDriverOverridesAsync();
        return Ok(ToEntries(updated));
    }

    private async Task<ActionResult?> ValidateChatGroupAsync(Guid partyId, Guid chatGroupId)
    {
        if (chatGroupId == Guid.Empty) return BadRequest("Invalid chat group id");
        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, partyId);
        if (!await root.HasPartyId(partyId)) return NotFound();
        var chatGroups = await grains.GetGrain<IPartyGrain>(partyId).GetChatGroups();
        if (!chatGroups.Any(g => g.Id == chatGroupId)) return NotFound();
        return null;
    }

    private static List<DriverOverrideEntry> ToEntries(IReadOnlyDictionary<Guid, DriverKind> overrides)
        => overrides
            .Select(kv => new DriverOverrideEntry { PersonaId = kv.Key, Kind = kv.Value })
            .ToList();

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

        var senderId = await actors.SpeakAsync(User, request.ChatGroupId, request.SenderId);

        await grains.GetGrain<IChatGroupGrain>(request.ChatGroupId)
            .SendNewMessageAsync(senderId, request.Prompt);

        posthog.Track(HttpContext, "message_sent", new Dictionary<string, object>
        {
            ["party_id"] = id, ["room_id"] = request.ChatGroupId, ["length"] = request.Prompt.Length,
        });

        return Accepted();
    }

    [HttpPost("{id:guid}/proceed")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Proceed(Guid id, [FromBody] ProceedRequest request)
    {
        if (request.ChatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        await actors.RequireRoomAsync(User, request.ChatGroupId);


        await grains.GetGrain<IChatGroupGrain>(request.ChatGroupId).ContinueAsync();
        return Accepted();
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> CancelGenerations(Guid id)
    {
        await grains.GetGrain<IPartyGrain>(id).CancelAllGenerations();
        return NoContent();
    }

    [HttpDelete("{id:guid}/chat-groups/{chatGroupId:guid}/messages/{messageId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> DeleteMessage(Guid id, Guid chatGroupId, int messageId)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        await actors.RequireRoomAsync(User, chatGroupId);

        if (messageId < 0)
        {
            return BadRequest("Invalid message id");
        }

        await grains.GetGrain<IChatGroupGrain>(chatGroupId).DeleteMessageAsync(messageId);
        return NoContent();
    }

    [HttpDelete("{id:guid}/chat-groups/{chatGroupId:guid}/messages-after/{messageId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> DeleteMessagesAfter(Guid id, Guid chatGroupId, int messageId)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        await actors.RequireRoomAsync(User, chatGroupId);

        if (messageId < 0)
        {
            return BadRequest("Invalid message id");
        }

        await grains.GetGrain<IChatGroupGrain>(chatGroupId).DeleteMessagesAfterAsync(messageId);
        return NoContent();
    }

    [HttpPost("{id:guid}/reprompt/{messageId:int}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> Reprompt(Guid id, int messageId, [FromBody] RepromptRequest request)
    {
        if (request.ChatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        await actors.RequireRoomAsync(User, request.ChatGroupId);


        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(request.ChatGroupId);
        await chatGroupGrain.DeleteMessagesAfterAsync(messageId);
        await chatGroupGrain.ContinueAsync();
        return Accepted();
    }

    [AllowAnonymous]
    [HttpGet("{id:guid}/ws")]
    public async Task Realtime(Guid id)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsync("Expected a websocket upgrade request.", HttpContext.RequestAborted);
            return;
        }

        var viewer = await actors.ViewerAsync(User, id);
        using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
        await realtimeHub.HandleConnectionAsync(id, socket, viewer, HttpContext.RequestAborted);
    }

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

    [AllowAnonymous]
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
        var viewer = await actors.ViewerAsync(User, id);
        var humans = await actors.HumansAsync(id);
        return Ok(chatGroups.Where(g => AccessRules.CanSeeRoom(viewer, AccessRules.PrivateMembers(g, humans))).ToList());
    }

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

        await actors.RequireRoomAsync(User, chatGroupId);

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        await grains.GetGrain<IPartyGrain>(id).UpdateChatGroupScenario(chatGroupId, request.Scenario);
        return NoContent();
    }

    [HttpPost("{id:guid}/chat-groups/{chatGroupId:guid}/messages/{messageId:int}/remember")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RememberResponse>> RememberMessage(
        Guid id,
        Guid chatGroupId,
        int messageId,
        CancellationToken cancellationToken)
    {
        if (chatGroupId == Guid.Empty)
        {
            return BadRequest("Invalid chat group id");
        }

        await actors.RequireRoomAsync(User, chatGroupId);

        if (messageId < 0)
        {
            return BadRequest("Invalid message id");
        }

        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(chatGroupId);
        var messages = await chatGroupGrain.GetMessagesAsync();
        if (!messages.Any(m => m.MessageId == messageId))
        {
            return NotFound();
        }

        var roomCast = await chatGroupGrain.GetCastAsync();
        var contextWindow = messages.Where(m => m.MessageId <= messageId).ToList();

        var result = await worldMemory.CaptureAsync(
            Moment.FromMessages(id, MomentSources.OfKind(roomCast.Kind), chatGroupId, contextWindow, messageId, roomCast.Views()),
            cancellationToken);
        if (result is null)
            return Problem("Capture failed", statusCode: StatusCodes.Status500InternalServerError);

        logger.LogInformation(
            "Remember: message {MessageId} in room {RoomId} → event={EventCreated} recollections={RecollectionCount} concepts={ConceptCount}",
            messageId, chatGroupId, result.EventCreated, result.RecollectionsCreated, result.ConceptsTouched);

        return Accepted(new RememberResponse(
            EventCreated: result.EventCreated,
            RecollectionsCreated: result.RecollectionsCreated,
            ConceptsTouched: result.ConceptsTouched));
    }

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

        await actors.RequireRoomAsync(User, chatGroupId);

        var root = grains.GetGrain<IPartyRootGrain>(Guid.Empty);
        await EnsureDefaultParty(root, id);
        if (!await root.HasPartyId(id))
        {
            return NotFound();
        }

        var chatGroupGrain = grains.GetGrain<IChatGroupGrain>(chatGroupId);
        var messages = await chatGroupGrain.GetMessagesAsync();
        var skipped = await chatGroupGrain.GetSkippedTurnsAsync();
        var participants = await BuildRoomViewsAsync(chatGroupGrain);

        var doc = PapertrailRenderer.Build(messages, skipped, participants, sinceMessageId);

        return string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            ? Content(PapertrailRenderer.ToJson(doc), "application/json")
            : Content(PapertrailRenderer.ToText(doc), "text/plain");
    }

    private static async Task EnsureDefaultParty(IPartyRootGrain root, Guid id)
    {
        if (id == Guid.Empty && !await root.HasPartyId(Guid.Empty))
        {
            await root.AddParty(new PartyInfo { Id = Guid.Empty, Name = "Default Party" });
        }
    }

    private static async Task<List<ParticipantView>> BuildRoomViewsAsync(IChatGroupGrain chatGroupGrain)
        => (await chatGroupGrain.GetCastAsync()).Views();

    private async Task<List<PartyParticipant>> BuildDefaultParticipants(Guid? userId, string? userName)
    {
        var personas = await grains.GetGrain<IPersonaRootGrain>(Guid.Empty).GetAllMetadata();
        var participants = personas.Select(persona => new PartyParticipant
        {
            Id = persona.Id,
            Name = persona.Name,
            Driver = DriverKind.LLM
        }).ToList();

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            participants.Add(new PartyParticipant
            {
                Id = userId.Value,
                Name = string.IsNullOrWhiteSpace(userName) ? userId.Value.ToString() : userName,
                Driver = DriverKind.User
            });
        }

        return participants;
    }

}

public record PartyDetails(PartyInfo Party, PersonaMetadata[] PersonaParticipants);

public record RememberResponse(bool EventCreated, int RecollectionsCreated, int ConceptsTouched);
