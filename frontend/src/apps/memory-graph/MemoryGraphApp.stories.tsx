import type { Meta, StoryObj } from '@storybook/react-vite';
import { getGetWorldsPartyIdBoardQueryKey } from '#api/party-zone';
import { seeded } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WORLD_ID } from '../../lib/world';
import MemoryGraphApp from './MemoryGraphApp';

const meta = {
    title: 'Apps/MemoryGraphApp',
    component: MemoryGraphApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[720px] w-[480px] p-6'
                        : 'h-[720px] w-[1040px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow
                    id="memory-graph"
                    title="Memory Graph"
                    icon="memory-graph"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded([[getGetWorldsPartyIdBoardQueryKey(WORLD_ID), []]]),
    ],
} satisfies Meta<typeof MemoryGraphApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Narrow: Story = { parameters: { narrow: true } };
