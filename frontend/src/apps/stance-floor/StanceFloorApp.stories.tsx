import type { Meta, StoryObj } from '@storybook/react-vite';
import { fn } from 'storybook/test';
import WindowFrame from '../../components/desktop/WindowFrame';
import StanceFloorApp from './StanceFloorApp';

const meta = {
    title: 'Apps/StanceFloorApp',
    component: StanceFloorApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story) => (
            <div className="h-[600px] w-[840px] p-6">
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <WindowFrame
                    id="stance-floor"
                    title="Stance Floor"
                    icon="stance-floor"
                    width={760}
                    height={520}
                    zIndex={1}
                    onMinimize={fn()}
                    onMaximize={fn()}
                    onClose={fn()}
                >
                    <Story />
                </WindowFrame>
            </div>
        ),
    ],
} satisfies Meta<typeof StanceFloorApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
