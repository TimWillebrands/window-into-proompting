import type { Meta, StoryObj } from '@storybook/react-vite';
import {
    getGetWorldsPartyIdRelationsDoorsQueryKey,
    getGetWorldsPartyIdRelationsQueryKey,
} from '#api/party-zone';
import type { DoorRow, Relation } from '../../api/model';
import { RelationKind } from '../../api/model';
import {
    STORY_PLAYER,
    seeded,
    withStoryLives,
} from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WORLD_ID } from '../../lib/world';
import RelationsApp from './RelationsApp';

const relations: Relation[] = [
    {
        id: 'rel-1',
        aId: 'persona-ada',
        bId: 'persona-linus',
        kind: RelationKind.Together,
        story: 'met at the library',
        since: 0,
        endedAt: null,
        liveTogether: false,
    },
    {
        id: 'rel-2',
        aId: 'persona-hedy',
        bId: STORY_PLAYER,
        kind: RelationKind.Friends,
        story: null,
        since: 0,
        endedAt: null,
        liveTogether: true,
    },
    {
        id: 'rel-3',
        aId: 'persona-ada',
        bId: 'persona-hedy',
        kind: RelationKind.Married,
        story: 'you dated in high school',
        since: 0,
        endedAt: 1,
        liveTogether: false,
    },
];

const doors: DoorRow[] = [
    {
        place: "Ada's flat",
        host: 'Ada',
        visitor: 'Linus',
        doorsteps: 3,
        hasKey: false,
        visitorWant: 'a cup of tea',
        hostWant: null,
    },
    {
        place: 'The café',
        host: 'Hedy',
        visitor: 'Grace',
        doorsteps: 0,
        hasKey: true,
        visitorWant: null,
        hostWant: 'help with the picnic',
    },
];

const meta = {
    title: 'Apps/RelationsApp',
    component: RelationsApp,
    parameters: { layout: 'fullscreen' },
    beforeEach: withStoryLives,
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[520px] w-[480px] p-6'
                        : 'h-[520px] w-[780px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow id="relations" title="Relations" icon="relations">
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded([
            [getGetWorldsPartyIdRelationsQueryKey(WORLD_ID), relations],
            [getGetWorldsPartyIdRelationsDoorsQueryKey(WORLD_ID), doors],
        ]),
    ],
} satisfies Meta<typeof RelationsApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Narrow: Story = { parameters: { narrow: true } };

export const Empty: Story = {
    decorators: [
        seeded([
            [getGetWorldsPartyIdRelationsQueryKey(WORLD_ID), []],
            [getGetWorldsPartyIdRelationsDoorsQueryKey(WORLD_ID), []],
        ]),
    ],
};
