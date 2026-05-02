import deepEqual from 'fast-deep-equal';
import { create } from 'zustand';
import { useStoreWithEqualityFn } from 'zustand/traditional';

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
}

export type RealtimeConnectionStatus =
    | 'disconnected'
    | 'connecting'
    | 'connected'
    | 'reconnecting';

interface PartyRealtimeConnection {
    status: RealtimeConnectionStatus;
    socket: WebSocket | null;
    lastSequence: number;
    reconnectAttempt: number;
    subscriberCount: number;
}

const DEFAULT_CONNECTION_STATE: PartyRealtimeConnection = {
    status: 'disconnected',
    socket: null,
    lastSequence: 0,
    reconnectAttempt: 0,
    subscriberCount: 0,
};

export interface DeclinedDecision {
    personaId: string;
    reason: string | null;
    decisionText: string;
    messageId: number;
    timestamp: number;
}

interface ChatGroupRealtimeState {
    chatGroupId: string;
    messages: RealtimeChatMessage[];
    activeGenerationMessageIds: number[];
    declinedDecisions: DeclinedDecision[];
    lastSequence: number;
}

type PartyRealtimeEnvelope = {
    type: string;
    sequence: number;
    timestamp: number;
    data: unknown;
};

const DEFAULT_CHAT_GROUP_STATE = (
    chatGroupId: string,
): ChatGroupRealtimeState => ({
    chatGroupId,
    messages: [],
    activeGenerationMessageIds: [],
    declinedDecisions: [],
    lastSequence: 0,
});

export const EMPTY_MESSAGES: RealtimeChatMessage[] = [];
const EMPTY_GENERATION_MESSAGE_IDS: number[] = [];

interface RealtimeStoreState {
    connections: Record<string, PartyRealtimeConnection>;
    chatGroups: Record<string, ChatGroupRealtimeState>;
    connectPartyRealtime: (partyId: string) => void;
    disconnectPartyRealtime: (partyId: string) => void;
}

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

