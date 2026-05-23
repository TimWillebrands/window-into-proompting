using System.Diagnostics;
using PartyTown.Grains;
using PartyTown.Grains.Generation;
using PartyTown.Logging;
using PartyTown.Model;

namespace PartyTown.Services.ResponsePipeline;

/// <summary>
/// Stop-signal race: when a new message arrives, walk a persona's in-flight
/// generations in a chat group and decide cancel-vs-continue per generation.
///   • Decision phase → always cancel (cheap; no public artifact yet).
///   • Speaking phase past PNR → cannot cancel; record a repair hint for next turn.
///   • Speaking phase pre-PNR → score salience via LFM2; cancel if cancelScore &gt; 0.5,
///     otherwise record a repair hint.
/// Salience or routing failures default to "let it ride" (no cancel, no repair) —
/// preserves current behavior rather than introducing a new failure surface.
///
/// Stateless: the per-persona in-flight tracking lives in <see cref="InFlightStore"/>,
/// passed in per call. One singleton serves every persona.
/// </summary>
public sealed class RaceTrigger(IGrainFactory grainFactory, ILoggerFactory loggerFactory)
{
    // PNR is the absolute generated-token count past which an in-flight generation
    // cannot be cancelled (only repaired on the next turn). Cancel threshold is the
    // cancelScore above which the race elects to cancel.
    // See plans/well-i-controll-henk-eventual-stearns.md for derivation.
    private const int PnrTokens = 80;
    private const double CancelThreshold = 0.5;

    private readonly ILogger<RaceTrigger> _logger = loggerFactory.CreateLogger<RaceTrigger>();

    public async Task EvaluateAsync(
        Persona persona,
        Guid chatGroupId,
        ChatMessage triggeringMessage,
        IChatGroupGrain chatGroupGrain,
        InFlightStore store,
        CancellationToken ct)
    {
        var snapshot = store.SnapshotForChatGroup(chatGroupId);

        if (snapshot.Count == 0) return;

        string? senderName = null;
        async Task<string> ResolveSenderNameAsync()
        {
            if (senderName is not null) return senderName;
            try
            {
                var participants = await chatGroupGrain.GetParticipantsAsync();
                senderName = participants.FirstOrDefault(p => p.Id == triggeringMessage.SenderId)?.Name
                             ?? triggeringMessage.SenderId.ToString();
            }
            catch
            {
                senderName = triggeringMessage.SenderId.ToString();
            }
            return senderName;
        }

        foreach (var (inFlightMessageId, gen) in snapshot)
        {
            using var raceSpan = Tracing.Persona.StartActivity("persona.race", ActivityKind.Internal);
            raceSpan?.SetTag("persona.id", persona.Id);
            raceSpan?.SetTag("persona.name", persona.Name);
            raceSpan?.SetTag("in_flight.message_id", inFlightMessageId);
            raceSpan?.SetTag("triggered_by.message_id", triggeringMessage.MessageId);

            var snap = gen.Snapshot();
            raceSpan?.SetTag("in_flight.phase", snap.Phase.ToString());
            raceSpan?.SetTag("in_flight.tokens", snap.GeneratedTokens);

            if (snap.Phase == InFlightPhase.Decision)
            {
                raceSpan?.SetTag("race.outcome", "cancel-decision");
                _logger.LogInformation(
                    "Race: persona {Name} cancelling in-flight DECISION (msg {Mid}) on new {NewMid}",
                    persona.Name, inFlightMessageId, triggeringMessage.MessageId);
                gen.MarkRaceCancelled(
                    triggeringMessage.Content ?? string.Empty,
                    await ResolveSenderNameAsync());
                try { gen.Cts.Cancel(); } catch (ObjectDisposedException) { }
                await RecordOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "cancel-decision", null, null);
                continue;
            }

            // Speaking phase
            if (snap.GeneratedTokens >= PnrTokens)
            {
                // Past point of no return. Stash repair hint without burning a salience call —
                // the message will ship regardless, and the next decision pass will see the
                // hint and the new message in history.
                raceSpan?.SetTag("race.outcome", "past-pnr");
                store.SetRepairHint(chatGroupId, new RepairHint(
                    triggeringMessage.MessageId,
                    await ResolveSenderNameAsync(),
                    triggeringMessage.Content ?? string.Empty));
                await RecordOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "past-pnr", null, null);
                continue;
            }

