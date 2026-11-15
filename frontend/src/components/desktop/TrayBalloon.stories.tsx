import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import NotificationTrayIcon from './NotificationTrayIcon';
import { TrayBalloonView } from './TrayBalloon';

const meta = {
    title: 'Desktop/TrayBalloon',
    component: TrayBalloonView,
    parameters: {
        layout: 'fullscreen',
        backgrounds: { default: 'xp-desktop' },
    },
    args: {
        right: 44,
        bottom: 40,
        onOpen: fn(),
        onClose: fn(),
        balloon: {
            id: 'a',
            title: 'Ana sent you 2 messages',
            body: 'are you coming to the pub quiz tonight?',
            icon: '💬',
            items: [],
        },
    },
    decorators: [
        (Story) => (
            <div style={{ minHeight: 220, position: 'relative' }}>
                <Story />
                <div className="xp-taskbar fixed right-0 bottom-0 left-0 flex h-[30px] items-stretch">
                    <div className="xp-tray ml-auto">
                        <NotificationTrayIcon unread={3} onOpen={fn()} />
                        <span>21:47</span>
                    </div>
                </div>
            </div>
        ),
    ],
} satisfies Meta<typeof TrayBalloonView>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Single: Story = {};

export const WhileYouWereAway: Story = {
    args: {
        balloon: {
            id: 'b',
            title: 'While you were away',
            body: '• Ana sent you 2 messages\n• Bo and Ana replied on your post\n• Cas gave you Respect!\n…and 2 more',
            icon: 'ℹ️',
            items: [],
        },
    },
};
