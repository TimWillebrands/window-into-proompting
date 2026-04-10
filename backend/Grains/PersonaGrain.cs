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

    /// <summary>
    /// Performs activation logic for the PersonaGrain, logging the persona name and grain primary key and then delegating to the base activation behavior.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to observe while the activation completes.</param>
    /// <returns>A task that completes when activation processing has finished.</returns>
    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain activated - Name: '{PersonaName}' - Id: '{PersonaId}'",
            state.State.Name,
            this.GetPrimaryKey());
        return base.OnActivateAsync(cancellationToken);
    }

    /// <summary>
    /// Handles grain deactivation by logging the deactivation reason and persona id, then defers completion to the base implementation.
    /// </summary>
    /// <param name="reason">The deactivation reason provided by the runtime.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel deactivation work.</param>
    /// <returns>A task that completes when deactivation processing has finished.</returns>
    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "PersonaGrain deactivating: {Reason} - Id: '{PersonaId}'",
            reason.ReasonCode,
            this.GetPrimaryKey());
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    /// <summary>
        /// Updates the persisted persona using the provided persona's Name, SystemPrompt, and Bio.
        /// </summary>
        /// <param name="persona">The persona whose Name, SystemPrompt, and Bio will be stored.</param>
        /// <returns>A task that completes when the persona state has been updated and persisted.</returns>
        public Task SetPersona(Persona persona) =>
        SetPersona(persona.Name, persona.SystemPrompt, persona.Bio);

    /// <summary>
        /// Set the persona's name, system prompt, and optional bio and persist them to the grain state.
        /// </summary>
        /// <param name="name">The persona's display name.</param>
        /// <param name="systemPrompt">The system prompt text associated with the persona.</param>
        /// <param name="bio">An optional short biography for the persona, or null to clear it.</param>
        public async Task SetPersona(string name, string systemPrompt, string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name,
            SystemPrompt = systemPrompt,
            Bio = bio
        });

    /// <summary>
        /// Updates the persona's Name in persistent grain state and saves the change.
        /// </summary>
        /// <param name="name">The new display name for the persona.</param>
        public async Task SetName(string name)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Name = name
        });

    /// <summary>
        /// Updates the persona's system prompt in persistent state to the provided value.
        /// </summary>
        /// <param name="systemPrompt">The new system prompt text for the persona.</param>
        public async Task SetSystemPrompt(string systemPrompt)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            SystemPrompt = systemPrompt
        });

    /// <summary>
        /// Update the persona's Bio field in persistent state to the provided value.
        /// </summary>
        /// <param name="bio">The persona's biography text; pass <c>null</c> to remove any existing biography.</param>
        public async Task SetBio(string? bio)
        => await UpdateStateAsync(current => current with
        {
            Id = this.GetPrimaryKey(),
            Bio = bio
        });

    /// <summary>
        /// Retrieves the persona stored in this grain and returns it with its Id set to the grain's primary key.
        /// </summary>
        /// <returns>The current <see cref="Persona"/> state with <c>Id</c> equal to this grain's primary key.</returns>
        public Task<Persona> GetPersona() =>
        Task.FromResult(state.State with
        {
            Id = this.GetPrimaryKey()
        });

    /// <summary>
        /// Clears the persisted persona state for this grain from durable storage.
        /// </summary>
        /// <returns>A task that completes when the persona state has been cleared.</returns>
        public Task DeletePersona() =>
        state.ClearStateAsync();

    /// <summary>
    /// Called by ChatGroupGrain when a new message arrives in the chat group.
    /// The persona independently decides whether to respond and generates if yes.
    /// <summary>
    /// Reacts to a chat-group message notification by deciding whether the persona should respond and, if so, generating and persisting a response while streaming progress events to the chat group.
    /// </summary>
    /// <param name="chatGroupId">The identifier of the chat group where the triggering message was posted.</param>
    /// <param name="triggeringMessage">The message that notified this persona and serves as the context trigger for evaluation and potential response.</param>
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

    /// <summary>
    /// Apply a transformation to the stored Persona and persist the updated state.
    /// </summary>
    /// <param name="update">A function that receives the current Persona (or a new default Persona if none exists) and returns the Persona to store.</param>
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
    /// <summary>
    /// Replaces the stored persona with the provided Persona model.
    /// </summary>
    /// <param name="persona">Persona whose Name, SystemPrompt, and Bio will replace the persisted persona state.</param>
    [Alias("SetPersonaFromModel")]
    Task SetPersona(Persona persona);

    /// <summary>
    /// Sets the persona's name, system prompt, and optional bio in the grain's persistent state, overwriting any existing values.
    /// </summary>
    /// <param name="name">The display name for the persona.</param>
    /// <param name="systemPrompt">The persona's system prompt guiding behavior and responses.</param>
    /// <param name="bio">An optional biography or description for the persona; pass null to clear it.</param>
    [Alias("SetPersona")]
    Task SetPersona(string name, string systemPrompt, string? bio);

    /// <summary>
    /// Updates the persona's display name in persisted state.
    /// </summary>
    /// <param name="name">The new persona name to persist.</param>
    [Alias("SetName")]
    Task SetName(string name);

    /// <summary>
    /// Update the persona's system prompt used to guide its behavior.
    /// </summary>
    /// <param name="systemPrompt">The system prompt text for the persona.</param>
    [Alias("SetSystemPrompt")]
    Task SetSystemPrompt(string systemPrompt);

    /// <summary>
    /// Updates the persona's biography and persists the change to the persona's state.
    /// </summary>
    /// <param name="bio">The persona's short biography or null to clear it.</param>
    [Alias("SetBio")]
    Task SetBio(string? bio);

    /// <summary>
    /// Gets the persona stored in this grain.
    /// </summary>
    /// <returns>The persona for this grain; its <c>Id</c> is set to the grain's primary key.</returns>
    [Alias("GetPersona")]
    Task<Persona> GetPersona();

    /// <summary>
    /// Remove the stored persona from persistent state for this grain.
    /// </summary>
    /// <remarks>
    /// After completion, the persisted persona will be cleared so subsequent reads return a default/empty Persona until a new persona is set.
    /// </remarks>
    [Alias("DeletePersona")]
    Task DeletePersona();

    /// <summary>
    /// Notify this persona that a message was posted in a chat group so the persona can decide whether and how to respond.
    /// </summary>
    /// <param name="chatGroupId">The identifier of the chat group where the triggering message was posted.</param>
    /// <param name="triggeringMessage">The chat message that triggered the notification.</param>
    [Alias("NotifyMessageAsync")]
    Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage);
}
