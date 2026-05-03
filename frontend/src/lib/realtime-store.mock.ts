import { fn } from 'storybook/test';
import type {
    ActiveGenerationPhase,
    DeclinedDecision,
    GenerationPhase,
    RaceEvaluation,
    RealtimeChatMessage,
    RealtimeConnectionStatus,
} from './realtime-store';

export type {
    ActiveGenerationPhase,
    DeclinedDecision,
    GenerationPhase,
    RaceEvaluation,
    RealtimeChatMessage,
    RealtimeConnectionStatus,
} from './realtime-store';

export const EMPTY_MESSAGES: RealtimeChatMessage[] = [];

export const mockChatMessages: RealtimeChatMessage[] = [
    {
        chatGroupId: 'cg-1',
        messageId: 1,
        content: 'Welcome to the standup. Status updates please.',
        reasoning: null,
        appraisal: null,
        error: null,
        senderType: 'user',
        senderId: 'user-1',
        sendAt: Date.parse('2026-04-22T09:00:00Z'),
        generationEvents: [],
    },
    {
        chatGroupId: 'cg-1',
        messageId: 2,
        content:
            'Shipped the seedling tracker yesterday. Today: dial in the watering schedule heuristic. 🚀',
        reasoning: 'Indie hacker mode — concise, action-led updates.',
        appraisal: null,
        error: null,
        senderType: 'assistant',
        senderId: 'persona-1',
        sendAt: Date.parse('2026-04-22T09:00:30Z'),
        generationEvents: [],
    },
];

export const useChatGroupMessages = fn(
    (_chatGroupId: string): RealtimeChatMessage[] => mockChatMessages,
);

export const useChatGroupGenerationState = fn(
    (_chatGroupId: string): number[] => [],
);

export const useDeclinedDecisions = fn(
    (_chatGroupId: string): DeclinedDecision[] => [],
);

export const useRaceEvaluations = fn(
    (_chatGroupId: string): RaceEvaluation[] => [],
);

export const useActiveGenerationInfo = fn(
    (_chatGroupId: string): GenerationPhase => ({ phase: 'idle' }),
);

export const useActiveGenerationPhases = fn(
    (_chatGroupId: string): ActiveGenerationPhase[] => [],
);

export const useRealtimeConnectionStatus = fn(
    (_partyId: string): RealtimeConnectionStatus => 'connected',
);

export const useRealtimeStoreActions = fn(() => ({
    connectPartyRealtime: fn(),
    disconnectPartyRealtime: fn(),
}));

// Stub the underlying zustand store too — anything calling .getState() etc.
export const useRealtimeStore = Object.assign(
    fn((selector?: (state: unknown) => unknown) =>
        selector ? selector({}) : {},
    ),
    {
        getState: () => ({
            connections: {},
            chatGroups: {},
            connectPartyRealtime: () => {},
            disconnectPartyRealtime: () => {},
        }),
        setState: fn(),
        subscribe: fn(() => () => {}),
    },
);
