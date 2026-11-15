import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { NotificationList } from './NotificationsApp';

const now = Date.UTC(2026, 10, 15, 20, 0);
const min = 60_000;

const meta = {
    title: 'Apps/Notifications',
    component: NotificationList,
    args: {
        now,
        onOpen: fn(),
        fresh: new Set<string>(),
        items: [
            {
                id: 'dm',
                kind: 'dm',
                at: now - 2 * min,
                title: 'Ana sent you 2 messages',
                body: 'are you coming to the pub quiz tonight?',
                app: 'messenger',
                targetId: 'c1',
                fromName: 'Ana',
                unread: true,
            },
            {
                id: 'reply',
                kind: 'board-reply',
                at: now - 15 * min,
                title: 'Bo and Ana replied on your post',
                body: 'the bakery on the corner does them better',
                app: 'board',
                targetId: 't1',
                fromName: 'Bo',
                unread: true,
            },
            {
                id: 'respect',
                kind: 'respect',
                at: now - 90 * min,
                title: 'Cas gave you Respect!',
                body: 'first rain of the autumn and I forgot my coat',
                app: 'board',
                targetId: 't2',
                fromName: 'Cas',
            },
            {
                id: 'gathering',
                kind: 'gathering',
                at: now - 4 * 60 * min,
                title: '“Pub quiz” is starting',
                body: "At The Anchor. You said you'd be there.",
                app: 'places',
                fromName: 'Ana',
            },
            {
                id: 'gift',
                kind: 'gift',
                at: now - 26 * 60 * min,
                title: 'Denise gave you a present',
                body: "Red scarf. It's in your Wardrobe.",
                app: 'wardrobe',
                fromName: 'Denise',
            },
        ],
    },
    decorators: [
        (Story) => (
            <div style={{ width: 560, height: 460 }}>
                <Story />
            </div>
        ),
    ],
} satisfies Meta<typeof NotificationList>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Recent: Story = {};

export const Empty: Story = { args: { items: [], fresh: new Set() } };
