import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    getGetWorldsPartyIdGatheringsQueryKey,
    getGetWorldsPartyIdPlacesQueryKey,
} from '#api/party-zone';
import type { Gathering, PlacesSnapshot } from '../../api/model';
import {
    STORY_PLAYER,
    seeded,
    withStoryLives,
} from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WORLD_ID } from '../../lib/world';
import CalendarApp from './CalendarApp';

const now = Date.now();

const places: PlacesSnapshot = {
    places: [
        { id: 'place-park', name: 'the Park', kind: 'public' },
        { id: 'place-cafe', name: 'Café Luna', kind: 'public' },
        { id: 'place-home', name: "Grace's Home", kind: 'home' },
    ],
    bodies: [],
};

const gatherings: Gathering[] = [
    {
        id: 'g-1',
        hostId: 'persona-ada',
        hostName: 'Ada',
        title: 'Picnic in the park',
        placeId: 'place-park',
        roomName: 'the Park',
        where: 'at the Park',
        startsAt: now + 45 * 60_000,
        attendeeIds: ['persona-ada', 'persona-linus', STORY_PLAYER],
        status: 'Planned',
    },
    {
        id: 'g-2',
        hostId: 'persona-linus',
        hostName: 'Linus',
        title: 'Lighthouse stories',
        placeId: 'place-cafe',
        roomName: 'Café Luna',
        where: 'at Café Luna',
        startsAt: now + 3 * 3_600_000,
        attendeeIds: ['persona-linus'],
        status: 'Planned',
    },
    {
        id: 'g-3',
        hostId: 'persona-hedy',
        hostName: 'Hedy',
        title: 'Kettle amnesty',
        placeId: 'place-cafe',
        roomName: 'Café Luna',
        where: 'at Café Luna',
        startsAt: now - 86_400_000,
        attendeeIds: ['persona-hedy', 'persona-ada'],
        status: 'Over',
    },
    {
        id: 'g-4',
        hostId: 'persona-ada',
        hostName: 'Ada',
        title: 'Midnight stargazing',
        placeId: 'place-park',
        roomName: 'the Park',
        where: 'at the Park',
        startsAt: now - 2 * 86_400_000,
        attendeeIds: [],
        status: 'Missed',
    },
    {
        id: 'g-5',
        hostId: 'persona-linus',
        hostName: 'Linus',
        title: 'Board games',
        roomName: 'board-games',
        startsAt: now + 2 * 3_600_000,
        attendeeIds: ['persona-linus', 'persona-ada'],
        status: 'Cancelled',
        cancelledBecause: 'it had no venue: gatherings happen at a Place',
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
            [getGetWorldsPartyIdPlacesQueryKey(WORLD_ID), places],
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
            [getGetWorldsPartyIdPlacesQueryKey(WORLD_ID), places],
        ]),
    ],
};

export const Narrow: Story = { parameters: { narrow: true } };