export const useRealtimeStore = create<RealtimeStoreState>((set, get) => {
    const establishPartyRealtime = (partyId: string) => {
        if (typeof window === 'undefined') {
            return;
        }

        const existing = get().connections[partyId];
        if (
            existing &&
            (existing.status === 'connecting' ||
                existing.status === 'connected')
        ) {
            return;
        }

        if (existing && existing.subscriberCount === 0) {
            return;
        }

        const httpUrl = new URL(
            `/api/Party/${partyId}/ws`,
            window.location.href,
        );
        const wsUrl = `${
            httpUrl.protocol === 'https:' ? 'wss:' : 'ws:'
        }//${httpUrl.host}${httpUrl.pathname}${httpUrl.search}`;

        const socket = new WebSocket(wsUrl);

        set((state) => ({
            connections: {
                ...state.connections,
                [partyId]: {
                    ...(state.connections[partyId] ?? DEFAULT_CONNECTION_STATE),
                    status: 'connecting',
                    socket,
                },
            },
        }));

        socket.addEventListener('open', () => {
            set((state) => ({
                connections: {
                    ...state.connections,
                    [partyId]: {
                        ...(state.connections[partyId] ??
                            DEFAULT_CONNECTION_STATE),
                        status: 'connected',
                        reconnectAttempt: 0,
                        socket,
                    },
                },
            }));
        });

        socket.addEventListener('message', (event) => {
            if (typeof event.data !== 'string') {
                return;
            }

            try {
                const envelope = JSON.parse(
                    event.data,
                ) as PartyRealtimeEnvelope;
                handleEnvelope(partyId, envelope);
            } catch {
                // Ignore malformed payloads.
            }
        });

        socket.addEventListener('close', () => {
            const current = get().connections[partyId];
            if (!current) {
                return;
            }

            if (
                current.status === 'disconnected' ||
                current.subscriberCount === 0
            ) {
                return;
            }

            scheduleReconnect(partyId);
        });

        socket.addEventListener('error', () => {
            socket.close();
        });
    };

    const updateChatGroup = (
        chatGroupId: string,
        sequence: number,
        update: (prev: ChatGroupRealtimeState) => ChatGroupRealtimeState,
    ) => {
        set((state) => {
            const previous =
                state.chatGroups[chatGroupId] ??
                DEFAULT_CHAT_GROUP_STATE(chatGroupId);

            const updated = update(previous);

            return {
                chatGroups: {
                    ...state.chatGroups,
                    [chatGroupId]: {
                        ...updated,
                        lastSequence:
                            sequence > 0
                                ? Math.max(updated.lastSequence, sequence)
                                : updated.lastSequence,
                    },
                },
            };
        });
    };

    const handleEnvelope = (
        partyId: string,
        envelope: PartyRealtimeEnvelope,
    ) => {
        set((state) => ({
            connections: {
                ...state.connections,
                [partyId]: {
                    ...(state.connections[partyId] ?? DEFAULT_CONNECTION_STATE),
                    lastSequence: Math.max(
                        state.connections[partyId]?.lastSequence ?? 0,
                        envelope.sequence,
                    ),
                },
            },
        }));

        if (envelope.type === 'party.snapshot') {
            const payload = envelope.data as {
                chatGroupId?: string;
                messages?: RealtimeChatMessage[];
            };

            if (!payload.chatGroupId) {
                return;
            }

            updateChatGroup(payload.chatGroupId, envelope.sequence, (prev) => ({
                ...prev,
                messages: (payload.messages ?? []).sort(
                    (a, b) => a.messageId - b.messageId,
                ),
                activeGenerationMessageIds: [],
                declinedDecisions: [],
            }));
            return;
        }

        if (envelope.type === 'party.message.created') {
            const payload = envelope.data as {
                chatGroupId?: string;
                message?: RealtimeChatMessage | null;
            };

            if (!payload.chatGroupId || !payload.message) {
                return;
            }

            const chatGroupId = payload.chatGroupId;
            const message = payload.message;

            updateChatGroup(chatGroupId, envelope.sequence, (prev) => ({
                ...prev,
                messages: appendOrUpdateMessage(prev.messages, message),
            }));
            return;
        }

        if (envelope.type === 'party.message.deleted') {
            const payload = envelope.data as {
                chatGroupId?: string;
                messageId?: number | null;
            };

            if (!payload.chatGroupId || payload.messageId == null) {
                return;
            }

            const chatGroupId = payload.chatGroupId;
            const messageId = payload.messageId;

            updateChatGroup(chatGroupId, envelope.sequence, (prev) => ({
                ...prev,
                messages: prev.messages.filter(
                    (message) => message.messageId !== messageId,
                ),
                activeGenerationMessageIds:
                    prev.activeGenerationMessageIds.filter(
                        (id) => id !== messageId,
                    ),
            }));
            return;
        }

        if (envelope.type === 'party.messages.truncated') {
            const payload = envelope.data as {
                chatGroupId?: string;
                messageId?: number | null;
            };

            if (!payload.chatGroupId || payload.messageId == null) {
                return;
            }

            const chatGroupId = payload.chatGroupId;
            const messageId = payload.messageId;

            updateChatGroup(chatGroupId, envelope.sequence, (prev) => ({
                ...prev,
                messages: prev.messages.filter(
                    (message) => message.messageId <= messageId,
                ),
                activeGenerationMessageIds:
                    prev.activeGenerationMessageIds.filter(
                        (id) => id <= messageId,
                    ),
            }));
            return;
        }

        if (envelope.type === 'party.generation.started') {
            const payload = envelope.data as {
                chatGroupId?: string;
                messageId?: number;
            };

            if (!payload.chatGroupId || payload.messageId == null) {
                return;
            }

            const chatGroupId = payload.chatGroupId;
            const messageId = payload.messageId;

            updateChatGroup(chatGroupId, envelope.sequence, (prev) => ({
                ...prev,
                activeGenerationMessageIds: Array.from(
                    new Set(prev.activeGenerationMessageIds.concat(messageId)),
                ),
            }));
            return;
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
                return;
            }

            const chatGroupId = payload.chatGroupId;
            const messageId = payload.messageId;

            updateChatGroup(chatGroupId, envelope.sequence, (prev) => {
                const existing = prev.messages.find(
                    (message) => message.messageId === messageId,
                );

                const resolvedSenderId =
                    existing?.senderId ??
                    (payload.event === 'attend' ? payload.data : null) ??
                    existing?.generationEvents?.find(
                        (e) => e.event === 'attend',
                    )?.data ??
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
                        timestamp: Date.now(),
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
                        declinedDecisions: [
                            ...prev.declinedDecisions,
                            declined,
                        ],
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
                                  prev.activeGenerationMessageIds.concat(
                                      messageId,
                                  ),
                              ),
                          ),
                };
            });
            return;
        }

        if (envelope.type === 'party.generation.completed') {
            const payload = envelope.data as {
                chatGroupId?: string;
                messageId?: number;
            };

            if (!payload.chatGroupId || payload.messageId == null) {
                return;
            }

            const chatGroupId = payload.chatGroupId;
            const messageId = payload.messageId;

            updateChatGroup(chatGroupId, envelope.sequence, (prev) => ({
                ...prev,
                activeGenerationMessageIds:
                    prev.activeGenerationMessageIds.filter(
                        (id) => id !== messageId,
                    ),
            }));
        }
    };

    const scheduleReconnect = (partyId: string) => {
        const connection = get().connections[partyId];
        const reconnectAttempt = (connection?.reconnectAttempt ?? 0) + 1;
        const delayMs = Math.min(1000 * 2 ** reconnectAttempt, 10_000);

        set((state) => ({
            connections: {
                ...state.connections,
                [partyId]: {
                    ...(state.connections[partyId] ?? DEFAULT_CONNECTION_STATE),
                    status: 'reconnecting',
                    reconnectAttempt,
                    socket: null,
                },
            },
        }));

        window.setTimeout(() => {
            establishPartyRealtime(partyId);
        }, delayMs);
    };

    return {
        connections: {},
        chatGroups: {},
        connectPartyRealtime: (partyId: string) => {
            set((state) => ({
                connections: {
                    ...state.connections,
                    [partyId]: {
                        ...(state.connections[partyId] ??
                            DEFAULT_CONNECTION_STATE),
                        subscriberCount:
                            (state.connections[partyId]?.subscriberCount ?? 0) +
                            1,
                    },
                },
            }));

            queueMicrotask(() => establishPartyRealtime(partyId));
        },
        disconnectPartyRealtime: (partyId: string) => {
            const connection = get().connections[partyId];
            if (!connection) {
                return;
            }

            const nextSubscriberCount = Math.max(
                connection.subscriberCount - 1,
                0,
            );
            if (nextSubscriberCount > 0) {
                set((state) => ({
                    connections: {
                        ...state.connections,
                        [partyId]: {
                            ...(state.connections[partyId] ??
                                DEFAULT_CONNECTION_STATE),
                            subscriberCount: nextSubscriberCount,
                        },
                    },
                }));
                return;
            }

            if (connection.socket) {
                connection.socket.close();
            }

            set((state) => ({
                connections: {
                    ...state.connections,
                    [partyId]: {
                        ...DEFAULT_CONNECTION_STATE,
                        subscriberCount: 0,
                    },
                },
            }));
        },
    };
});

