import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import ChatManagerApp from './ChatManagerApp';

const meta = {
    title: 'Apps/ChatManagerApp',
    component: ChatManagerApp,
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
                    title="Group Chats"
                    icon="💬"
                    width={840}
                    height={540}
                    zIndex={1}
                    onMinimize={fn()}
                    onRestore={fn()}
                    onClose={fn()}
                >
                    <Story />
                </WindowFrame>
            </div>
        ),
    ],
} satisfies Meta<typeof ChatManagerApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
