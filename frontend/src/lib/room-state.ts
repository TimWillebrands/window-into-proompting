// Room realtime state: pure envelope reduction + generation-phase selector. No socket, no store.

export interface ChatMessageMetadata {
    provider: string | null;
    modelName: string | null;
}

export interface RealtimeChatMessage {
    chatGroupId: string;
    messageId: number;
    content: string | null;
    reasoning: string | null;
    appraisal: string | null;
    error: string | null;
    senderType: string;
    senderId: string;
    sendAt: number | null;
    metadata: ChatMessageMetadata | null;
    generationEvents: Array<{ event: string; data: string; at: number }>;
    /**
     * Discriminator for special message variants. Currently:
     *   "emote" — race-cancelled abandonment line (italic action, no bubble chrome).
     * null/undefined for normal messages.
     */
    kind?: string | null;
}

export interface DeclinedDecision {
    personaId: string;
    reason: string | null;
    decisionText: string;
    messageId: number;
    timestamp: number;
}

/**
 * One stop-signal race outcome from a PersonaGrain. Mirrors backend RaceEvaluation
 * exactly. Surfaced into the thought log with kind = `race-${outcome}` so the user
 * can see when in-flight generations are cancelled, ship past PNR, or let-it-ride.
 */
export interface RaceEvaluation {
    personaId: string;
    personaName: string;
    triggeredByMessageId: number;
    /** "cancel-decision" | "cancel-generation" | "past-pnr" | "continue" */
    outcome: string;
    salience: number | null;
    cancelScore: number | null;
    sendAt: number;
}

export interface ChatGroupRealtimeState {
    chatGroupId: string;
    messages: RealtimeChatMessage[];
    activeGenerationMessageIds: number[];
    declinedDecisions: DeclinedDecision[];
    raceEvaluations: RaceEvaluation[];
    lastSequence: number;
}

export type PartyRealtimeEnvelope = {
    type: string;
    sequence: number;
    timestamp: number;
    data: unknown;
};

export const DEFAULT_CHAT_GROUP_STATE = (
    chatGroupId: string,
): ChatGroupRealtimeState => ({
    chatGroupId,
    messages: [],
    activeGenerationMessageIds: [],
    declinedDecisions: [],
    raceEvaluations: [],
    lastSequence: 0,
});

const appendOrUpdateMessage = (
    messages: RealtimeChatMessage[],
    message: RealtimeChatMessage,
) => {
    const existingIndex = messages.findIndex(
        (entry) => entry.messageId === message.messageId,
    );

    if (existingIndex < 0) {
        const nextMessages = [...messages, message];
        const lastMsg = messages[messages.length - 1];

        if (lastMsg && message.messageId < lastMsg.messageId) {
            return nextMessages.sort((a, b) => a.messageId - b.messageId);
        }
        return nextMessages;
    }

    const existing = messages[existingIndex];
    const nextMessages = [...messages];
    nextMessages[existingIndex] = {
        ...existing,
        ...message,
        generationEvents:
            (message.generationEvents?.length ?? 0) > 0
                ? message.generationEvents
                : existing.generationEvents,
    };
    return nextMessages;
};

/** Apply `update` to one Room, tracking its high-water sequence. */
const withRoom = (
    rooms: Record<string, ChatGroupRealtimeState>,
    chatGroupId: string,
    sequence: number,
    update: (prev: ChatGroupRealtimeState) => ChatGroupRealtimeState,
): Record<string, ChatGroupRealtimeState> => {
    const updated = update(
        rooms[chatGroupId] ?? DEFAULT_CHAT_GROUP_STATE(chatGroupId),
    );
    return {
        ...rooms,
        [chatGroupId]: {
            ...updated,
            lastSequence:
                sequence > 0
                    ? Math.max(updated.lastSequence, sequence)
                    : updated.lastSequence,
        },
    };
};

