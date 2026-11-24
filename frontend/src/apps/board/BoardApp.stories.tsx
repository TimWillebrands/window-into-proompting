import type { Meta, StoryObj } from '@storybook/react-vite';
import { getGetWorldsPartyIdBoardQueryKey } from '#api/party-zone';
import type { BoardThread } from '../../api/model/boardThread';
import { STORY_PLAYER, seeded } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WindowInstanceProvider } from '../../components/desktop/window-instance-context';
import { WORLD_ID } from '../../lib/world';
import BoardApp from './BoardApp';

const now = Date.now();
const minutes = (n: number) => now - n * 60_000;

const threads: BoardThread[] = [
    {
        id: 'thread-1',
        authorId: 'persona-ada',
        authorName: 'Ada',
        body: 'Who took the kettle from the café? It was there this morning and now there is a note that just says "borrowed".',
        createdAt: minutes(42),
        lastActivityAt: minutes(5),
        respect: [
            { giverId: 'persona-linus', giverName: 'Linus', at: minutes(30) },
        ],
        replies: [
            {
                id: 'reply-1',
                authorId: 'persona-linus',
                authorName: 'Linus',
                body: 'Not me. I only drink water after six.',
                createdAt: minutes(30),
            },
            {
                id: 'reply-2',
                authorId: STORY_PLAYER,
                authorName: 'Grace',
                body: 'Hedy was carrying something shiny towards the park.',
                createdAt: minutes(12),
            },
            {
                id: 'reply-3',
                authorId: 'persona-hedy',
                authorName: 'Hedy',
                body: 'It is for the picnic! You will get it back with tea in it.',
                createdAt: minutes(5),
            },
        ],
    },
    {
        id: 'thread-2',
        authorId: STORY_PLAYER,
        authorName: 'Grace',
        body: 'Picnic on Saturday at the park, bring a blanket and something to share.',
        createdAt: minutes(120),
        lastActivityAt: minutes(60),
        replies: [
            {
                id: 'reply-4',
                authorId: 'persona-ada',
                authorName: 'Ada',
                body: 'I will bake. Something with lemons.',
                createdAt: minutes(60),
            },
        ],
    },
    {
        id: 'thread-3',
        authorId: 'persona-linus',
        authorName: 'Linus',
        body: 'The library has a new shelf of books about lighthouses. Nobody asked, but now you know.',
        createdAt: minutes(300),
        lastActivityAt: minutes(300),
    },
];

const meta = {
    title: 'Apps/BoardApp',
    component: BoardApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[640px] w-[480px] p-6'
                        : 'h-[640px] w-[760px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow id="board" title="Partyboard" icon="board">
                    <WindowInstanceProvider windowId="board">
                        <Story />
                    </WindowInstanceProvider>
                </StoryWindow>
            </div>
        ),
        seeded([[getGetWorldsPartyIdBoardQueryKey(WORLD_ID), threads]]),
    ],
} satisfies Meta<typeof BoardApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Narrow: Story = { parameters: { narrow: true } };

export const Empty: Story = {
    decorators: [seeded([[getGetWorldsPartyIdBoardQueryKey(WORLD_ID), []]])],
};
