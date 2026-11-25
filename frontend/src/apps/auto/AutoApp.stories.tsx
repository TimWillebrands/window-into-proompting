import type { Decorator, Meta, StoryObj } from '@storybook/react-vite';
import { useQueryClient } from '@tanstack/react-query';
import { seeded } from '../../components/chat/story-seed';
import StoryWindow from '../../components/desktop/StoryWindow';
import { WORLD_ID } from '../../lib/world';
import AutoApp from './AutoApp';

const onAuto: Decorator = (Story) => {
    const client = useQueryClient();
    client.setQueryData(['world-player', WORLD_ID, 'story-user'], {
        playerId: 'player-grace',
        name: 'Grace',
        auto: true,
        mission: 'Find a job.',
        isAdmin: true,
    });
    return <Story />;
};

const meta = {
    title: 'Apps/AutoApp',
    component: AutoApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div className="h-[340px] w-[480px] p-6">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow id="auto" title="Auto Mode" icon="auto">
                    <Story />
                </StoryWindow>
            </div>
        ),
        seeded(),
    ],
} satisfies Meta<typeof AutoApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const OnAuto: Story = { decorators: [onAuto] };
