import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from './WindowFrame';

const meta = {
    title: 'Desktop/WindowFrame',
    component: WindowFrame,
    parameters: { layout: 'centered', backgrounds: { default: 'xp-desktop' } },
    args: {
        id: 'demo',
        title: 'Untitled — Notepad',
        icon: '📝',
        width: 600,
        height: 400,
        zIndex: 1,
        onMinimize: fn(),
        onRestore: fn(),
        onClose: fn(),
        children: (
            <div style={{ padding: 12 }}>
                Window body content goes here. xp.css renders chrome.
            </div>
        ),
    },
    decorators: [
        (Story) => (
            <div style={{ width: 600, height: 400 }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof WindowFrame>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const NarrowWindow: Story = {
    args: {
        width: 320,
        height: 240,
        title: 'Calculator',
        icon: '🧮',
    },
    decorators: [
        (Story) => (
            <div style={{ width: 320, height: 240 }}>
                <Story />
            </div>
        ),
    ],
};

export const SuspenseFallback: Story = {
    name: 'Suspense fallback',
    args: {
        title: 'Loading…',
        children: <SuspendForever />,
    },
};

function SuspendForever(): never {
    throw new Promise<never>(() => {});
}