/** Fold one party envelope into the Room map; returns the same object when it doesn't concern a Room. */
export const reduceRooms = (
    rooms: Record<string, ChatGroupRealtimeState>,
    envelope: PartyRealtimeEnvelope,
    now: number = Date.now(),
): Record<string, ChatGroupRealtimeState> => {
    if (envelope.type === 'party.snapshot') {
        // Snapshot now carries the two thought-log auxiliary streams alongside
        // messages — pre-gate skips (skipObvious-shaped, mapped into the same
        // DeclinedDecision lane the live `declined` event feeds) and race outcomes.
        // Without these the thought log on reload only shows persisted-message
        // appraisals; every "negative" beat (skip / race-cancel / past-pnr / continue)
        // would silently disappear.
        const payload = envelope.data as {
            chatGroupId?: string;
            messages?: RealtimeChatMessage[];
            skippedTurns?: Array<{
                personaId: string;
                personaName?: string | null;
                triggeredByMessageId: number;
                urgeTotal?: number;
                reason?: string | null;
                sendAt?: number;
            }>;
            raceEvaluations?: RaceEvaluation[];
        };

        if (!payload.chatGroupId) {
            return rooms;
        }

        const seededDeclined: DeclinedDecision[] = (
            payload.skippedTurns ?? []
        ).map((s) => ({
            personaId: s.personaId,
            reason: s.reason ?? null,
            decisionText: '',
            messageId: s.triggeredByMessageId,
            timestamp: s.sendAt ?? 0,
        }));

        return withRoom(
            rooms,
            payload.chatGroupId,
            envelope.sequence,
            (prev) => ({
                ...prev,
                messages: (payload.messages ?? []).sort(
                    (a, b) => a.messageId - b.messageId,
                ),
                activeGenerationMessageIds: [],
                declinedDecisions: seededDeclined,
                raceEvaluations: payload.raceEvaluations ?? [],
            }),
        );
    }

    if (envelope.type === 'party.race.evaluation') {
        // Stop-signal race outcome from PersonaGrain. Accumulate into thoughtLog
        // alongside go/skip entries so race activity is visible in the sidebar.
        const payload = envelope.data as {
            chatGroupId?: string;
            evaluation?: RaceEvaluation;
        };
        if (!payload.chatGroupId || !payload.evaluation) return rooms;
        const evaluation = payload.evaluation;
        return withRoom(
            rooms,
            payload.chatGroupId,
            envelope.sequence,
            (prev) => ({
                ...prev,
                raceEvaluations: [...prev.raceEvaluations, evaluation],
            }),
        );
    }

    if (envelope.type === 'party.message.created') {
        const payload = envelope.data as {
            chatGroupId?: string;
            message?: RealtimeChatMessage | null;
        };

        if (!payload.chatGroupId || !payload.message) {
            return rooms;
        }

        const chatGroupId = payload.chatGroupId;
        const message = payload.message;

        return withRoom(rooms, chatGroupId, envelope.sequence, (prev) => ({
            ...prev,
            messages: appendOrUpdateMessage(prev.messages, message),
        }));
    }

    if (envelope.type === 'party.message.deleted') {
        const payload = envelope.data as {
            chatGroupId?: string;
            messageId?: number | null;
        };

        if (!payload.chatGroupId || payload.messageId == null) {
            return rooms;
        }

        const chatGroupId = payload.chatGroupId;
        const messageId = payload.messageId;

        return withRoom(rooms, chatGroupId, envelope.sequence, (prev) => ({
            ...prev,
            messages: prev.messages.filter(
                (message) => message.messageId !== messageId,
            ),
            activeGenerationMessageIds: prev.activeGenerationMessageIds.filter(
                (id) => id !== messageId,
            ),
        }));
    }

    if (envelope.type === 'party.messages.truncated') {
        const payload = envelope.data as {
            chatGroupId?: string;
            messageId?: number | null;
        };

        if (!payload.chatGroupId || payload.messageId == null) {
            return rooms;
        }

        const chatGroupId = payload.chatGroupId;
        const messageId = payload.messageId;

        return withRoom(rooms, chatGroupId, envelope.sequence, (prev) => ({
            ...prev,
            messages: prev.messages.filter(
                (message) => message.messageId <= messageId,
            ),
            activeGenerationMessageIds: prev.activeGenerationMessageIds.filter(
                (id) => id <= messageId,
            ),
        }));
    }

    if (envelope.type === 'party.generation.started') {
        const payload = envelope.data as {
            chatGroupId?: string;
            messageId?: number;
        };

        if (!payload.chatGroupId || payload.messageId == null) {
            return rooms;
        }

        const chatGroupId = payload.chatGroupId;
        const messageId = payload.messageId;

        return withRoom(rooms, chatGroupId, envelope.sequence, (prev) => ({
            ...prev,
            activeGenerationMessageIds: Array.from(
                new Set(prev.activeGenerationMessageIds.concat(messageId)),
            ),
        }));
    }

    if (envelope.type === 'party.generation.delta') {
        const payload = envelope.data as {
            chatGroupId?: string;
            messageId?: number;
            event?: string;
            data?: string;
            done?: boolean;
        };

        if (!payload.chatGroupId || payload.messageId == null) {
            return rooms;
        }

        const chatGroupId = payload.chatGroupId;
        const messageId = payload.messageId;

        return withRoom(rooms, chatGroupId, envelope.sequence, (prev) => {
            const existing = prev.messages.find(
                (message) => message.messageId === messageId,
            );

            const resolvedSenderId =
                existing?.senderId ??
                (payload.event === 'attend' ? payload.data : null) ??
                existing?.generationEvents?.find((e) => e.event === 'attend')
                    ?.data ??
                '00000000-0000-0000-0000-000000000000';

            const baseMessage: RealtimeChatMessage = existing ?? {
                chatGroupId,
                messageId,
                content: null,
                reasoning: null,
                appraisal: null,
                error: null,
                senderType: 'assistant',
                senderId: resolvedSenderId,
                sendAt: null,
                metadata: null,
                generationEvents: [],
            };

            let nextMessage = baseMessage;

            const eventEntry = {
                event: payload.event ?? 'unknown',
                data: payload.data ?? '',
                at: envelope.timestamp,
            };

            if (payload.event === 'message') {
                nextMessage = {
                    ...baseMessage,
                    content: `${baseMessage.content ?? ''}${payload.data ?? ''}`,
                };
            } else if (payload.event === 'reasoning') {
                nextMessage = {
                    ...baseMessage,
                    reasoning: `${baseMessage.reasoning ?? ''}${payload.data ?? ''}`,
                };
            } else if (payload.event === 'error') {
                nextMessage = {
                    ...baseMessage,
                    error: payload.data ?? baseMessage.error,
                    generationEvents: [
                        ...baseMessage.generationEvents,
                        eventEntry,
                    ],
                };
            } else if (payload.event === 'appraisal') {
                // Accumulate appraisal chunks for realtime display
                nextMessage = {
                    ...baseMessage,
                    appraisal: `${baseMessage.appraisal ?? ''}${payload.data ?? ''}`,
                    generationEvents: [
                        ...baseMessage.generationEvents,
                        eventEntry,
                    ],
                };
            } else if (payload.event === 'appraisalComplete') {
                // Replace accumulated chunks with clean parsed JSON
                nextMessage = {
                    ...baseMessage,
                    appraisal: payload.data ?? null,
                    generationEvents: [
                        ...baseMessage.generationEvents,
                        eventEntry,
                    ],
                };
            } else if (payload.event === 'attend') {
                // First event that carries the real persona ID — use it as senderId
                nextMessage = {
                    ...baseMessage,
                    senderId: payload.data ?? baseMessage.senderId,
                    generationEvents: [
                        ...baseMessage.generationEvents,
                        eventEntry,
                    ],
                };
            } else if (payload.event === 'retry') {
                // Backend is about to retry generation after a transient failure.
                // Clear any partial content/reasoning we buffered during the failed
                // attempt so the next stream starts from a clean slate.
                nextMessage = {
                    ...baseMessage,
                    content: '',
                    reasoning: null,
                    error: null,
                    generationEvents: [
                        ...baseMessage.generationEvents,
                        eventEntry,
                    ],
                };
            } else if (payload.event === 'declined') {
                // Persona decided not to respond — capture decision then remove phantom message.
                // Authoritative personaId lives in the payload JSON; the phantom's senderId is
                // a fallback only — the persisted slot's SenderId is cleared to Guid.Empty on
                // decline (see ChatGroupGenerationStoppedEvent), so falling through to it
                // surfaced "00000000" entries in the thought log.
                const phantom = prev.messages.find(
                    (m) => m.messageId === messageId,
                );
                let declinedReason: string | null = null;
                let declinedPersonaId = '';
                try {
                    const parsed = JSON.parse(payload.data ?? '{}');
                    declinedReason = parsed.reason ?? null;
                    declinedPersonaId =
                        parsed.personaId ?? parsed.PersonaId ?? '';
                } catch {
                    /* ignore */
                }
                const declined: DeclinedDecision = {
                    personaId: declinedPersonaId || phantom?.senderId || '',
                    reason: declinedReason,
                    decisionText: (phantom?.appraisal ?? '').trim(),
                    messageId,
                    timestamp: now,
                };
                return {
                    ...prev,
                    messages: prev.messages.filter(
                        (m) => m.messageId !== messageId,
                    ),
                    activeGenerationMessageIds:
                        prev.activeGenerationMessageIds.filter(
                            (id) => id !== messageId,
                        ),
                    declinedDecisions: [...prev.declinedDecisions, declined],
                };
            } else {
                nextMessage = {
                    ...baseMessage,
                    generationEvents: [
                        ...baseMessage.generationEvents,
                        eventEntry,
                    ],
                };
            }

            return {
                ...prev,
                messages: appendOrUpdateMessage(prev.messages, nextMessage),
                activeGenerationMessageIds: payload.done
                    ? prev.activeGenerationMessageIds.filter(
                          (id) => id !== messageId,
                      )
                    : Array.from(
                          new Set(
                              prev.activeGenerationMessageIds.concat(messageId),
                          ),
                      ),
            };
        });
    }

    if (envelope.type === 'party.generation.completed') {
        const payload = envelope.data as {
            chatGroupId?: string;
            messageId?: number;
        };

        if (!payload.chatGroupId || payload.messageId == null) {
            return rooms;
        }

        const chatGroupId = payload.chatGroupId;
        const messageId = payload.messageId;

        return withRoom(rooms, chatGroupId, envelope.sequence, (prev) => ({
            ...prev,
            activeGenerationMessageIds: prev.activeGenerationMessageIds.filter(
                (id) => id !== messageId,
            ),
        }));
    }
    return rooms;
};

