import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import DesktopIcon from './DesktopIcon';

const meta = {
    title: 'Desktop/DesktopIcon',
    component: DesktopIcon,
    parameters: {
        layout: 'fullscreen',
        backgrounds: { default: 'xp-desktop' },
    },
    args: {
        icon: '/img/1012.ico',
        label: 'Personas',
        position: { x: 20, y: 20 },
        onActivate: fn(),
    },
    decorators: [
        (Story) => (
            <div
                style={{
                    position: 'relative',
                    width: 320,
                    height: 320,
                    background: '#5C87C2',
                }}
            >
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof DesktopIcon>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const LongLabel: Story = {
    args: {
        label: 'A Very Long Desktop Icon Label',
        position: { x: 20, y: 20 },
    },
};

export const Grid: Story = {
    name: 'Multiple icons',
    render: (args) => (
        <>
            <DesktopIcon
                {...args}
                label="Personas"
                position={{ x: 20, y: 20 }}
            />
            <DesktopIcon
                {...args}
                label="Chat Rooms"
                position={{ x: 20, y: 130 }}
            />
            <DesktopIcon
                {...args}
                label="Control Panel"
                position={{ x: 20, y: 240 }}
            />
        </>
    ),
};
