using System.Collections.Concurrent;
using System.Text.Json;
using Orleans.Concurrency;
using PartyTown.Grains.Generation;
using PartyTown.Model;
using PartyTown.Services.Generation;
using PartyTown.Services.Streaming;

namespace PartyTown.Grains;

/// <summary>
/// Grain that stores a single persona's data and reacts to messages.
/// Marked [Reentrant] so multiple concurrent NotifyMessageAsync calls don't deadlock,
/// and so CancelGenerationAsync can interrupt an in-flight NotifyMessageAsync.
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

    // One CTS per chat group so concurrent NotifyMessageAsync for different groups don't
    // cancel each other. Reentrancy made the previous single _activeCts field a race:
    // group B's call would cancel group A's in-flight generation and mark it "cancelled".
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _ctsByChatGroup = new();

    public Task CancelGenerationAsync()
    {
        foreach (var cts in _ctsByChatGroup.Values)
        {
            try { cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        return Task.CompletedTask;
    }

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
    public async Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage, CancellationToken ct = default)
    {
        var personaId = this.GetPrimaryKey();
        var persona = state.State with { Id = personaId };

        var newCts = new CancellationTokenSource();
        _ctsByChatGroup.AddOrUpdate(
            chatGroupId,
            _ => newCts,
            (_, old) =>
            {
                try { old.Cancel(); } catch (ObjectDisposedException) { }
                old.Dispose();
                return newCts;
            });

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, newCts.Token);
        var linkedCt = linkedCts.Token;

        logger.LogInformation("Persona {PersonaName} notified of message {MessageId} in chat group {ChatGroupId}",
            persona.Name, triggeringMessage.MessageId, chatGroupId);

        var chatGroupGrain = GrainFactory.GetGrain<IChatGroupGrain>(chatGroupId);
        var messageId = await chatGroupGrain.GetNextMessageIdAsync(personaId);

        try
        {
            // Single history snapshot, shared by decision + generation (issue #4).
            // Taking the snapshot at `messageId` includes any messages other personas
            // committed between our slot reservation and our read.
            var history = await chatGroupGrain.GetMessagesUntilAsync(messageId);
            var participants = await chatGroupGrain.GetParticipantsAsync();

            var self = new GenerationParticipant
            {
                Id = personaId,
                Name = persona.Name,
                Bio = persona.Bio,
                SystemPrompt = persona.SystemPrompt,
                IsUser = false
            };

            var decisionParticipants = BuildDecisionParticipants(participants, personaId, self);

            await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                MessageStreamEvent.PersonaEvaluatingResponse, personaId.ToString(), false);

            var decision = await RunDecisionPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, history, decisionParticipants, linkedCt);

            if (!decision.Respond)
            {
                logger.LogInformation("Persona {PersonaName} decided NOT to respond: {Reason}", persona.Name, decision.Reason);
                await chatGroupGrain.MarkGenerationStoppedAsync(messageId, decision.Reason);
                await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                    MessageStreamEvent.PersonaDeclinedResponse,
                    JsonSerializer.Serialize(new { personaId, reason = decision.Reason }, WebOptions),
                    true);
                return;
            }

            logger.LogInformation("Persona {PersonaName} decided to respond: {Reason}", persona.Name, decision.Reason);

            await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                MessageStreamEvent.PersonaEvaluationComplete,
                JsonSerializer.Serialize(new
                {
                    personaId,
                    instruction = decision.Instruction,
                    reason = decision.Reason,
                    stop = false
                }, WebOptions),
                false);

            var fullParticipants = await BuildGenerationParticipantsAsync(participants, personaId);
            var result = await RunGenerationPhaseAsync(
                chatGroupGrain, chatGroupId, messageId, self, fullParticipants, history, decision.Instruction, persona.Name, linkedCt);

            var appraisalJson = JsonSerializer.Serialize(new
            {
                personaId,
                instruction = decision.Instruction,
                reason = decision.Reason,
                stop = false
            }, WebOptions);

            await chatGroupGrain.AppendMessageAsync(
                messageId,
                result.Message ?? string.Empty,
                result.Reasoning,
                appraisalJson,
                linkedCt);

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
        finally
        {
            // Clear the slot only if we still own it (a later call may have replaced us).
            if (_ctsByChatGroup.TryGetValue(chatGroupId, out var current) && ReferenceEquals(current, newCts))
            {
                _ctsByChatGroup.TryRemove(new KeyValuePair<Guid, CancellationTokenSource>(chatGroupId, newCts));
                newCts.Dispose();
            }
        }
    }

    private static List<GenerationParticipant> BuildDecisionParticipants(
        IReadOnlyList<PartyParticipant> participants,
        Guid personaId,
        GenerationParticipant self)
        => participants.Select(p => p.Id == personaId
            ? self
            : new GenerationParticipant
            {
                Id = p.Id,
                Name = p.Name,
                IsUser = p.IsUser,
                Bio = null,
                SystemPrompt = null
            }).ToList();

    private async Task<List<GenerationParticipant>> BuildGenerationParticipantsAsync(
        IReadOnlyList<PartyParticipant> participants,
        Guid personaId)
    {
        var personaRoot = GrainFactory.GetGrain<IPersonaRootGrain>(Guid.Empty);
        var allPersonas = await personaRoot.GetAll();
        var personaMap = allPersonas.ToDictionary(p => p.Id, p => p);

        return participants.Select(p =>
        {
            if (p.IsUser)
            {
                // SystemPrompt left null for users: IsUser carries the distinction, and a literal
                // marker string risked leaking into concatenated prompts downstream (issue #9).
                return new GenerationParticipant { Id = p.Id, Name = p.Name, IsUser = true, SystemPrompt = null, Bio = null };
            }
            if (personaMap.TryGetValue(p.Id, out var pm))
                return new GenerationParticipant { Id = pm.Id, Name = pm.Name, Bio = pm.Bio, SystemPrompt = pm.SystemPrompt, IsUser = false };
            return new GenerationParticipant { Id = p.Id, Name = p.Name, IsUser = false };
        }).ToList();
    }

    private async Task<ShouldRespondResult> RunDecisionPhaseAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        GenerationParticipant self,
        IReadOnlyList<ChatMessage> history,
        IReadOnlyList<GenerationParticipant> participants,
        CancellationToken ct)
    {
        var router = GrainFactory.GetGrain<ILlmRouterGrain>(0);
        var decisionService = new PersonaDecisionService(router, loggerFactory.CreateLogger<PersonaDecisionService>());
        var totalAiRounds = await chatGroupGrain.CountTrailingAssistantMessagesAsync();

        return await decisionService.ShouldRespondAsync(
            self,
            history,
            participants,
            totalAiRounds,
            onEvent: (eventType, data, done) => NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId, eventType, data, done),
            cancellationToken: ct);
    }

    private async Task<GenerationResult> RunGenerationPhaseAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        GenerationParticipant self,
        List<GenerationParticipant> fullParticipants,
        IReadOnlyList<ChatMessage> history,
        string? turnInstruction,
        string personaName,
        CancellationToken ct)
    {
        var router = GrainFactory.GetGrain<ILlmRouterGrain>(0);
        var session = new GenerationSession(router, fullParticipants);

        const int maxRetries = 2;
        GenerationResult? result = null;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                result = await session.GenerateResponseOnlyAsync(
                    self,
                    history,
                    onEvent: (eventType, data, done) => NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId, eventType, data, done),
                    ct,
                    turnInstruction);
                break;
            }
            catch (OperationCanceledException)
            {
                throw; // manual cancel — don't retry, don't emit retry event
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                logger.LogWarning(ex, "Persona {PersonaName} generation attempt {Attempt} failed, retrying", personaName, attempt + 1);
                // Tell the frontend to clear any partial chunks it buffered during this attempt
                // before the next attempt begins restreaming from scratch (issue #8).
                await NotifyStreamAsync(chatGroupGrain, chatGroupId, messageId,
                    MessageStreamEvent.GenerationRetry,
                    JsonSerializer.Serialize(new { attempt = attempt + 1, nextAttemptInSeconds = 2 * (attempt + 1) }, WebOptions),
                    false);
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
            }
        }

        return result!;
    }

    private static Task NotifyStreamAsync(
        IChatGroupGrain chatGroupGrain,
        Guid chatGroupId,
        int messageId,
        string eventType,
        string data,
        bool done)
        => chatGroupGrain.NotifyStreamChunkAsync(messageId, new MessageStreamEvent
        {
            ChatGroupId = chatGroupId,
            Event = eventType,
            Data = data,
            Done = done
        });

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
    Task NotifyMessageAsync(Guid chatGroupId, ChatMessage triggeringMessage, CancellationToken ct);

    [Alias("CancelGenerationAsync")]
    Task CancelGenerationAsync();
}
