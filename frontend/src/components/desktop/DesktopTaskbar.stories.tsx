import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import DesktopTaskbar from './DesktopTaskbar';

const meta = {
    title: 'Desktop/DesktopTaskbar',
    component: DesktopTaskbar,
    parameters: { layout: 'fullscreen' },
    args: {
        windows: [
            { id: 'w1', title: 'Personas', icon: '/img/icons/personas.ico' },
            {
                id: 'w2',
                title: 'Chat Rooms',
                icon: '/img/icons/chat-rooms.ico',
            },
            {
                id: 'w3',
                title: 'Control Panel',
                icon: '/img/icons/control-panel.ico',
                minimized: true,
            },
        ],
        focusedId: 'w2',
        startMenuOpen: false,
        onToggle: fn(),
        onClose: fn(),
        onStartClick: fn(),
    },
    decorators: [
        (Story) => (
            <div className="relative min-h-20">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof DesktopTaskbar>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WithWindows: Story = {};

export const Empty: Story = { args: { windows: [], focusedId: null } };
