import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import MemoryGraphApp from './MemoryGraphApp';

const meta = {
    title: 'Apps/MemoryGraphApp',
    component: MemoryGraphApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div className="h-[720px] w-[1040px] p-6">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="memory-graph"
                    title="Memory Graph"
                    icon="memory-graph"
                    width={960}
                    height={640}
                    zIndex={1}
                    onMinimize={fn()}
                    onMaximize={fn()}
                    onClose={fn()}
                >
                    <Story />
                </WindowFrame>
            </div>
        ),
    ],
} satisfies Meta<typeof MemoryGraphApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
