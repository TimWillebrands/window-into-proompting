import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import MemoryGraphApp from './MemoryGraphApp';

// Story sees the storybook variant of `#api/party-zone` (party-zone.mock.ts), which
// already exports a hand-crafted `mockMemoryGraph` covering all six node kinds and all
// four edge kinds — exactly the dataset PRD user-story #32 calls for.
const meta = {
    title: 'Apps/MemoryGraphApp',
    component: MemoryGraphApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 1040,
                    height: 720,
                    padding: 24,
                    background: '#5C87C2',
                }}
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="memory-graph"
                    title="Memory Graph"
                    icon="🧠"
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
