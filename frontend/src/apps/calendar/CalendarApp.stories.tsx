import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    getGetPartyIdChatGroupsQueryKey,
    getGetWorldsPartyIdGatheringsQueryKey,
} from '#api/party-zone';
import type { ChatGroupInfo, Gathering } from '../../api/model';
import {
    STORY_PLAYER,
    seeded,
    withStoryLives,
} from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WORLD_ID } from '../../lib/world';
import CalendarApp from './CalendarApp';

const now = Date.now();

const rooms: ChatGroupInfo[] = [
    { id: 'room-lobby', name: 'lobby', kind: 'room' },
    { id: 'room-garden', name: 'garden', kind: 'room' },
];

const gatherings: Gathering[] = [
    {
        id: 'g-1',
        hostId: 'persona-ada',
        hostName: 'Ada',
        title: 'Picnic in the park',
        roomId: 'room-garden',
        roomName: 'garden',
        startsAt: now + 45 * 60_000,
        attendeeIds: ['persona-ada', 'persona-linus', STORY_PLAYER],
        status: 'Planned',
    },
    {
        id: 'g-2',
        hostId: 'persona-linus',
        hostName: 'Linus',
        title: 'Lighthouse stories',
        roomId: 'room-lobby',
        roomName: 'lobby',
        startsAt: now + 3 * 3_600_000,
        attendeeIds: ['persona-linus'],
        status: 'Planned',
    },
    {
        id: 'g-3',
        hostId: 'persona-hedy',
        hostName: 'Hedy',
        title: 'Kettle amnesty',
        roomId: 'room-lobby',
        roomName: 'lobby',
        startsAt: now - 86_400_000,
        attendeeIds: ['persona-hedy', 'persona-ada'],
        status: 'Over',
    },
    {
        id: 'g-4',
        hostId: 'persona-ada',
        hostName: 'Ada',
        title: 'Midnight stargazing',
        roomId: 'room-garden',
        roomName: 'garden',
        startsAt: now - 2 * 86_400_000,
        attendeeIds: [],
        status: 'Missed',
    },
];

const meta = {
    title: 'Apps/CalendarApp',
    component: CalendarApp,
    parameters: { layout: 'fullscreen' },
    beforeEach: withStoryLives,
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[560px] w-[440px] p-6'
                        : 'h-[560px] w-[660px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow id="calendar" title="Calendar" icon="calendar">
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded([
            [getGetWorldsPartyIdGatheringsQueryKey(WORLD_ID), gatherings],
            [getGetPartyIdChatGroupsQueryKey(WORLD_ID), rooms],
        ]),
    ],
} satisfies Meta<typeof CalendarApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Empty: Story = {
    decorators: [
        seeded([
            [getGetWorldsPartyIdGatheringsQueryKey(WORLD_ID), []],
            [getGetPartyIdChatGroupsQueryKey(WORLD_ID), rooms],
        ]),
    ],
};

export const Narrow: Story = { parameters: { narrow: true } };