            // Pre-PNR: race
            SalienceScore salience;
            try
            {
                var salienceService = new PersonaSalienceService(
                    grainFactory.GetGrain<ILlmRouterGrain>(0),
                    loggerFactory.CreateLogger<PersonaSalienceService>());
                var selfParticipant = new GenerationParticipant
                {
                    Id = persona.Id,
                    Name = persona.Name,
                    Bio = persona.Bio,
                    SystemPrompt = persona.SystemPrompt,
                    IsUser = false,
                    Chattiness = persona.Chattiness,
                    Impulsivity = persona.Impulsivity
                };
                salience = await salienceService.ScoreAsync(
                    selfParticipant,
                    snap.GutReaction,
                    snap.WouldSayPreview,
                    snap.GeneratedText,
                    triggeringMessage,
                    await ResolveSenderNameAsync(),
                    ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Race salience call failed for persona {Name}", persona.Name);
                salience = SalienceScore.LetItRide;
            }

            var commitmentProgress = Math.Min(1.0, snap.GeneratedTokens / (double)PnrTokens);
            var cancelScore = salience.Value * (1.0 - persona.Impulsivity) * (1.0 - commitmentProgress);

            raceSpan?.SetTag("race.salience", salience.Value);
            raceSpan?.SetTag("race.salience.kind", salience.Kind);
            raceSpan?.SetTag("race.impulsivity", persona.Impulsivity);
            raceSpan?.SetTag("race.commitment_progress", commitmentProgress);
            raceSpan?.SetTag("race.cancel_score", cancelScore);

            if (cancelScore > CancelThreshold)
            {
                raceSpan?.SetTag("race.outcome", "cancel-generation");
                _logger.LogInformation(
                    "Race: persona {Name} cancelling in-flight GENERATION (msg {Mid}, tokens {Tok}, salience {Sal:F2}, cancelScore {Cs:F2}) on new {NewMid}",
                    persona.Name, inFlightMessageId, snap.GeneratedTokens, salience.Value, cancelScore, triggeringMessage.MessageId);
                gen.MarkRaceCancelled(
                    triggeringMessage.Content ?? string.Empty,
                    await ResolveSenderNameAsync());
                try { gen.Cts.Cancel(); } catch (ObjectDisposedException) { }
                await RecordOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "cancel-generation", salience.Value, cancelScore);
            }
            else
            {
                raceSpan?.SetTag("race.outcome", "continue");
                store.SetRepairHint(chatGroupId, new RepairHint(
                    triggeringMessage.MessageId,
                    await ResolveSenderNameAsync(),
                    triggeringMessage.Content ?? string.Empty));
                await RecordOutcomeAsync(chatGroupGrain, persona,
                    triggeringMessage.MessageId, "continue", salience.Value, cancelScore);
            }
        }
    }

    /// <summary>Persist a race outcome to the chat group's thought-log papertrail. Wraps
    /// the call so a transient persistence failure can't bring down the race itself —
    /// the cancel/continue decision has already been applied by this point.</summary>
    private async Task RecordOutcomeAsync(
        IChatGroupGrain chatGroupGrain,
        Persona persona,
        int triggeredByMessageId,
        string outcome,
        double? salience,
        double? cancelScore)
    {
        try
        {
            await chatGroupGrain.RecordRaceEvaluationAsync(
                persona.Id,
                persona.Name ?? string.Empty,
                triggeredByMessageId,
                outcome,
                salience,
                cancelScore);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Failed to record race outcome {Outcome} for persona {PersonaName}",
                outcome, persona.Name);
        }
    }
}
