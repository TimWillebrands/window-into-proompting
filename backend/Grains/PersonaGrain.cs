using System.Text.Json;
using Orleans.Concurrency;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Generation;
using PartyTown.Services.Streaming;

namespace PartyTown.Grains;

/// <summary>
/// Grain that stores a single persona's data and reacts to messages.
/// Marked [Reentrant] so multiple concurrent NotifyMessageAsync calls don't deadlock.
/// </summary>
[Reentrant]
public sealed class PersonaGrain(
    [PersistentState(stateName: "persona", storageName: "personas")]
    IPersistentState<Persona> state,
    ILoggerFactory loggerFactory,
    ILogger<PersonaGrain> logger)
    : Grain, IPersonaGrain
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain activated - Name: '{PersonaName}' - Id: '{PersonaId}'",
            state.State.Name,
            this.GetPrimaryKey());
        return base.OnActivateAsync(cancellationToken);
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain deactivating: {Reason} - Id: '{PersonaId}'",
            reason.ReasonCode,
            this.GetPrimaryKey());
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public Task SetPersona(Persona persona) =>
        SetPersona(persona.Name, persona.SystemPrompt, persona.Bio);

    public async Task SetPersona(string name, string systemPrompt, string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name,
            SystemPrompt = systemPrompt,
            Bio = bio
        });

    public async Task SetName(string name)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name
        });

    public async Task SetSystemPrompt(string systemPrompt)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            SystemPrompt = systemPrompt
        });

    public async Task SetBio(string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Bio = bio
        });

    public Task<Persona> GetPersona() =>
        Task.FromResult(state.State with
        {
            Id = this.GetPrimaryKey()
        });

    public Task DeletePersona() =>
        state.ClearStateAsync();

    /// <summary>
    /// Called by ChatGroupGrain when a new message arrives in the chat group.
    /// The persona independently decides whether to respond and generates if yes.
    /// </summary>
    public async Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage)
    {
        var personaId = this.GetPrimaryKey();
        var persona = state.State with { Id = personaId };

        logger.LogInformation("Persona {PersonaName} notified of message {MessageId} in chat group {ChatGroupId}",
            persona.Name, triggeringMessage.MessageId, chatGroupId);

        var chatGroupGrain = GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);

        // Phase 1: Reserve a message slot early so we can stream decision events
        var messageId = await chatGroupGrain.GetNextMessageIdAsync(personaId);

        try
        {
            // Resolve the LLM endpoint
            var router = GrainFactory.GetGrain<ILlmRouterGrain>(0);

            // Fetch context from the chat group
            var history = await chatGroupGrain.GetMessagesUntilAsync(triggeringMessage.MessageId + 1);
            var participants = await chatGroupGrain.GetParticipantsAsync();

            var self = new GenerationParticipant
            {
                Id = personaId,
                Name = persona.Name,
                Bio = persona.Bio,
                SystemPrompt = persona.SystemPrompt,
                IsUser = false
            };

            var allParticipants = participants.Select(p =>
            {
                if (p.Id == personaId)
                    return self;
                return new GenerationParticipant
                {
                    Id = p.Id,
                    Name = p.Name,
                    IsUser = p.IsUser,
                    Bio = null,
                    SystemPrompt = null
                };
            }).ToList();

            // Send attend event so frontend knows this persona is evaluating the conversation
            await chatGroupGrain.NotifyStreamChunkAsync(messageId, new MessageStreamEvent
            {
                ChatGroupId = chatGroupId,
                Event = MessageStreamEvent.PersonaEvaluatingResponse,
                Data = personaId.ToString(),
                Done = false
            });

            // Phase 2: Decide whether to respond (streaming decision reasoning)
            var decisionService = new PersonaDecisionService(router, loggerFactory.CreateLogger<PersonaDecisionService>());
            var totalAiRounds = await chatGroupGrain.CountTrailingAssistantMessagesAsync();

            var decision = await decisionService.ShouldRespondAsync(
                self,
                history,
                allParticipants,
                totalAiRounds,
                onEvent: async (eventType, data, done) =>
                {
                    await chatGroupGrain.NotifyStreamChunkAsync(messageId, new MessageStreamEvent
                    {
                        ChatGroupId = chatGroupId,
                        Event = eventType,
                        Data = data,
                        Done = done
                    });
                },
                cancellationToken: default);

            if (!decision.Respond)
            {
                logger.LogInformation("Persona {PersonaName} decided NOT to respond: {Reason}", persona.Name, decision.Reason);

                // Notify frontend that this persona declined
                await chatGroupGrain.NotifyStreamChunkAsync(messageId, new MessageStreamEvent
                {
                    ChatGroupId = chatGroupId,
                    Event = MessageStreamEvent.PersonaDeclinedResponse,
                    Data = JsonSerializer.Serialize(new
                    {
                        personaId = personaId,
                        reason = decision.Reason
                    }, WebOptions),
                    Done = true
                });
                return;
            }

            logger.LogInformation("Persona {PersonaName} decided to respond: {Reason}", persona.Name, decision.Reason);

            // Send appraisal complete — persona has decided to engage
            await chatGroupGrain.NotifyStreamChunkAsync(messageId, new MessageStreamEvent
            {
                ChatGroupId = chatGroupId,
                Event = MessageStreamEvent.PersonaEvaluationComplete,
                Data = JsonSerializer.Serialize(new
                {
                    personaId = personaId,
                    instruction = decision.Instruction,
                    reason = decision.Reason,
                    stop = false
                }, WebOptions),
                Done = false
            });

            // Phase 3: Generate response
            var session = new GenerationSession(router, allParticipants);

            // Re-fetch history up to our reserved message
            var generationHistory = await chatGroupGrain.GetMessagesUntilAsync(messageId);

            // Build generation participants with full details
            var personaRoot = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
            var allPersonas = await personaRoot.GetAll();
            var personaMap = allPersonas.ToDictionary(p => p.Id, p => p);

            var fullParticipants = participants.Select(p =>
            {
                if (p.IsUser)
                    return new GenerationParticipant { Id = p.Id, Name = p.Name, IsUser = true, SystemPrompt = "this is a real human user", Bio = null };
                if (personaMap.TryGetValue(p.Id, out var pm))
                    return new GenerationParticipant { Id = pm.Id, Name = pm.Name, Bio = pm.Bio, SystemPrompt = pm.SystemPrompt, IsUser = false };
                return new GenerationParticipant { Id = p.Id, Name = p.Name, IsUser = false };
            }).ToList();

            // Generate response — appraisal already decided to engage
            var result = await session.GenerateResponseOnlyAsync(
                self,
                generationHistory,
                onEvent: async (eventType, data, done) =>
                {
                    await chatGroupGrain.NotifyStreamChunkAsync(messageId, new MessageStreamEvent
                    {
                        ChatGroupId = chatGroupId,
                        Event = eventType,
                        Data = data,
                        Done = done
                    });
                },
                default);

            // Phase 4: Store the completed response
            var appraisalJson = JsonSerializer.Serialize(new
            {
                personaId = personaId,
                instruction = decision.Instruction,
                reason = decision.Reason,
                stop = false
            }, WebOptions);

            await chatGroupGrain.AppendMessageAsync(
                messageId,
                result.Message ?? string.Empty,
                result.Reasoning,
                appraisalJson);

            logger.LogInformation("Persona {PersonaName} completed response for message {MessageId}", persona.Name, messageId);
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Persona {PersonaName} generation cancelled", persona.Name);
            await chatGroupGrain.MarkGenerationFailedAsync(messageId, "cancelled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Persona {PersonaName} generation failed", persona.Name);
            // FIXME: If at some point we go public, don't send exceptions over the wire
            await chatGroupGrain.MarkGenerationFailedAsync(messageId, ex.ToString());
        }
    }

    private async Task UpdateStateAsync(Func<Persona, Persona> update)
    {
        var current = state.State ?? new Persona();
        state.State = update(current);
        await state.WriteStateAsync();
    }
}

/// <summary>
/// Grain contract for managing a single persona.
/// </summary>
[Alias("backend.IPersonaGrain")]
public interface IPersonaGrain : IGrainWithGuidKey
{
    [Alias("SetPersonaFromModel")]
    Task SetPersona(Persona persona);

    [Alias("SetPersona")]
    Task SetPersona(string name, string systemPrompt, string? bio);

    [Alias("SetName")]
    Task SetName(string name);

    [Alias("SetSystemPrompt")]
    Task SetSystemPrompt(string systemPrompt);

    [Alias("SetBio")]
    Task SetBio(string? bio);

    [Alias("GetPersona")]
    Task<Persona> GetPersona();

    [Alias("DeletePersona")]
    Task DeletePersona();

    [Alias("NotifyMessageAsync")]
    Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage);
}
