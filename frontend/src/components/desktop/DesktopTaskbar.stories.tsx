import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import DesktopTaskbar from './DesktopTaskbar';

const meta = {
    title: 'Desktop/DesktopTaskbar',
    component: DesktopTaskbar,
    parameters: {
        layout: 'fullscreen',
        backgrounds: { default: 'xp-desktop' },
    },
    args: {
        windows: [],
        focusedId: null,
        onToggle: fn(),
        onClose: fn(),
    },
    decorators: [
        (Story) => (
            <div style={{ minHeight: 80, position: 'relative' }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof DesktopTaskbar>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Empty: Story = {};

export const SingleWindow: Story = {
    args: {
        windows: [{ id: 'w1', title: 'Notepad', icon: '📝' }],
        focusedId: 'w1',
    },
};

export const ManyWindows: Story = {
    args: {
        windows: [
            { id: 'w1', title: 'Personas', icon: '👤' },
            { id: 'w2', title: 'Group Chats', icon: '💬' },
            { id: 'w3', title: 'Control Panel', icon: '⚙️' },
            { id: 'w4', title: 'Calculator', icon: '🧮' },
            { id: 'w5', title: 'Solitaire', icon: '🃏' },
        ],
        focusedId: 'w3',
    },
};
