import { create } from 'zustand';
import { useShallow } from 'zustand/react/shallow';

export interface RealtimeChatMessage {
    chatGroupId: string;
    messageId: number;
    content: string | null;
    reasoning: string | null;
    overseer: string | null;
    error: string | null;
    senderType: string;
    senderId: string;
    senderName: string | null;
    sendAt: number | null;
    modelEndpointStub: string | null;
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

interface ChatGroupRealtimeState {
    chatGroupId: string;
    messages: RealtimeChatMessage[];
    activeGenerationMessageIds: number[];
    lastSequence: number;
}

type PartyRealtimeEnvelope = {
    type: string;
    sequence: number;
    timestamp: number;
    data: unknown;
};

type ChatGroupListener = (state: ChatGroupRealtimeState) => void;

const DEFAULT_CHAT_GROUP_STATE = (
    chatGroupId: string,
): ChatGroupRealtimeState => ({
    chatGroupId,
    messages: [],
    activeGenerationMessageIds: [],
    lastSequence: 0,
});

const chatGroupListeners = new Map<string, Set<ChatGroupListener>>();
const EMPTY_MESSAGES: RealtimeChatMessage[] = [];
const EMPTY_GENERATION_MESSAGE_IDS: number[] = [];

interface RealtimeStoreState {
    connections: Record<string, PartyRealtimeConnection>;
    chatGroups: Record<string, ChatGroupRealtimeState>;
    connectPartyRealtime: (partyId: string) => void;
    disconnectPartyRealtime: (partyId: string) => void;
    subscribeToChatGroup: (
        chatGroupId: string,
        listener: ChatGroupListener,
    ) => () => void;
}

const emitChatGroupState = (
    chatGroupId: string,
    state: ChatGroupRealtimeState,
) => {
    const listeners = chatGroupListeners.get(chatGroupId);
    if (!listeners) {
        return;
    }

    for (const listener of listeners) {
        listener(state);
    }
};

const appendOrUpdateMessage = (
    messages: RealtimeChatMessage[],
    message: RealtimeChatMessage,
) => {
    const existingIndex = messages.findIndex(
        (entry) => entry.messageId === message.messageId,
    );

    if (existingIndex < 0) {
        return [...messages, message].sort((a, b) => a.messageId - b.messageId);
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

const useRealtimeStore = create<RealtimeStoreState>((set, get) => {
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
                    ...(state.connections[partyId] ?? {
                        lastSequence: 0,
                        reconnectAttempt: 0,
                        socket: null,
                        status: 'disconnected',
                        subscriberCount: 0,
                    }),
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
                        ...(state.connections[partyId] ?? {
                            lastSequence: 0,
                            reconnectAttempt: 0,
                            socket: null,
                            status: 'disconnected',
                            subscriberCount: 0,
                        }),
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
        let nextState: ChatGroupRealtimeState | null = null;

        set((state) => {
            const previous =
                state.chatGroups[chatGroupId] ??
                DEFAULT_CHAT_GROUP_STATE(chatGroupId);

            const updated = update(previous);
            nextState = {
                ...updated,
                lastSequence:
                    sequence > 0
                        ? Math.max(updated.lastSequence, sequence)
                        : updated.lastSequence,
            };

            return {
                chatGroups: {
                    ...state.chatGroups,
                    [chatGroupId]: nextState,
                },
            };
        });

        if (nextState) {
            emitChatGroupState(chatGroupId, nextState);
        }
    };

    const handleEnvelope = (
        partyId: string,
        envelope: PartyRealtimeEnvelope,
    ) => {
        set((state) => ({
            connections: {
                ...state.connections,
                [partyId]: {
                    ...(state.connections[partyId] ?? {
                        status: 'disconnected',
                        socket: null,
                        lastSequence: 0,
                        reconnectAttempt: 0,
                        subscriberCount: 0,
                    }),
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

                const baseMessage: RealtimeChatMessage = existing ?? {
                    chatGroupId,
                    messageId,
                    content: null,
                    reasoning: null,
                    overseer: null,
                    error: null,
                    senderType: 'assistant',
                    senderId: '00000000-0000-0000-0000-000000000000',
                    senderName: null,
                    sendAt: null,
                    modelEndpointStub: null,
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
                        generationEvents: [
                            ...baseMessage.generationEvents,
                            eventEntry,
                        ],
                    };
                } else if (payload.event === 'reasoning') {
                    nextMessage = {
                        ...baseMessage,
                        reasoning: `${baseMessage.reasoning ?? ''}${payload.data ?? ''}`,
                        generationEvents: [
                            ...baseMessage.generationEvents,
                            eventEntry,
                        ],
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
                } else if (payload.event === 'overseer') {
                    // Accumulate overseer chunks for realtime display
                    nextMessage = {
                        ...baseMessage,
                        overseer: `${baseMessage.overseer ?? ''}${payload.data ?? ''}`,
                        generationEvents: [
                            ...baseMessage.generationEvents,
                            eventEntry,
                        ],
                    };
                } else if (payload.event === 'overseerComplete') {
                    // Replace accumulated chunks with clean parsed JSON
                    nextMessage = {
                        ...baseMessage,
                        overseer: payload.data,
                        generationEvents: [
                            ...baseMessage.generationEvents,
                            eventEntry,
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
                    ...(state.connections[partyId] ?? {
                        status: 'disconnected',
                        socket: null,
                        lastSequence: 0,
                        reconnectAttempt: 0,
                        subscriberCount: 0,
                    }),
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
                        ...(state.connections[partyId] ?? {
                            lastSequence: 0,
                            reconnectAttempt: 0,
                            socket: null,
                            status: 'disconnected',
                            subscriberCount: 0,
                        }),
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
                            ...(state.connections[partyId] ?? {
                                lastSequence: 0,
                                reconnectAttempt: 0,
                                socket: null,
                                status: 'disconnected',
                                subscriberCount: 0,
                            }),
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
                        ...(state.connections[partyId] ?? {
                            lastSequence: 0,
                            reconnectAttempt: 0,
                            socket: null,
                            status: 'disconnected',
                            subscriberCount: 0,
                        }),
                        status: 'disconnected',
                        socket: null,
                        reconnectAttempt: 0,
                        subscriberCount: 0,
                    },
                },
            }));
        },
        subscribeToChatGroup: (chatGroupId, listener) => {
            const listeners = chatGroupListeners.get(chatGroupId) ?? new Set();
            listeners.add(listener);
            chatGroupListeners.set(chatGroupId, listeners);

            listener(
                get().chatGroups[chatGroupId] ??
                    DEFAULT_CHAT_GROUP_STATE(chatGroupId),
            );

            return () => {
                const activeListeners = chatGroupListeners.get(chatGroupId);
                if (!activeListeners) {
                    return;
                }

                activeListeners.delete(listener);
                if (activeListeners.size === 0) {
                    chatGroupListeners.delete(chatGroupId);
                }
            };
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
    | { phase: 'overseer' }
    | { phase: 'typing'; personaName: string }
    | { phase: 'streaming'; personaName: string; charCount: number };

export const useActiveGenerationInfo = (chatGroupId: string): GenerationPhase =>
    useRealtimeStore(
        useShallow((state) => {
            const group = state.chatGroups[chatGroupId];
            if (!group || group.activeGenerationMessageIds.length === 0)
                return { phase: 'idle' };

            const activeId =
                group.activeGenerationMessageIds[
                    group.activeGenerationMessageIds.length - 1
                ];
            const message = group.messages.find(
                (m) => m.messageId === activeId,
            );
            if (!message) return { phase: 'overseer' };

            const events = message.generationEvents ?? [];
            if (events.find((e) => e.event === 'overseerStop'))
                return { phase: 'idle' };

            const personaChangeEvent = [...events]
                .reverse()
                .find((e) => e.event === 'personaChange');
            if (!personaChangeEvent) return { phase: 'overseer' };

            const personaName = personaChangeEvent.data;
            const msgCount = events.filter((e) => e.event === 'message').length;

            if (msgCount < 3) return { phase: 'typing', personaName };
            return {
                phase: 'streaming',
                personaName,
                charCount: (message.content ?? '').length,
            };
        }),
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

export async function sendPromptToChatGroup(args: {
    partyId: string;
    chatGroupId: string;
    prompt: string;
    model: string;
    provider: string;
    personaId?: string;
    senderId?: string;
    senderName?: string;
}) {
    const response = await fetch(`/api/Party/${args.partyId}/prompt`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({
            chatGroupId: args.chatGroupId,
            prompt: args.prompt,
            model: args.model,
            provider: args.provider,
            personaId: args.personaId ?? null,
            senderId: args.senderId ?? null,
            senderName: args.senderName ?? null,
        }),
    });

    if (!response.ok) {
        const body = await response.text();
        throw new Error(body || `Prompt failed with status ${response.status}`);
    }
}

export function useRealtimeStoreActions() {
    const connectPartyRealtime = useRealtimeStore(
        (state) => state.connectPartyRealtime,
    );
    const disconnectPartyRealtime = useRealtimeStore(
        (state) => state.disconnectPartyRealtime,
    );
    const subscribeToChatGroup = useRealtimeStore(
        (state) => state.subscribeToChatGroup,
    );

    return {
        connectPartyRealtime,
        disconnectPartyRealtime,
        subscribeToChatGroup,
    };
}
