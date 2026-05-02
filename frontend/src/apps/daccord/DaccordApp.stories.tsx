import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import DaccordApp from './DaccordApp';

const meta = {
    title: 'Apps/DaccordApp',
    component: DaccordApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 880,
                    height: 580,
                    padding: 24,
                    background: '#5C87C2',
                }}
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="open-party"
                    title="D'Accord — Chat Rooms"
                    icon="💬"
                    width={840}
                    height={540}
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
} satisfies Meta<typeof DaccordApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
