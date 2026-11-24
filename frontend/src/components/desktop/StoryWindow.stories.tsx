import type { Meta, StoryObj } from '@storybook/react-vite';
import StoryWindow from './StoryWindow';

const meta = {
    title: 'Desktop/StoryWindow',
    component: StoryWindow,
    parameters: { layout: 'centered' },
    args: {
        id: 'demo',
        title: 'Untitled - Notepad',
        icon: '/img/icons/notepad.ico',
        children: <div className="p-3">Window body content goes here.</div>,
    },
    decorators: [
        (Story) => (
            <div className="h-[400px] w-[600px]">
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof StoryWindow>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

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
