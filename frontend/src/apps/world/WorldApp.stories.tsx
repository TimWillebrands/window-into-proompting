import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    getGetLlmConfigUsageQueryKey,
    getGetWorldsPartyIdActivitiesQueryKey,
    getGetWorldsPartyIdQueryKey,
} from '#api/party-zone';
import { seeded, withStoryLives } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WORLD_ID } from '../../lib/world';
import WorldApp from './WorldApp';

const now = Date.now();

const status = {
    partyId: WORLD_ID,
    running: true,
    tickSeconds: 30,
    maxActionsPerHour: 20,
    actionsLastHour: 14,
    lastTickAt: now - 25_000,
    ambientMinutes: 10,
};

const activities = [
    {
        id: 'a1',
        ts: now - 60_000,
        personaId: 'persona-ada',
        personaName: 'Ada',
        app: 'board',
        action: 'post',
        summary: 'Posted "Who took the kettle?" on the Board',
        thought: 'Somebody must know where it went.',
        targetId: 'thread-1',
    },
    {
        id: 'a2',
        ts: now - 600_000,
        personaId: 'persona-linus',
        personaName: 'Linus',
        app: 'life',
        action: 'rest',
        summary: 'Took a nap',
    },
    {
        id: 'a3',
        ts: now - 900_000,
        personaId: 'persona-ada',
        personaName: 'Ada',
        app: 'messenger',
        action: 'error',
        summary: 'Could not reach the model',
    },
];

const usage = [
    {
        providerId: 'openrouter',
        modelName: 'tiny-model',
        usageUsd: 0.42,
        limitUsd: 5,
    },
];

const SIZES = {
    large: { width: 1000, height: 680 },
    regular: { width: 820, height: 560 },
    narrow: { width: 480, height: 640 },
} as const;

const meta = {
    title: 'Apps/WorldApp',
    component: WorldApp,
    parameters: { layout: 'fullscreen' },
    beforeEach: withStoryLives,
    decorators: [
        (Story, { parameters }) => {
            const size =
                SIZES[(parameters.size as keyof typeof SIZES) ?? 'large'];
            return (
                <div
                    className="p-6"
                    style={{ width: size.width + 40, height: size.height + 40 }}
                >
                    {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                    <StoryWindow
                        id="world"
                        title="World"
                        icon="/img/icons/world.ico"
                    >
                        <Story />
                    </StoryWindow>
                </div>
            );
        },
        seeded([
            [getGetWorldsPartyIdQueryKey(WORLD_ID), status],
            [
                getGetWorldsPartyIdActivitiesQueryKey(WORLD_ID, { limit: 100 }),
                activities,
            ],
            [getGetLlmConfigUsageQueryKey(), usage],
        ]),
    ],
} satisfies Meta<typeof WorldApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Regular: Story = { parameters: { size: 'regular' } };

export const Narrow: Story = { parameters: { size: 'narrow' } };
