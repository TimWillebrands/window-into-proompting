import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import DesktopWallpaper from '../DesktopWallpaper';
import { NOW, SAMPLE } from './fixtures';
import { NotificationBannersView } from './NotificationBanners';
import NotificationTrayButton from './NotificationTrayButton';

const meta = {
    title: 'Desktop/Notifications/Banners',
    component: NotificationBannersView,
    parameters: { layout: 'fullscreen' },
    args: {
        banners: SAMPLE.slice(0, 3),
        now: NOW,
        onOpen: fn(),
        onRetire: fn(),
    },
    decorators: [
        (Story) => (
            <div style={{ position: 'relative', height: 420 }}>
                <DesktopWallpaper />
                <Story />
                <div className="xp-taskbar fixed right-0 bottom-0 left-0 flex h-[30px] items-stretch">
                    <div className="xp-tray ml-auto">
                        <NotificationTrayButton
                            unread={3}
                            open={false}
                            onToggle={fn()}
                        />
                        <span>21:00</span>
                    </div>
                </div>
            </div>
        ),
    ],
} satisfies Meta<typeof NotificationBannersView>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Stacked: Story = {};

export const Single: Story = { args: { banners: SAMPLE.slice(0, 1) } };