export type GenerationPhase =
    | { phase: 'idle' }
    | { phase: 'waiting' }
    | { phase: 'deciding'; personaName: string; decisionText: string }
    | { phase: 'typing'; personaName: string }
    | { phase: 'streaming'; personaName: string; charCount: number };

export type ActiveGenerationPhase = GenerationPhase & { messageId?: number };

export const phaseOf = (
    group: ChatGroupRealtimeState,
    activeId: number,
): ActiveGenerationPhase => {
    const message = group.messages.find((m) => m.messageId === activeId);
    if (!message) return { phase: 'waiting', messageId: activeId };

    const events = message.generationEvents ?? [];
    if (events.find((e) => e.event === 'overseerStop'))
        return { phase: 'idle', messageId: activeId };
    if (events.find((e) => e.event === 'declined'))
        return { phase: 'idle', messageId: activeId };

    const attendEvent = [...events].reverse().find((e) => e.event === 'attend');
    if (!attendEvent) return { phase: 'waiting', messageId: activeId };

    const personaName = attendEvent.data;

    // Check if still in the appraisal phase (has appraisal chunks but no appraisalComplete yet)
    const hasAppraisalComplete = events.some(
        (e) => e.event === 'appraisalComplete',
    );
    if (!hasAppraisalComplete) {
        const decisionText = (message.appraisal ?? '').trim();
        return {
            phase: 'deciding',
            personaName,
            decisionText,
            messageId: activeId,
        };
    }

    const msgCount = events.filter((e) => e.event === 'message').length;

    if (msgCount < 3)
        return { phase: 'typing', personaName, messageId: activeId };
    return {
        phase: 'streaming',
        personaName,
        charCount: (message.content ?? '').length,
        messageId: activeId,
    };
};
