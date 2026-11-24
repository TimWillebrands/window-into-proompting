import type { Meta, StoryObj } from '@storybook/react-vite';
import { seeded, withStoryLives } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
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
                <StoryWindow
                    id="messenger"
                    title="Messenger"
                    icon="messenger"
                    skin="msn"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded(),
    ],
} satisfies Meta<typeof MessengerApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Noir: Story = { globals: { scheme: 'noir' } };
