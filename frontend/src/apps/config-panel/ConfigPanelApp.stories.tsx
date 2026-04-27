import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import ConfigPanelApp from './ConfigPanelApp';

const meta = {
    title: 'Apps/ConfigPanelApp',
    component: ConfigPanelApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div
                style={{
                    width: 800,
                    height: 560,
                    padding: 24,
                    background: '#5C87C2',
                }}
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="config-panel"
                    title="Control Panel"
                    icon="⚙️"
                    width={760}
                    height={520}
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
} satisfies Meta<typeof ConfigPanelApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
