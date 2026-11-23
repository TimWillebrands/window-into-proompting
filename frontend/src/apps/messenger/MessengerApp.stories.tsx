import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import { seeded, withStoryLives } from '../../components/chat/story-seed';
import WindowFrame from '../../components/desktop/WindowFrame';
import MessengerApp from './MessengerApp';

const meta = {
    title: 'Apps/MessengerApp',
    component: MessengerApp,
    parameters: { layout: 'fullscreen' },
    beforeEach: withStoryLives,
    decorators: [
        (Story) => (
            <div className="h-[600px] w-[360px] p-6">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="messenger"
                    title="Messenger"
                    icon="/img/icons/messenger.ico"
                    width={320}
                    height={560}
                    zIndex={1}
                    onMinimize={fn()}
                    onMaximize={fn()}
                    onClose={fn()}
                >
                    <Story />
                </WindowFrame>
            </div>
        ),
        seeded(),
    ],
} satisfies Meta<typeof MessengerApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
