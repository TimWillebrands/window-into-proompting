import type { Decorator } from '@storybook/react-vite';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
    getGetWorldsPartyIdMessengerQueryKey,
    getGetWorldsPartyIdPlacesQueryKey,
    getGetWorldsPartyIdSelvesQueryKey,
    useGetWorldsPartyIdLivesSuspense,
} from '#api/party-zone';
import type { DirectChatSummary } from '../../api/model/directChatSummary';
import type { LifeSummary } from '../../api/model/lifeSummary';
import type { SelfSummary } from '../../api/model/selfSummary';
import { usePlayerSession, WORLD_ID } from '../../lib/world';

export const STORY_PLAYER = 'player-grace';

const now = Date.now();

export const storyLives: LifeSummary[] = [
    {
        personaId: STORY_PLAYER,
        name: 'Grace',
        driver: 'User',
        chattiness: 0.6,
        busy: false,
        isPlayer: true,
        lastActivity: null,
    },
    {
        personaId: 'persona-ada',
        name: 'Ada',
        driver: 'LLM',
        chattiness: 0.8,
        busy: true,
        isPlayer: false,
        lastActivity: {
            id: 'act-1',
            ts: now - 120_000,
            app: 'board',
            action: 'post',
            summary: 'Posted "Who took the kettle?" on the Board',
            targetId: 'thread-1',
        },
    },
    {
        personaId: 'persona-linus',
        name: 'Linus',
        driver: 'LLM',
        chattiness: 0.3,
        busy: false,
        isPlayer: false,
        lastActivity: {
            id: 'act-2',
            ts: now - 3_600_000,
            app: 'diary',
            action: 'write',
            summary: 'Wrote in their diary',
        },
    },
    {
        personaId: 'persona-hedy',
        name: 'Hedy',
        driver: 'LLM',
        chattiness: 0.5,
        busy: false,
        afk: true,
        isPlayer: false,
        lastActivity: null,
    },
] as LifeSummary[];

export const storySelves: SelfSummary[] = [
    {
        personaId: 'persona-ada',
        name: 'Ada',
        mood: 'curious',
        energy: 'high',
        preoccupation: null,
        battery: 0.9,
        availability: 0.8,
        doing: 'at the café',
        day: [],
        threads: ['Looking for the kettle', 'Planning a picnic'],
        fed: 0.3,
        haunts: ['Café', 'Library'],
    },
    {
        personaId: 'persona-linus',
        name: 'Linus',
        mood: 'calm',
        energy: 'low',
        preoccupation: null,
        battery: 0.4,
        availability: 0.6,
        doing: 'Asleep',
        day: [],
        threads: [],
    },
];

export const storyChats: DirectChatSummary[] = [
    {
        chatGroupId: 'dm-ada',
        withId: 'persona-ada',
        withName: 'Ada',
        lastMessage: 'are you around tonight? :)',
        lastSenderId: 'persona-ada',
        lastAt: now - 300_000,
    },
    {
        chatGroupId: 'dm-linus',
        withId: 'persona-linus',
        withName: 'Linus',
        lastMessage: 'see you then',
        lastSenderId: STORY_PLAYER,
        lastAt: now - 86_400_000 * 2,
    },
];

const wrap = <T,>(data: T) => ({ data, status: 200, headers: new Headers() });

export function seeded(
    entries: ReadonlyArray<readonly [readonly unknown[], unknown]> = [],
): Decorator {
    return (Story) => {
        const client = new QueryClient({
            defaultOptions: {
                queries: {
                    retry: false,
                    staleTime: Number.POSITIVE_INFINITY,
                    gcTime: Number.POSITIVE_INFINITY,
                },
                mutations: { retry: false },
            },
        });
        usePlayerSession.setState({ userId: 'story-user', hydrated: true });
        client.setQueryData(['world-player', WORLD_ID, 'story-user'], {
            playerId: STORY_PLAYER,
            name: 'Grace',
            auto: false,
            isAdmin: true,
        });
        client.setQueryData(
            getGetWorldsPartyIdPlacesQueryKey(WORLD_ID),
            wrap({ bodies: [] }),
        );
        client.setQueryData(
            getGetWorldsPartyIdSelvesQueryKey(WORLD_ID),
            wrap(storySelves),
        );
        client.setQueryData(
            getGetWorldsPartyIdMessengerQueryKey(WORLD_ID, {
                personaId: STORY_PLAYER,
            }),
            wrap(storyChats),
        );
        for (const [key, data] of entries) client.setQueryData(key, wrap(data));
        return (
            <QueryClientProvider client={client}>
                <Story />
            </QueryClientProvider>
        );
    };
}

export const withStoryLives = () => {
    (
        useGetWorldsPartyIdLivesSuspense as unknown as {
            mockReturnValue: (v: unknown) => void;
        }
    ).mockReturnValue({
        data: wrap(storyLives),
        status: 'success',
        isSuccess: true,
    });
};
