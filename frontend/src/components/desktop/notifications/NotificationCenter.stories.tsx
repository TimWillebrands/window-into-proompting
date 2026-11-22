import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import DesktopWallpaper from '../DesktopWallpaper';
import { NOW, SAMPLE } from './fixtures';
import { NotificationCenterPanel } from './NotificationCenter';

const meta = {
    title: 'Desktop/Notifications/Center',
    component: NotificationCenterPanel,
    parameters: { layout: 'fullscreen' },
    args: {
        open: true,
        items: SAMPLE,
        now: NOW,
        onClose: fn(),
        onOpen: fn(),
        onDismiss: fn(),
        onClearAll: fn(),
    },
    decorators: [
        (Story) => (
            <div style={{ position: 'relative', height: 640 }}>
                <DesktopWallpaper />
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof NotificationCenterPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Grouped: Story = {};

export const Empty: Story = { args: { items: [] } };