export const useRealtimeConnectionStatus = (
    partyId: string,
): RealtimeConnectionStatus =>
    useRealtimeStore(
        (state) => state.connections[partyId]?.status ?? 'disconnected',
    );

export type GenerationPhase =
    | { phase: 'idle' }
    | { phase: 'waiting' }
    | { phase: 'deciding'; personaName: string; decisionText: string }
    | { phase: 'typing'; personaName: string }
    | { phase: 'streaming'; personaName: string; charCount: number };

export type ActiveGenerationPhase = GenerationPhase & { messageId?: number };

const getPhaseForMessage = (
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

/** Returns a single GenerationPhase for the most recent active generation (backward compat). */
export const useActiveGenerationInfo = (chatGroupId: string): GenerationPhase =>
    useStoreWithEqualityFn(
        useRealtimeStore,
        (state) => {
            const group = state.chatGroups[chatGroupId];
            if (!group || group.activeGenerationMessageIds.length === 0)
                return { phase: 'idle' };

            const activeId =
                group.activeGenerationMessageIds[
                    group.activeGenerationMessageIds.length - 1
                ];
            return getPhaseForMessage(group, activeId);
        },
        deepEqual,
    );

/** Returns an array of GenerationPhases — one per active generation stream. */
export const useActiveGenerationPhases = (
    chatGroupId: string,
): ActiveGenerationPhase[] =>
    useStoreWithEqualityFn(
        useRealtimeStore,
        (state) => {
            const group = state.chatGroups[chatGroupId];
            if (!group || group.activeGenerationMessageIds.length === 0)
                return [];

            return group.activeGenerationMessageIds
                .map((id) => getPhaseForMessage(group, id))
                .filter((p) => p.phase !== 'idle');
        },
        deepEqual,
    );

export const useChatGroupMessages = (chatGroupId: string) =>
    useRealtimeStore(
        (state) => state.chatGroups[chatGroupId]?.messages ?? EMPTY_MESSAGES,
    );

export const useChatGroupGenerationState = (chatGroupId: string) =>
    useRealtimeStore(
        (state) =>
            state.chatGroups[chatGroupId]?.activeGenerationMessageIds ??
            EMPTY_GENERATION_MESSAGE_IDS,
    );

const EMPTY_DECLINED: DeclinedDecision[] = [];
export const useDeclinedDecisions = (chatGroupId: string): DeclinedDecision[] =>
    useRealtimeStore(
        (state) =>
            state.chatGroups[chatGroupId]?.declinedDecisions ?? EMPTY_DECLINED,
    );

export function useRealtimeStoreActions() {
    const connectPartyRealtime = useRealtimeStore(
        (state) => state.connectPartyRealtime,
    );
    const disconnectPartyRealtime = useRealtimeStore(
        (state) => state.disconnectPartyRealtime,
    );

    return {
        connectPartyRealtime,
        disconnectPartyRealtime,
    };
}
