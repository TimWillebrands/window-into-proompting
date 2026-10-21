import deepEqual from 'fast-deep-equal';
import { create } from 'zustand';
import { useStoreWithEqualityFn } from 'zustand/traditional';
import {
    type ActiveGenerationPhase,
    type ChatGroupRealtimeState,
    type DeclinedDecision,
    type GenerationPhase,
    type PartyRealtimeEnvelope,
    phaseOf,
    type RaceEvaluation,
    type RealtimeChatMessage,
    reduceRooms,
} from './room-state';
import { worldBus } from './world-bus';

export type {
    ActiveGenerationPhase,
    ChatMessageMetadata,
    DeclinedDecision,
    GenerationPhase,
    RaceEvaluation,
    RealtimeChatMessage,
} from './room-state';

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

export const EMPTY_MESSAGES: RealtimeChatMessage[] = [];
const EMPTY_GENERATION_MESSAGE_IDS: number[] = [];

interface RealtimeStoreState {
    connections: Record<string, PartyRealtimeConnection>;
    chatGroups: Record<string, ChatGroupRealtimeState>;
    connectPartyRealtime: (partyId: string) => void;
    disconnectPartyRealtime: (partyId: string) => void;
}

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

        if (
            envelope.type.startsWith('world.') ||
            envelope.type === 'party.message.created'
        ) {
            worldBus.emit(envelope);
        }

        set((state) => {
            const chatGroups = reduceRooms(state.chatGroups, envelope);
            return chatGroups === state.chatGroups ? state : { chatGroups };
        });
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
            return phaseOf(group, activeId);
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
                .map((id) => phaseOf(group, id))
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

const EMPTY_RACE_EVALUATIONS: RaceEvaluation[] = [];
export const useRaceEvaluations = (chatGroupId: string): RaceEvaluation[] =>
    useRealtimeStore(
        (state) =>
            state.chatGroups[chatGroupId]?.raceEvaluations ??
            EMPTY_RACE_EVALUATIONS,
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
