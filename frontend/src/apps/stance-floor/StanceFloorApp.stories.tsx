import type { Meta, StoryObj } from '@storybook/react-vite';
import StoryWindow from '../../components/desktop/StoryWindow';
import StanceFloorApp from './StanceFloorApp';

const meta = {
    title: 'Apps/StanceFloorApp',
    component: StanceFloorApp,
    parameters: { layout: 'fullscreen' },
    decorators: [
        (Story, { parameters }) => (
            <div
                className={
                    parameters.narrow
                        ? 'h-[600px] w-[480px] p-6'
                        : 'h-[600px] w-[840px] p-6'
                }
            >
                {/* biome-ignore lint/correctness/useUniqueElementIds: stories render single instance */}
                <StoryWindow
                    id="stance-floor"
                    title="Stance Floor"
                    icon="stance-floor"
                >
                    <Story />
                </StoryWindow>
            </div>
        ),
    ],
} satisfies Meta<typeof StanceFloorApp>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Narrow: Story = { parameters: { narrow: true } };
