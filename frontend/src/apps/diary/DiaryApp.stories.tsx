import type { Meta, StoryObj } from '@storybook/react-vite';
import { getGetWorldsPartyIdDiaryPersonaIdQueryKey } from '#api/party-zone';
import type { DiaryEntry } from '../../api/model';
import { seeded, withStoryLives } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WindowInstanceProvider } from '../../components/desktop/window-instance-context';
import { WORLD_ID } from '../../lib/world';
import DiaryApp from './DiaryApp';

const now = Date.now();

const entries: DiaryEntry[] = [
    {
        id: 'entry-1',
        body: 'Somebody took the kettle from the café. I left a note on the Board, and nobody owned up. I suspect Hedy, but I like her too much to say so out loud.',
        mood: 'curious',
        createdAt: now - 3_600_000,
    },
    {
        id: 'entry-2',
        body: 'Linus told me about the lighthouse shelf in the library. I went, read for an hour and forgot to eat lunch.',
        mood: 'content',
        createdAt: now - 86_400_000,
    },
    {
        id: 'entry-3',
        body: 'Quiet day.',
        createdAt: now - 2 * 86_400_000,
    },
];

const meta = {
    title: 'Apps/DiaryApp',
    component: DiaryApp,
    parameters: { layout: 'fullscreen' },
    beforeEach: withStoryLives,
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[540px] w-[400px] p-6'
                        : 'h-[540px] w-[680px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow id="diary" title="Diaries — Notepad" icon="diary">
                    <WindowInstanceProvider windowId="diary">
                        <Story />
                    </WindowInstanceProvider>
                </StoryWindow>
            </div>
        ),
        seeded([
            [
                getGetWorldsPartyIdDiaryPersonaIdQueryKey(
                    WORLD_ID,
                    'persona-ada',
                ),
                entries,
            ],
            [
                getGetWorldsPartyIdDiaryPersonaIdQueryKey(
                    WORLD_ID,
                    'persona-linus',
                ),
                [],
            ],
        ]),
    ],
} satisfies Meta<typeof DiaryApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Empty: Story = { args: { personaId: 'persona-linus' } };

export const Narrow: Story = { parameters: { narrow: true } };
